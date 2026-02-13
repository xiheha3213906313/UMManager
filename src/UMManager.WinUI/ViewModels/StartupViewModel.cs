﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System.Globalization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;
using UMManager.Core.Helpers;
using UMManager.Core.Services;
using UMManager.Core.Services.CommandService;
using UMManager.Core.Services.GameBanana;
using UMManager.WinUI.Contracts.Services;
using UMManager.WinUI.Contracts.ViewModels;
using UMManager.WinUI.Helpers;
using UMManager.WinUI.Models.Options;
using UMManager.WinUI.Models.Settings;
using UMManager.WinUI.Services;
using UMManager.WinUI.Services.AppManagement;
using UMManager.WinUI.Services.Notifications;
using UMManager.WinUI.Validators.PreConfigured;
using UMManager.WinUI.ViewModels.SubVms;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace UMManager.WinUI.ViewModels;

public partial class StartupViewModel : ObservableRecipient, INavigationAware
{
    private readonly INavigationService _navigationService;
    private readonly ILogger _logger = Log.ForContext<StartupViewModel>();
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IWindowManagerService _windowManagerService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly IGameService _gameService;
    private readonly UserPreferencesService _userPreferencesService;
    private readonly SelectedGameService _selectedGameService;
    private readonly ModArchiveRepository _modArchiveRepository;
    private readonly CommandService _commandService;


    public PathPicker PathToModsFolderPicker { get; }
    public Uri ConfigFolderPath { get; } = new Uri(ConfigStorageHelper.GetConfigRoot());
    public string ConfigFolderPathText => ConfigStorageHelper.GetConfigRoot();

    [ObservableProperty]
    private GameComboBoxEntryVM _selectedGame = new GameComboBoxEntryVM(SupportedGames.Genshin)
    {
        GameName = "Genshin Impact",
        GameShortName = SupportedGames.Genshin.ToString(),
        GameIconPath = null!
    };

    [ObservableProperty] private string _modelImporterName = "Genshin-Impact-Model-Importer";
    [ObservableProperty] private string _modelImporterShortName = "GIMI";
    [ObservableProperty] private Uri _gameBananaUrl = new("https://gamebanana.com/games/8552");

    [ObservableProperty] private Uri _modelImporterUrl = new("https://github.com/SilentNightSound");

    public ObservableCollection<GameComboBoxEntryVM> Games { get; } = new();

    public StartupViewModel(INavigationService navigationService, ILocalSettingsService localSettingsService,
        IWindowManagerService windowManagerService, ISkinManagerService skinManagerService,
        SelectedGameService selectedGameService, IGameService gameService,
        UserPreferencesService userPreferencesService, ModArchiveRepository modArchiveRepository,
        CommandService commandService)
    {
        _navigationService = navigationService;
        _localSettingsService = localSettingsService;
        _windowManagerService = windowManagerService;
        _skinManagerService = skinManagerService;
        _selectedGameService = selectedGameService;
        _gameService = gameService;
        _userPreferencesService = userPreferencesService;
        _modArchiveRepository = modArchiveRepository;
        _commandService = commandService;

        PathToModsFolderPicker =
            new PathPicker(ModsFolderValidator.Validators);

        PathToModsFolderPicker.IsValidChanged +=
            (sender, args) => SaveStartupSettingsCommand.NotifyCanExecuteChanged();
    }


    private bool ValidStartupSettings() => PathToModsFolderPicker.IsValid;


