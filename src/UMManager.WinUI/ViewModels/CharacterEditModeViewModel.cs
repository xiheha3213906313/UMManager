using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.GamesService.Models;
using UMManager.WinUI.Contracts.ViewModels;
using UMManager.WinUI.Services;
using UMManager.WinUI.ViewModels.Messages;

namespace UMManager.WinUI.ViewModels;

public sealed partial class CharacterEditModeViewModel(
    IGameService gameService,
    ISkinManagerService skinManagerService)
    : ObservableRecipient, INavigationAware, IRecipient<CustomCharacterDeletedMessage>
{
    private readonly IGameService _gameService = gameService;
    private readonly ISkinManagerService _skinManagerService = skinManagerService;

    private readonly List<CharacterEntryVm> _allCharacters = new();
    public ObservableCollection<CharacterEntryVm> FilteredCharacters { get; } = new();

    [ObservableProperty] private CharacterEntryVm? _selectedCharacter;

    [ObservableProperty] private string _searchText = string.Empty;

    public async void OnNavigatedTo(object parameter)
    {
        Messenger.RegisterAll(this);
        string? selectInternalName = parameter switch
        {
            InternalName id => id.Id,
            string s => s,
            _ => null
        };

        await LoadAsync(selectInternalName);
    }

    public void OnNavigatedFrom()
    {
        Messenger.UnregisterAll(this);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync(string? selectInternalName = null)
    {
        _allCharacters.Clear();

        var enabled = _gameService.GetCharacters().ToList();
        var disabled = _gameService.GetDisabledCharacters().ToList();

        foreach (var character in enabled)
        {
            var modList = _skinManagerService.GetCharacterModList(character);
            var enabledModsCount = modList.Mods.Count(m => m.IsEnabled);
            _allCharacters.Add(CharacterEntryVm.FromCharacter(character, modList.Mods.Count.ToString(), enabledModsCount.ToString(), isDisabled: false));
        }

        foreach (var character in disabled)
        {
            _allCharacters.Add(CharacterEntryVm.FromCharacter(character, "-", "-", isDisabled: true));
        }

        ApplyFilter(selectInternalName);
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter(string? preferSelectInternalName = null)
    {
        var query = SearchText?.Trim();

        IEnumerable<CharacterEntryVm> filtered = _allCharacters;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(c =>
                c.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.InternalName.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        FilteredCharacters.Clear();
        foreach (var c in filtered)
            FilteredCharacters.Add(c);

        var desiredInternalName = preferSelectInternalName ?? SelectedCharacter?.InternalName.Id;
        if (!string.IsNullOrWhiteSpace(desiredInternalName))
        {
            SelectedCharacter =
                FilteredCharacters.FirstOrDefault(c => c.InternalNameEquals(desiredInternalName)) ??
                FilteredCharacters.FirstOrDefault();
            return;
        }

        SelectedCharacter = FilteredCharacters.FirstOrDefault();
    }

    public void Receive(CustomCharacterDeletedMessage message)
    {
        var selectInternalName = SelectedCharacter?.InternalName.Id;
        if (!string.IsNullOrWhiteSpace(selectInternalName) &&
            selectInternalName.Equals(message.internalName, StringComparison.OrdinalIgnoreCase))
        {
            selectInternalName = null;
        }

        _ = LoadAsync(selectInternalName);
    }
}

public sealed partial class CharacterEntryVm : ObservableObject
{
    public InternalName InternalName { get; }

    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private Uri? _imageUri;
    [ObservableProperty] private string _modsCountText = string.Empty;
    [ObservableProperty] private string _enabledModsCountText = string.Empty;
    [ObservableProperty] private bool _isDisabled;

    private CharacterEntryVm(InternalName internalName)
    {
        InternalName = internalName;
    }

    public bool InternalNameEquals(string internalName) =>
        InternalName.Equals(internalName);

    public static CharacterEntryVm FromCharacter(ICharacter character, string modsCount, string enabledModsCount, bool isDisabled)
    {
        return new CharacterEntryVm(character.InternalName)
        {
            DisplayName = character.DisplayName,
            ImageUri = character.ImageUri ?? ImageHandlerService.StaticPlaceholderImageUri,
            ModsCountText = $"模组数：{modsCount}",
            EnabledModsCountText = $"启用模组数：{enabledModsCount}",
            IsDisabled = isDisabled
        };
    }
}
