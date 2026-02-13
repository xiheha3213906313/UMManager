﻿using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;
using UMManager.Core.Services;
using UMManager.Core.Services.CommandService;
using UMManager.Core.Services.GameBanana;
using UMManager.WinUI.Contracts.Services;
using UMManager.WinUI.Models.Options;
using UMManager.WinUI.Models.Settings;
using UMManager.WinUI.Services.AppManagement;
using UMManager.WinUI.ViewModels;
using Microsoft.UI.Xaml;

namespace UMManager.WinUI.Activation;

/// <summary>
/// FirstTimeStartupActivationHandler is completely wrong name for this class. This is the default startup handler.
/// </summary>
public class FirstTimeStartupActivationHandler : ActivationHandler<LaunchActivatedEventArgs>
{
    private readonly INavigationService _navigationService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly IGameService _gameService;
    private readonly UserPreferencesService _userPreferencesService;
    private readonly SelectedGameService _selectedGameService;
    private readonly ModArchiveRepository _modArchiveRepository;
    private readonly CommandService _commandService;
    public override string ActivationName { get; } = "RegularStartup";

    public FirstTimeStartupActivationHandler(INavigationService navigationService,
        ILocalSettingsService localSettingsService,
        ISkinManagerService skinManagerService, IGameService gameService, SelectedGameService selectedGameService,
        UserPreferencesService userPreferencesService,
        ModArchiveRepository modArchiveRepository, CommandService commandService)
    {
        _navigationService = navigationService;
        _localSettingsService = localSettingsService;
        _skinManagerService = skinManagerService;
        _gameService = gameService;
        _selectedGameService = selectedGameService;
        _userPreferencesService = userPreferencesService;
        _modArchiveRepository = modArchiveRepository;
        _commandService = commandService;
    }

    protected override bool CanHandleInternal(LaunchActivatedEventArgs args)
    {
        var options = Task
            .Run(async () => await _localSettingsService.ReadSettingAsync<ModManagerOptions>(ModManagerOptions.Section))
            .GetAwaiter().GetResult();

        return Directory.Exists(options?.ModsFolderPath);
    }

    protected override async Task HandleInternalAsync(LaunchActivatedEventArgs args)
    {
        var modManagerOptions =
            await _localSettingsService.ReadSettingAsync<ModManagerOptions>(ModManagerOptions.Section);

        var gameServiceOptions = new InitializationOptions
        {
            AssetsDirectory = Path.Combine(App.ASSET_DIR, "Games",
                await _selectedGameService.GetSelectedGameAsync()),
            LocalSettingsDirectory = _localSettingsService.ApplicationDataFolder,
            CharacterSkinsAsCharacters = modManagerOptions?.CharacterSkinsAsCharacters ?? false
        };

        await Task.Run(async () =>
        {
            var modArchiveSettings =
                await _localSettingsService.ReadOrCreateSettingAsync<ModArchiveSettings>(ModArchiveSettings.Key);

            await _gameService.InitializeAsync(gameServiceOptions).ConfigureAwait(false);

            var gimiRootFolderPath =
                modManagerOptions?.GimiRootFolderPath ??
                Directory.GetParent(modManagerOptions!.ModsFolderPath!)?.FullName;

            await _skinManagerService.InitializeAsync(modManagerOptions!.ModsFolderPath!, null, gimiRootFolderPath)
                .ConfigureAwait(false);

            var tasks = new List<Task>
            {
                _userPreferencesService.InitializeAsync(),
                _modArchiveRepository.InitializeAsync(_localSettingsService.ApplicationDataFolder,
                    o => o.MaxDirectorySizeGb = modArchiveSettings.MaxLocalArchiveCacheSizeGb),
                _commandService.InitializeAsync(_localSettingsService.ApplicationDataFolder)
            };

            await Task.WhenAll(tasks).ConfigureAwait(false);
        });


        _navigationService.NavigateTo(typeof(CharactersViewModel).FullName!,
            _gameService.GetCategories().First(c => c.InternalNameEquals("Character")), true);
    }
}