    [RelayCommand(CanExecute = nameof(ValidStartupSettings))]
    private async Task SaveStartupSettings()
    {
        var modsFolderPath = PathToModsFolderPicker.Path?.Trim();
        if (modsFolderPath.IsNullOrEmpty() || !Directory.Exists(modsFolderPath))
            return;

        if (!FileSystemAccessHelper.CanReadWriteDirectory(modsFolderPath!))
        {
            var localizer = App.GetService<ILanguageLocalizer>();
            await _windowManagerService.ShowDialogAsync(new ContentDialog
            {
                Title = localizer.GetLocalizedStringOrDefault("Dialog.ModsFolderAccessDenied.Title",
                    defaultValue: "无法访问所选文件夹"),
                Content = localizer.GetLocalizedStringOrDefault("Dialog.ModsFolderAccessDenied.Text",
                    defaultValue: "所选 Mods 文件夹需要管理员权限才能访问或写入。\n\n请修改文件夹权限，或以管理员身份启动本软件后再选择该文件夹。"),
                CloseButtonText = localizer.GetLocalizedStringOrDefault("Common.Button.GotIt", defaultValue: "知道了")
            });
            return;
        }

        try
        {
            if (Directory.EnumerateFileSystemEntries(modsFolderPath!).Any())
            {
                var localizer = App.GetService<ILanguageLocalizer>();
                await _windowManagerService.ShowDialogAsync(new ContentDialog
                {
                    Title = localizer.GetLocalizedStringOrDefault("Dialog.ModsFolderNotEmpty.Title",
                        defaultValue: "Mods 文件夹不为空"),
                    Content = localizer.GetLocalizedStringOrDefault("Dialog.ModsFolderNotEmpty.Text",
                        defaultValue:
                        "所选 Mods 文件夹必须为空。请先清空此文件夹，然后再保存。后续请你自行将模组手动导入到对应角色的文件夹中。"),
                    CloseButtonText = localizer.GetLocalizedStringOrDefault("Common.Button.GotIt", defaultValue: "知道了")
                });
                return;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            var localizer = App.GetService<ILanguageLocalizer>();
            await _windowManagerService.ShowDialogAsync(new ContentDialog
            {
                Title = localizer.GetLocalizedStringOrDefault("Dialog.ModsFolderAccessDenied.Title",
                    defaultValue: "无法访问所选文件夹"),
                Content = localizer.GetLocalizedStringOrDefault("Dialog.ModsFolderAccessDenied.Text",
                    defaultValue: "所选 Mods 文件夹需要管理员权限才能访问或写入。\n\n请修改文件夹权限，或以管理员身份启动本软件后再选择该文件夹。"),
                CloseButtonText = localizer.GetLocalizedStringOrDefault("Common.Button.GotIt", defaultValue: "知道了")
            });
            return;
        }

        var gimiRootFolderPath = modsFolderPath.IsNullOrEmpty() ? null : Directory.GetParent(modsFolderPath!)?.FullName;

        var modManagerOptions = new ModManagerOptions()
        {
            GimiRootFolderPath = gimiRootFolderPath,
            ModsFolderPath = modsFolderPath,
            UnloadedModsFolderPath = null
        };

        await _selectedGameService.SetSelectedGame(SelectedGame.Value.ToString());

        await _gameService.InitializeAsync(
            Path.Combine(App.ASSET_DIR, "Games", await _selectedGameService.GetSelectedGameAsync()),
            _localSettingsService.ApplicationDataFolder);

        await _localSettingsService.SaveSettingAsync(ModManagerOptions.Section,
            modManagerOptions);
        _logger.Information("Saved startup settings: {@ModManagerOptions}", modManagerOptions);

        await _skinManagerService.InitializeAsync(modManagerOptions.ModsFolderPath!, null,
            modManagerOptions.GimiRootFolderPath);

        var modArchiveSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModArchiveSettings>(ModArchiveSettings.Key);

        var tasks = new List<Task>
        {
            _userPreferencesService.InitializeAsync(),
            _modArchiveRepository.InitializeAsync(_localSettingsService.ApplicationDataFolder,
                o => o.MaxDirectorySizeGb = modArchiveSettings.MaxLocalArchiveCacheSizeGb),
            _commandService.InitializeAsync(_localSettingsService.ApplicationDataFolder)
        };

        await Task.WhenAll(tasks);

        await _localSettingsService.SaveSettingAsync(ActivationService.IgnoreNewFolderStructureKey, true);


        _navigationService.NavigateTo(typeof(CharactersViewModel).FullName!, null, true);
        _windowManagerService.ResizeWindowPercent(_windowManagerService.MainWindow, 80, 80);
        _windowManagerService.MainWindow.CenterOnScreen();
        var notificationLocalizer = App.GetService<ILanguageLocalizer>();
        App.GetService<NotificationManager>().ShowNotification(
            notificationLocalizer.GetLocalizedStringOrDefault("Notification.StartupSettingsSaved.Title", defaultValue: "启动设置已保存"),
            string.Format(CultureInfo.CurrentUICulture,
                notificationLocalizer.GetLocalizedStringOrDefault("Notification.StartupSettingsSaved.Message",
                    defaultValue: "启动设置已保存到“{0}”。")!,
                _localSettingsService.GameScopedSettingsLocation),
            TimeSpan.FromSeconds(7));
    }


    [RelayCommand]
    private async Task BrowseModsFolderAsync()
        => await PathToModsFolderPicker.BrowseFolderPathAsync(App.MainWindow);

    public async void OnNavigatedTo(object parameter)
    {
        _windowManagerService.ResizeWindowPercent(_windowManagerService.MainWindow, 50, 60);
        _windowManagerService.MainWindow.CenterOnScreen();

        var settings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModManagerOptions>(ModManagerOptions.Section);

        await SetGameComboBoxValues();

        SetSelectedGame(await _selectedGameService.GetSelectedGameAsync());
        await SetGameInfo(SelectedGame.Value.ToString());
        SetPaths(settings);
    }

    [RelayCommand]
    private async Task SetGameAsync(string game)
    {
        if (game.IsNullOrEmpty())
            return;

        await _selectedGameService.SetSelectedGame(game);
        SetSelectedGame(game);

        var settings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModManagerOptions>(ModManagerOptions.Section);

        await SetGameInfo(game);
        SetPaths(settings);
    }


    private async Task SetGameInfo(string game)
    {
        var languageCode = App.GetService<ILanguageLocalizer>().CurrentLanguage.LanguageCode;
        var gameInfo = await GameService.GetGameInfoAsync(Enum.Parse<SupportedGames>(game), languageCode);

        if (gameInfo is null)
        {
            _logger.Error("Game info for {Game} is null", game);
            return;
        }

        ModelImporterName = gameInfo.GameModelImporterName;
        ModelImporterShortName = gameInfo.GameModelImporterShortName;
        GameBananaUrl = gameInfo.GameBananaUrl;
        ModelImporterUrl = gameInfo.GameModelImporterUrl;
    }

    private async Task SetGameComboBoxValues()
    {
        var languageCode = App.GetService<ILanguageLocalizer>().CurrentLanguage.LanguageCode;
        foreach (var supportedGame in Enum.GetValues<SupportedGames>())
        {
            var gameInfo = await GameService.GetGameInfoAsync(supportedGame, languageCode);
            if (gameInfo is null)
                continue;

            Games.Add(new GameComboBoxEntryVM(supportedGame)
            {
                GameIconPath = new Uri(gameInfo.GameIcon),
                GameName = gameInfo.GameName,
                GameShortName = gameInfo.GameShortName
            });
        }
    }

    private void SetSelectedGame(string game)
    {
        var selectedGame = Games.FirstOrDefault(g => g.Value.ToString() == game);
        if (selectedGame is not null)
            SelectedGame = selectedGame;
    }


    private void SetPaths(ModManagerOptions settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ModsFolderPath))
            PathToModsFolderPicker.Path = settings.ModsFolderPath;
        else
            PathToModsFolderPicker.Path = "";
    }


    public void OnNavigatedFrom()
    {
    }
}
