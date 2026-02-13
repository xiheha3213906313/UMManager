using System.Globalization;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Windows.Storage;
using Windows.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkitWrapper;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.GamesService.Models;
using UMManager.Core.Helpers;
using UMManager.Core.Services;
using UMManager.Core.Services.GameBanana;
using UMManager.WinUI.Contracts.Services;
using UMManager.WinUI.Contracts.ViewModels;
using UMManager.WinUI.Helpers;
using UMManager.WinUI.Models;
using UMManager.WinUI.Models.Settings;
using UMManager.WinUI.Models.ViewModels;
using UMManager.WinUI.Services;
using UMManager.WinUI.Services.ModHandling;
using UMManager.WinUI.Services.Notifications;
using UMManager.WinUI.ViewModels.CharacterDetailsViewModels;
using UMManager.WinUI.ViewModels.Messages;
using UMManager.WinUI.ViewModels.SubVms;
using UMManager.WinUI.Views.CharacterDetailsPages;
using Serilog;

namespace UMManager.WinUI.ViewModels;

public partial class CharactersViewModel : ObservableRecipient, INavigationAware
    , IRecipient<UiCategoriesChangedMessage>
    , IRecipient<CharacterTagsChangedMessage>
{
    private readonly IGameService _gameService;
    private readonly ILogger _logger;
    private readonly INavigationService _navigationService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILanguageLocalizer _localizer;
    private readonly ModDragAndDropService _modDragAndDropService;
    private readonly ModNotificationManager _modNotificationManager;
    private readonly ModCrawlerService _modCrawlerService;
    private readonly ModSettingsService _modSettingsService;
    private readonly ModUpdateAvailableChecker _modUpdateAvailableChecker;
    private readonly BusyService _busyService;

    public NotificationManager NotificationManager { get; }
    public ElevatorService ElevatorService { get; }


    private IReadOnlyList<IModdableObject> _characters = new List<IModdableObject>();

    private IReadOnlyList<CharacterGridItemModel> _backendCharacters = new List<CharacterGridItemModel>();
    public ObservableCollection<CharacterGridItemModel> SuggestionsBox { get; } = new();
    public ObservableCollection<CharacterGridItemModel> Characters { get; } = new();

    private readonly Dictionary<string, HashSet<Guid>> _characterTags = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<UiCategoryFilterVm> UiCategories { get; } = new();
    [ObservableProperty] private UiCategoryFilterVm? _selectedUiCategory;

    private string _searchText = string.Empty;

    public ObservableCollection<GridItemSortingMethod> SortingMethods { get; } = new();

    [ObservableProperty] private GridItemSortingMethod _selectedSortingMethod;
    [ObservableProperty] private bool _sortByDescending;

    [ObservableProperty] private bool _canCheckForUpdates = false;

    [ObservableProperty] private Uri? _gameBananaLink;

    [ObservableProperty] private string _categoryPageTitle = string.Empty;
    [ObservableProperty] private string _searchBoxPlaceHolder = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    private bool _isNavigating = true;

    public CharactersViewModel(IGameService gameService, ILogger logger, INavigationService navigationService,
        ISkinManagerService skinManagerService, ILocalSettingsService localSettingsService,
        NotificationManager notificationManager, ElevatorService elevatorService,
        ModDragAndDropService modDragAndDropService, ModNotificationManager modNotificationManager,
        ModCrawlerService modCrawlerService, ModSettingsService modSettingsService,
        ModUpdateAvailableChecker modUpdateAvailableChecker,
        BusyService busyService, ILanguageLocalizer localizer)
    {
        _gameService = gameService;
        _logger = logger.ForContext<CharactersViewModel>();
        _navigationService = navigationService;
        _skinManagerService = skinManagerService;
        _localSettingsService = localSettingsService;
        NotificationManager = notificationManager;
        ElevatorService = elevatorService;
        _modDragAndDropService = modDragAndDropService;
        _modNotificationManager = modNotificationManager;
        _modCrawlerService = modCrawlerService;
        _modSettingsService = modSettingsService;
        _modUpdateAvailableChecker = modUpdateAvailableChecker;
        _busyService = busyService;
        _localizer = localizer;

        ElevatorService.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ElevatorService.ElevatorStatus))
                RefreshModsInGameCommand.NotifyCanExecuteChanged();
        };

        _modNotificationManager.OnModNotification += (_, _) =>
            App.MainWindow.DispatcherQueue.EnqueueAsync(RefreshNotificationsAsync);
        GameBananaLink = _gameService.GameBananaUrl;

        CanCheckForUpdates = _modUpdateAvailableChecker.IsReady;
        _modUpdateAvailableChecker.OnUpdateCheckerEvent += (_, _) =>
        {
            App.MainWindow.DispatcherQueue.EnqueueAsync(() =>
            {
                CanCheckForUpdates = _modUpdateAvailableChecker.IsReady;
                return Task.CompletedTask;
            });
        };

        IsBusy = _busyService.IsPageBusy(this);
    }

    public event EventHandler<ScrollToCharacterArgs>? OnScrollToCharacter;

    private CharacterGridItemModel NoCharacterFound =>
        new(new Character("None", $"未找到{_category.DisplayNamePlural}..."));

    public void AutoSuggestBox_TextChanged(string text)
    {
        _searchText = text;
        SuggestionsBox.Clear();

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            _searchText = string.Empty;
            ResetContent();
            return;
        }

        var suitableItems = _gameService.QueryModdableObjects(text, category: _category, minScore: 120)
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(x => new CharacterGridItemModel(x.Key))
            .ToList();


        if (!suitableItems.Any())
        {
            SuggestionsBox.Add(NoCharacterFound);
            ResetContent();
            return;
        }

        suitableItems.ForEach(suggestion => SuggestionsBox.Add(suggestion));
        ResetContent();
    }


    public async Task SuggestionBox_Chosen(CharacterGridItemModel? character)
    {
        if (character == NoCharacterFound || character is null)
            return;


        await CharacterClicked(character);
    }

    private void ResetContent()
    {
        if (_isNavigating) return;

        IEnumerable<CharacterGridItemModel> filteredCharacters = _backendCharacters;
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            filteredCharacters = filteredCharacters.Where(characterGridItemModel =>
                characterGridItemModel.Character.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                characterGridItemModel.Character.InternalName.Id.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
        }

        if (ShowUiCategories && SelectedUiCategory is { IsAll: false } selectedCategory)
        {
            filteredCharacters = filteredCharacters.Where(characterGridItemModel =>
                _characterTags.TryGetValue(characterGridItemModel.Character.InternalName.Id, out var tags) &&
                tags.Contains(selectedCategory.Id));
        }

        var sortedCharacters = SelectedSortingMethod.Sort(filteredCharacters, SortByDescending).ToList();

        var charactersToRemove = Characters.Except(sortedCharacters).ToArray();

        if (Characters.Count == 0)
        {
            foreach (var characterGridItemModel in sortedCharacters)
            {
                Characters.Add(characterGridItemModel);
            }

            return;
        }

        var missingCharacters = sortedCharacters.Except(Characters);

        foreach (var characterGridItemModel in missingCharacters)
        {
            Characters.Add(characterGridItemModel);
        }

        foreach (var characterGridItemModel in sortedCharacters)
        {
            var newIndex = sortedCharacters.IndexOf(characterGridItemModel);
            var oldIndex = Characters.IndexOf(characterGridItemModel);
            //Check if character is already at the right index

            if (newIndex == Characters.IndexOf(characterGridItemModel)) continue;

            if (oldIndex < 0 || oldIndex >= Characters.Count || newIndex < 0 || newIndex >= Characters.Count)
                throw new ArgumentOutOfRangeException();

            Characters.RemoveAt(oldIndex);
            Characters.Insert(newIndex, characterGridItemModel);
        }


        foreach (var characterGridItemModel in charactersToRemove)
        {
            Characters.Remove(characterGridItemModel);
        }


        Debug.Assert(Characters.Distinct().Count() == Characters.Count,
            $"Characters.Distinct().Count(): {Characters.Distinct().Count()} != Characters.Count: {Characters.Count}\n\t" +
            $"Duplicate characters found in character overview");
    }

    private ICategory _category = null!;

    public bool ShowUiCategories => _category?.ModCategory == ModCategory.Character;

    partial void OnSelectedUiCategoryChanged(UiCategoryFilterVm? value)
    {
        ResetContent();
    }

    public async void OnNavigatedTo(object parameter)
    {
        if (parameter is not ICategory category)
        {
            _logger.Error("Invalid parameter type {ParameterType}", parameter?.GetType().FullName);
            category = _gameService.GetCategories().First();
        }

        _busyService.BusyChanged += OnBusyChangedHandler;

        _category = category;
        OnPropertyChanged(nameof(ShowUiCategories));
        Messenger.UnregisterAll(this);
        Messenger.RegisterAll(this);
        CategoryPageTitle =
            $"{category.DisplayName} {_localizer.GetLocalizedStringOrDefault("Overview", useUidAsDefaultValue: true)}";
        SearchBoxPlaceHolder = $"搜索{category.DisplayNamePlural}...";


        var characters = _gameService.GetModdableObjects(_category);

        var firstType = characters.FirstOrDefault()?.GetType();
        if (characters.Any(ch => ch.GetType() != firstType))
            throw new InvalidOperationException("Characters must be of the same type");

        var others =
            characters.FirstOrDefault(ch =>
                ch.InternalName.Id.Contains("Others", StringComparison.OrdinalIgnoreCase));
        if (others is not null) // Add to front
        {
            characters.Remove(others);
            characters.Insert(0, others);
        }

        var gliders =
            characters.FirstOrDefault(ch => ch.InternalNameEquals(_gameService.GlidersCharacterInternalName));
        if (gliders is not null) // Add to end
        {
            characters.Remove(gliders);
            characters.Add(gliders);
        }

        var weapons =
            characters.FirstOrDefault(ch => ch.InternalNameEquals(_gameService.WeaponsCharacterInternalName));
        if (weapons is not null) // Add to end
        {
            characters.Remove(weapons);
            characters.Add(weapons);
        }


        _characters = characters;

        characters = new List<IModdableObject>(_characters);

        var pinnedCharactersOptions = await ReadCharacterSettings();

        var backendCharacters = new List<CharacterGridItemModel>();
        foreach (var pinedCharacterId in pinnedCharactersOptions.PinedCharacters)
        {
            var character = characters.FirstOrDefault(x => x.InternalNameEquals(pinedCharacterId));
            if (character is not null)
            {
                backendCharacters.Add(new CharacterGridItemModel(character) { IsPinned = true });
                characters.Remove(character);
            }
        }

        foreach (var hiddenCharacterId in pinnedCharactersOptions.HiddenCharacters)
        {
            var character = characters.FirstOrDefault(x => x.InternalNameEquals(hiddenCharacterId));
            if (character is not null)
            {
                backendCharacters.Add(new CharacterGridItemModel(character) { IsHidden = true });
                characters.Remove(character);
            }
        }

        // Add rest of characters
        foreach (var character in characters)
        {
            backendCharacters.Add(new CharacterGridItemModel(character));
        }

        _backendCharacters = backendCharacters;

        foreach (var characterGridItemModel in _backendCharacters)
        {
            var modList = _skinManagerService.GetCharacterModList(characterGridItemModel.Character);

            var characterModItems = new List<CharacterModItem>();
            foreach (var skinModEntry in modList.Mods)
            {
                var modSettings = await skinModEntry.Mod.Settings.TryReadSettingsAsync(true);

                characterModItems.Add(new CharacterModItem(skinModEntry.Mod.GetDisplayName(), skinModEntry.IsEnabled, modSettings?.DateAdded ?? default));
            }

            characterGridItemModel.SetMods(characterModItems);
        }


        InitializeSorters();

        if (typeof(ICharacter).IsAssignableFrom(firstType))
        {
            var backendCharactersList = _backendCharacters.Select(x => x.Character).Cast<ICharacter>().ToList();
            var distinctReleaseDates = backendCharactersList
                .Where(ch => ch.ReleaseDate != default)
                .DistinctBy(ch => ch.ReleaseDate)
                .Count();

            if (distinctReleaseDates == 1 &&
                SortingMethods.FirstOrDefault(x => x.SortingMethodType == GridItemSorter.ReleaseDateSortName) is
                { } releaseDateSortingMethod)
            {
                SortingMethods.Remove(releaseDateSortingMethod);
            }
        }

        // Add notifications
        await RefreshNotificationsAsync();

        // Character Ids where more than 1 skin is enabled
        await RefreshMultipleModsWarningAsync();

        var settings =
            await _localSettingsService
                .ReadOrCreateSettingAsync<CharacterOverviewSettings>(CharacterOverviewSettings.GetKey(_category));

        SortByDescending = settings.SortByDescending;

        var sorter = SortingMethods.FirstOrDefault(x => x.SortingMethodType == settings.SortingMethod);

        SelectedSortingMethod = sorter ?? SortingMethods.First();

        await InitializeUiCategoryFiltersAsync();


        _isNavigating = false;
        ResetContent();

        var lastPageType = _navigationService.GetNavigationHistory().SkipLast(1).LastOrDefault();
        if (lastPageType?.PageType == typeof(CharacterDetailsPage) ||
            lastPageType?.PageType == typeof(CharacterDetailsViewModel))
        {
            InternalName? internalName = null;

            if (lastPageType.Parameter is CharacterGridItemModel characterGridModel)
            {
                internalName = characterGridModel.Character.InternalName;
            }
            else if (lastPageType.Parameter is INameable modObject)
            {
                internalName = modObject.InternalName;
            }
            else if (lastPageType.Parameter is string modObjectString)

            {
                internalName = new InternalName(modObjectString);
            }

            if (internalName is null)
                return;

            var characterGridItemModel = FindCharacterByInternalName(internalName);
            if (characterGridItemModel is not null)
            {
                OnScrollToCharacter?.Invoke(this, new ScrollToCharacterArgs(characterGridItemModel));
            }
        }
    }

    private async Task RefreshMultipleModsWarningAsync()
    {
        var charactersWithMultipleMods = _skinManagerService.CharacterModLists
            .Where(x => x.Mods.Count(mod => mod.IsEnabled) > 1);

        var charactersWithMultipleActiveSkins = new HashSet<InternalName>();
        await Task.Run(async () =>
        {
            foreach (var modList in charactersWithMultipleMods)
            {
                if (_gameService.IsMultiMod(modList.Character))
                    continue;

                if (modList.Character is ICharacter { Skins.Count: > 1 } character)
                {
                    var addWarning = false;
                    var subSkinsFound = new List<ICharacterSkin>();
                    foreach (var characterSkinEntry in modList.Mods)
                    {
                        if (!characterSkinEntry.IsEnabled) continue;

                        var subSkin = _modCrawlerService.GetFirstSubSkinRecursive(characterSkinEntry.Mod.FullPath);
                        var modSettingsResult = await _modSettingsService.GetSettingsAsync(characterSkinEntry.Id);


                        var mod = ModModel.FromMod(characterSkinEntry);


                        if (modSettingsResult.IsT0)
                            mod.WithModSettings(modSettingsResult.AsT0);

                        if (!mod.CharacterSkinOverride.IsNullOrEmpty())
                            subSkin = _gameService.GetCharacterByIdentifier(character.InternalName)?.Skins
                                .FirstOrDefault(x => SkinVM.FromSkin(x).InternalNameEquals(mod.CharacterSkinOverride));

                        if (subSkin is null)
                            continue;


                        if (subSkinsFound.All(foundSubSkin =>
                                !subSkin.InternalNameEquals(foundSubSkin)))
                        {
                            subSkinsFound.Add(subSkin);
                            continue;
                        }


                        addWarning = true;
                        break;
                    }

                    if (addWarning || subSkinsFound.Count > 1 && character.Skins.Count == 1)
                        charactersWithMultipleActiveSkins.Add(modList.Character.InternalName);
                }
                else if (modList.Mods.Count(modEntry => modEntry.IsEnabled) >= 2)
                {
                    charactersWithMultipleActiveSkins.Add(modList.Character.InternalName);
                }
            }
        });


        foreach (var characterGridItemModel in _backendCharacters)
        {
            if (charactersWithMultipleActiveSkins.Contains(characterGridItemModel.Character.InternalName))
            {
                if (_gameService.IsMultiMod(characterGridItemModel.Character))
                    continue;

                characterGridItemModel.Warning = true;
            }
            else
            {
                characterGridItemModel.Warning = false;
            }
        }
    }

    private async Task RefreshNotificationsAsync()
    {
        foreach (var character in _characters)
        {
            var characterGridItemModel = FindCharacterByInternalName(character.InternalName);
            if (characterGridItemModel is null) continue;

            var characterMods = _skinManagerService.GetCharacterModList(character).Mods;

            var notifications = new List<ModNotification>();
            foreach (var characterSkinEntry in characterMods)
            {
                var modNotification = await _modNotificationManager.GetNotificationsForModAsync(characterSkinEntry.Id);
                notifications.AddRange(modNotification);
            }

            if (!notifications.Any())
            {
                characterGridItemModel.Notification = false;
                characterGridItemModel.NotificationType = AttentionType.None;
            }

            foreach (var modNotification in notifications)
            {
                if (modNotification.AttentionType == AttentionType.Added ||
                    modNotification.AttentionType == AttentionType.UpdateAvailable)
                {
                    characterGridItemModel.Notification = true;
                    characterGridItemModel.NotificationType = modNotification.AttentionType;
                }
            }
        }
    }

    public void OnNavigatedFrom()
    {
        _busyService.BusyChanged -= OnBusyChangedHandler;
        Messenger.UnregisterAll(this);
    }

    [RelayCommand]
    private void SelectUiCategory(UiCategoryFilterVm? category)
    {
        if (category is null)
            return;

        foreach (var c in UiCategories)
            c.IsSelected = false;

        category.IsSelected = true;
        SelectedUiCategory = category;
    }

    private async Task InitializeUiCategoryFiltersAsync(Guid? preferredCategoryId = null)
    {
        UiCategories.Clear();
        _characterTags.Clear();

        if (!ShowUiCategories)
            return;

        var categories = await _gameService.GetUiCategoriesAsync();
        var allHidden = categories.FirstOrDefault(c => c.Id == Guid.Empty)?.IsHidden ?? false;

        UiCategoryFilterVm? all = null;
        if (!allHidden)
        {
            all = UiCategoryFilterVm.CreateAll();
            UiCategories.Add(all);
        }

        foreach (var category in categories.Where(c => c.Id != Guid.Empty && !c.IsHidden))
            UiCategories.Add(UiCategoryFilterVm.FromCategory(category));

        var toSelect =
            preferredCategoryId is null
                ? all ?? UiCategories.FirstOrDefault()
                : UiCategories.FirstOrDefault(c => c.Id == preferredCategoryId) ??
                  (all ?? UiCategories.FirstOrDefault());

        if (toSelect is null)
            return;
        SelectUiCategory(toSelect);

        var tagTasks = _backendCharacters.Select(async characterGridItemModel =>
        {
            var tags = await _gameService.GetCharacterTagsAsync(characterGridItemModel.Character.InternalName);
            return (internalName: characterGridItemModel.Character.InternalName.Id, tags);
        });

        var tagResults = await Task.WhenAll(tagTasks);
        foreach (var (internalName, tags) in tagResults)
            _characterTags[internalName] = new HashSet<Guid>(tags);

        ResetContent();
    }

    public void Receive(UiCategoriesChangedMessage message)
    {
        if (!ShowUiCategories)
            return;

        var preferredId = SelectedUiCategory?.Id;
        _ = App.MainWindow.DispatcherQueue.EnqueueAsync(() => InitializeUiCategoryFiltersAsync(preferredId));
    }

    public void Receive(CharacterTagsChangedMessage message)
    {
        if (!ShowUiCategories)
            return;

        _characterTags[message.CharacterInternalName] = new HashSet<Guid>(message.TagIds);
        ResetContent();
    }

    [RelayCommand]
    private Task CharacterClicked(CharacterGridItemModel characterModel)
    {
        _navigationService.SetListDataItemForNextConnectedAnimation(characterModel);

        _navigationService.NavigateToCharacterDetails(characterModel.Character.InternalName);

        return Task.CompletedTask;
    }


    [ObservableProperty] private string _pinText = DefaultPinText;

    [ObservableProperty] private string _pinGlyph = DefaultPinGlyph;

    const string DefaultPinGlyph = "\uE718";
    const string DefaultPinText = "Pin To Top";
    const string DefaultUnpinGlyph = "\uE77A";
    const string DefaultUnpinText = "Unpin Character";

    public void OnRightClickContext(CharacterGridItemModel clickedCharacter)
    {
        ClearNotificationsCommand.NotifyCanExecuteChanged();
        DisableCharacterModsCommand.NotifyCanExecuteChanged();
        if (clickedCharacter.IsPinned)
        {
            PinText = DefaultUnpinText;
            PinGlyph = DefaultUnpinGlyph;
        }
        else
        {
            PinText = DefaultPinText;
            PinGlyph = DefaultPinGlyph;
        }
    }

    [RelayCommand]
    private async Task PinCharacterAsync(CharacterGridItemModel character)
    {
        if (character.IsPinned)
        {
            character.IsPinned = false;

            ResetContent();

            var settingss = await ReadCharacterSettings();

            var pinedCharacterss = _backendCharacters.Where(ch => ch.IsPinned)
                .Select(ch => ch.Character.InternalName.Id)
                .ToArray();
            settingss.PinedCharacters = pinedCharacterss;
            await SaveCharacterSettings(settingss);
            return;
        }


        character.IsPinned = true;

        ResetContent();

        var settings = await ReadCharacterSettings();

        var pinedCharacters = _backendCharacters
            .Where(ch => ch.IsPinned)
            .Select(ch => ch.Character.InternalName.Id)
            .ToArray();

        settings.PinedCharacters = pinedCharacters;

        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }


    private bool CanClearNotifications(CharacterGridItemModel? character)
    {
        return character?.Notification ?? false;
    }

    [RelayCommand(CanExecute = nameof(CanClearNotifications))]
    private async Task ClearNotificationsAsync(CharacterGridItemModel character)
    {
        await _modNotificationManager.ClearModNotificationsAsync(character.Character.InternalName);
        await RefreshNotificationsAsync().ConfigureAwait(false);
    }

    private bool CanDisableCharacterMods(CharacterGridItemModel? character) =>
        character is not null && character.Mods.Any(m => m.IsEnabled);

    [RelayCommand(CanExecute = nameof(CanDisableCharacterMods))]
    private async Task DisableCharacterMods(CharacterGridItemModel character)
    {
        var updatedMods = new List<CharacterModItem>();
        try
        {
            await Task.Run(async () =>
            {
                var modList = _skinManagerService.GetCharacterModList(character.Character);
                var mods = modList.Mods.ToArray();
                foreach (var modEntry in mods)
                {
                    if (modList.IsModEnabled(modEntry.Mod))
                    {
                        modList.DisableMod(modEntry.Id);
                    }

                    var modSettings = await modEntry.Mod.Settings.TryReadSettingsAsync(true).ConfigureAwait(false);

                    updatedMods.Add(new CharacterModItem(modEntry.Mod.GetDisplayName(), modEntry.IsEnabled, modSettings?.DateAdded ?? default));
                }
            });
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error disabling mods for character {Character}", character.Character.InternalName);
            NotificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.Character.DisableModsFailed.Title",
                    defaultValue: "禁用模组失败"),
                e.Message,
                TimeSpan.FromSeconds(6));
            return;
        }

        character.SetMods(updatedMods);
        await RefreshMultipleModsWarningAsync();
        NotificationManager.ShowNotification(
            _localizer.GetLocalizedStringOrDefault("Notification.Character.ModsDisabled.Title", defaultValue: "已禁用模组"),
            string.Format(CultureInfo.CurrentUICulture,
                _localizer.GetLocalizedStringOrDefault("Notification.Character.ModsDisabled.Message",
                    defaultValue: "已禁用 {0} 的全部模组。")!,
                character.Character.DisplayName),
            null);
    }

    [RelayCommand]
    private async Task OpenCharacterFolderAsync(CharacterGridItemModel? character)
    {
        if (character is null)
            return;
        var modList = _skinManagerService.GetCharacterModList(character.Character);

        var directoryToOpen = new DirectoryInfo(modList.AbsModsFolderPath);
        if (!directoryToOpen.Exists)
        {
            modList.InstantiateCharacterFolder();
            directoryToOpen.Refresh();

            if (!directoryToOpen.Exists)
            {
                var parentDir = directoryToOpen.Parent;

                if (parentDir is null)
                {
                    _logger.Error("Could not find parent directory of {Directory}", directoryToOpen.FullName);
                    return;
                }

                directoryToOpen = parentDir;
            }
        }

        await Launcher.LaunchFolderAsync(
            await StorageFolder.GetFolderFromPathAsync(directoryToOpen.FullName));
    }

    [RelayCommand]
    private void HideCharacter(CharacterGridItemModel character)
    {
        NotImplemented.Show(_localizer.GetLocalizedStringOrDefault("Notification.NotImplemented.HideCharacter.Message",
            defaultValue: "暂不支持隐藏角色"));
    }

    private Task<CharacterOverviewSettings> ReadCharacterSettings() =>
        _localSettingsService
            .ReadOrCreateSettingAsync<CharacterOverviewSettings>(CharacterOverviewSettings.GetKey(_category));

    private Task SaveCharacterSettings(CharacterOverviewSettings settings) =>
        _localSettingsService.SaveSettingAsync(CharacterOverviewSettings.GetKey(_category), settings);


    private bool CanRefreshModsInGame()
    {
        return ElevatorService.ElevatorStatus == ElevatorStatus.Running;
    }

    [RelayCommand(CanExecute = nameof(CanRefreshModsInGame))]
    private async Task RefreshModsInGameAsync()
    {
        _logger.Debug("Refreshing Mods In Game");
        await ElevatorService.RefreshGenshinMods();
    }

    [ObservableProperty] private bool _isAddingMod = false;

    public async Task ModDroppedOnCharacterAsync(CharacterGridItemModel characterGridItemModel,
        IReadOnlyList<IStorageItem> storageItems)
    {
        if (IsAddingMod)
        {
            _logger.Warning("Already adding mod");
            return;
        }

        var modList =
            _skinManagerService.CharacterModLists.FirstOrDefault(x =>
                x.Character.InternalNameEquals(characterGridItemModel.Character));
        if (modList is null)
        {
            _logger.Warning("No mod list found for character {Character}",
                characterGridItemModel.Character.InternalName);
            return;
        }

        try
        {
            IsAddingMod = true;
            await _modDragAndDropService.AddStorageItemFoldersAsync(modList, storageItems);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error adding mod");
            NotificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.ModDragDrop.AddFailed.Title", defaultValue: "添加模组失败"),
                e.Message,
                TimeSpan.FromSeconds(10));
        }
        finally
        {
            IsAddingMod = false;
        }
    }

    public async Task ModUrlDroppedOnCharacterAsync(CharacterGridItemModel characterGridItemModel, Uri uri)
    {
        if (IsAddingMod)
        {
            _logger.Warning("Already adding mod");
            return;
        }

        var modList = _skinManagerService.CharacterModLists.FirstOrDefault(x => x.Character.InternalNameEquals(characterGridItemModel.Character));
        if (modList is null)
        {
            _logger.Warning("No mod list found for character {Character}",
                characterGridItemModel.Character.InternalName);
            return;
        }

        if (!GameBananaUrlHelper.TryGetModIdFromUrl(uri, out _))
        {
            NotificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.GameBanana.InvalidUrl.Title",
                    defaultValue: "无效的 GameBanana 模组链接"),
                string.Empty,
                null);
            return;
        }

        try
        {
            IsAddingMod = true;
            await _modDragAndDropService.AddModFromUrlAsync(modList, uri);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error opening mod page window");
            NotificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.GameBanana.OpenModPageFailed.Title",
                    defaultValue: "打开模组页面失败"),
                e.Message,
                TimeSpan.FromSeconds(10));
        }
        finally
        {
            IsAddingMod = false;
        }
    }


    private CharacterGridItemModel? FindCharacterByInternalName(string internalName)
    {
        return _backendCharacters.FirstOrDefault(x =>
            x.Character.InternalNameEquals(internalName));
    }


    [RelayCommand]
    private async Task SortBy(IEnumerable<GridItemSortingMethod> methodTypes)
    {
        if (_isNavigating) return;
        var sortingMethodType = methodTypes.First();

        ResetContent();

        var settings = await ReadCharacterSettings();
        settings.SortingMethod = sortingMethodType.SortingMethodType;
        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }


    [RelayCommand]
    private async Task InvertSorting()
    {
        ResetContent();

        var settings = await ReadCharacterSettings();
        settings.SortByDescending = SortByDescending;
        await SaveCharacterSettings(settings).ConfigureAwait(false);
    }

    [RelayCommand]
    private void CheckForUpdatesForCharacter(object? characterGridItemModel)
    {
        if (characterGridItemModel is not CharacterGridItemModel character)
            return;

        var modList = _skinManagerService.GetCharacterModList(character.Character);
        if (modList is null)
        {
            _logger.Warning("No mod list found for character {Character}", character.Character.InternalName);
            return;
        }

        var check = ModCheckRequest.ForCharacter(character.Character);

        _modUpdateAvailableChecker.CheckNow(check.WithIgnoreLastChecked());
    }


    private void InitializeSorters()
    {
        var lastCharacters = new List<CharacterGridItemModel>
        {
            FindCharacterByInternalName(_gameService.GlidersCharacterInternalName)!,
            FindCharacterByInternalName(_gameService.WeaponsCharacterInternalName)!
        };

        var othersCharacter = _backendCharacters.FirstOrDefault(ch =>
            ch.Character.InternalName.Id.Contains("Others", StringComparison.OrdinalIgnoreCase));

        var alphabetical = new GridItemSortingMethod(GridItemSorter.Alphabetical, othersCharacter, lastCharacters);
        SortingMethods.Add(alphabetical);

        var byModCount = new GridItemSortingMethod(GridItemSorter.ModCount, othersCharacter, lastCharacters);
        SortingMethods.Add(byModCount);
    }


    private void OnBusyChangedHandler(object? sender, BusyChangedEventArgs args)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (args.IsKey(this))
                IsBusy = args.IsBusy;
        });
    }

    public sealed class GridItemSortingMethod(
        Sorter<CharacterGridItemModel> sortingMethodType,
        CharacterGridItemModel? firstItem = null,
        ICollection<CharacterGridItemModel>? lastItems = null)
        : SortingMethod<CharacterGridItemModel>(sortingMethodType, firstItem, lastItems)
    {
        public override string ToString()
        {
            return SortingMethodType switch
            {
                GridItemSorter.AlphabeticalSortName => "按名称",
                GridItemSorter.ModCountSortName => "按模组数量",
                _ => SortingMethodType
            };
        }

        protected override void PostSortAction(List<CharacterGridItemModel> sortedList)
        {
            var pinnedCharacters = sortedList.Where(x => x.IsPinned).ToArray();
            if (pinnedCharacters.Length == 0)
                return;
            var pinnedStartIndex = FirstItem is not null ? 1 : 0;
            foreach (var pinnedCharacter in pinnedCharacters)
            {
                if (pinnedCharacter == FirstItem) continue;
                sortedList.Remove(pinnedCharacter);
                sortedList.Insert(pinnedStartIndex, pinnedCharacter);
                pinnedStartIndex++;
            }
        }
    }

    public sealed class GridItemSorter : Sorter<CharacterGridItemModel>
    {
        private GridItemSorter(string sortingMethodType, SortFunc firstSortFunc, AdditionalSortFunc? secondSortFunc = null,
            AdditionalSortFunc? thirdSortFunc = null) : base(sortingMethodType, firstSortFunc, secondSortFunc, thirdSortFunc)
        {
        }

        public const string AlphabeticalSortName = "Alphabetical";

        public static GridItemSorter Alphabetical { get; } =
            new(
                AlphabeticalSortName,
                (characters, isDescending) =>
                    isDescending
                        ? characters.OrderByDescending(x => x.Character.DisplayName)
                        : characters.OrderBy(x => x.Character.DisplayName
                        ));


        public const string ReleaseDateSortName = "Release Date";

        public static GridItemSorter ReleaseDate { get; } =
            new(
                ReleaseDateSortName,
                (characters, isDescending) =>
                    !isDescending
                        ? characters.OrderByDescending(x => ((ICharacter)x.Character).ReleaseDate)
                        : characters.OrderBy(x => ((ICharacter)x.Character).ReleaseDate),
                (characters, _) =>
                    characters.ThenBy(x => x.Character.DisplayName
                    ));


        public const string RaritySortName = "Rarity";

        public static GridItemSorter Rarity { get; } =
            new(
                RaritySortName,
                (characters, isDescending) =>
                    !isDescending
                        ? characters.OrderByDescending(x => ((IRarity)x.Character).Rarity)
                        : characters.OrderBy(x => ((IRarity)x.Character).Rarity),
                (characters, _) =>
                    characters.ThenBy(x => x.Character.DisplayName
                    ));


        public const string ModCountSortName = "Mod Count";

        public static GridItemSorter ModCount { get; } =
            new(
                ModCountSortName,
                (characters, isDescending) =>
                    !isDescending
                        ? characters.OrderByDescending(x => x.ModCount)
                        : characters.OrderBy(x => x.ModCount),
                (characters, _) =>
                    characters.ThenBy(x => x.Character.DisplayName
                    ));


        public const string ModRecentlyAddedName = "Recently Added Mods";

        public static GridItemSorter ModRecentlyAdded { get; } =
            new(
                ModRecentlyAddedName,
                (characters, isDescending) =>
                    !isDescending
                        ? characters.OrderByDescending(x =>
                        {
                            var validDates = x.Mods.Where(mod => mod.DateAdded != default).Select(mod => mod.DateAdded)
                                .ToArray();
                            if (validDates.Any())
                                return validDates.Max();
                            else
                                return DateTime.MinValue;
                        })
                        : characters.OrderBy(x =>
                        {
                            var validDates = x.Mods.Where(mod => mod.DateAdded != default).Select(mod => mod.DateAdded)
                                .ToArray();
                            if (validDates.Any())
                                return validDates.Min();
                            else
                                return DateTime.MaxValue;
                        }),
                (characters, _) =>
                    characters.ThenBy(x => x.Character.DisplayName
                    ));
    }

}

public sealed partial class UiCategoryFilterVm : ObservableObject
{
    public Guid Id { get; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private Uri? _imageUri;
    [ObservableProperty] private bool _isSelected;
    public bool IsAll => Id == Guid.Empty;
    public bool PreferImage => !IsAll && ImageUri is not null;
    public bool PreferText => IsAll || ImageUri is null;

    private UiCategoryFilterVm(Guid id)
    {
        Id = id;
    }

    partial void OnImageUriChanged(Uri? value)
    {
        OnPropertyChanged(nameof(PreferImage));
        OnPropertyChanged(nameof(PreferText));
    }

    public static UiCategoryFilterVm CreateAll()
    {
        return new UiCategoryFilterVm(Guid.Empty)
        {
            Name = "全部"
        };
    }

    public static UiCategoryFilterVm FromCategory(UiCategory category)
    {
        return new UiCategoryFilterVm(category.Id)
        {
            Name = category.Name,
            ImageUri = category.ImageUri
        };
    }
}

public class ScrollToCharacterArgs : EventArgs
{
    public CharacterGridItemModel Character { get; }

    public ScrollToCharacterArgs(CharacterGridItemModel character)
    {
        Character = character;
    }
}
