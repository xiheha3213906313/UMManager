﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Windows.Storage.Pickers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErrorOr;
using UMManager.Core.Contracts.Entities;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.Helpers;
using UMManager.Core.Services;
using UMManager.Core.Services.GameBanana;
using UMManager.WinUI.Contracts.Services;
using UMManager.WinUI.Contracts.ViewModels;
using UMManager.WinUI.Helpers;
using UMManager.WinUI.Models.Options;
using UMManager.WinUI.Models.Settings;
using UMManager.WinUI.Services;
using UMManager.WinUI.Services.AppManagement;
using UMManager.WinUI.Services.AppManagement.Updating;
using UMManager.WinUI.Services.ModHandling;
using UMManager.WinUI.Services.Notifications;
using UMManager.WinUI.Validators.PreConfigured;
using UMManager.WinUI.ViewModels.SettingsViewModels;
using UMManager.WinUI.ViewModels.SubVms;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Serilog;

namespace UMManager.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableRecipient, INavigationAware
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILogger _logger;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly INavigationViewService _navigationViewService;
    private readonly IWindowManagerService _windowManagerService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly IGameService _gameService;
    private readonly ILanguageLocalizer _localizer;
    private readonly AutoUpdaterService _autoUpdaterService;
    private readonly SelectedGameService _selectedGameService;
    private readonly ModUpdateAvailableChecker _modUpdateAvailableChecker;
    private readonly LifeCycleService _lifeCycleService;
    private readonly INavigationService _navigationService;
    private readonly ModArchiveRepository _modArchiveRepository;


    private readonly NotificationManager _notificationManager;
    private readonly UpdateChecker _updateChecker;
    public ElevatorService ElevatorService;


    [ObservableProperty] private ElementTheme _elementTheme;

    [ObservableProperty] private string _versionDescription;

    [ObservableProperty] private string _latestVersion = string.Empty;
    [ObservableProperty] private bool _showNewVersionAvailable = false;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IgnoreNewVersionCommand))]
    private bool _CanIgnoreUpdate = false;

    [ObservableProperty] private ObservableCollection<string> _languages = new();
    [ObservableProperty] private string _selectedLanguage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _games = new()
    {
        SupportedGames.Genshin.ToString(),
        SupportedGames.Honkai.ToString(),
        SupportedGames.WuWa.ToString(),
        SupportedGames.ZZZ.ToString()
    };

    [ObservableProperty] private string _selectedGame = string.Empty;

    [ObservableProperty] private string _modCheckerStatus = ModUpdateAvailableChecker.RunningState.Waiting.ToString();

    [ObservableProperty] private bool _isModUpdateCheckerEnabled = false;

    [ObservableProperty] private DateTime? _nextModCheckTime = null;

    [ObservableProperty] private bool _characterAsSkinsCheckbox = false;

    [ObservableProperty] private int _maxCacheLimit;

    [ObservableProperty] private Uri _archiveCacheFolderPath;

    [ObservableProperty] private bool _persistWindowSize = false;

    [ObservableProperty] private bool _persistWindowPosition = false;

    private Dictionary<string, string> _nameToLangCode = new();

    public PathPicker PathToModsFolderPicker { get; }
    public Uri ConfigFolderPath { get; } = new Uri(ConfigStorageHelper.GetConfigRoot());
    public string ConfigFolderPathText => ConfigStorageHelper.GetConfigRoot();

    [ObservableProperty] private bool _legacyCharacterDetails;


    private static bool _showElevatorStartDialog = true;

    private ModManagerOptions? _modManagerOptions = null!;

    [ObservableProperty] private string _modCacheSizeGB = string.Empty;

    public SettingsViewModel(
        IThemeSelectorService themeSelectorService, ILocalSettingsService localSettingsService,
        ElevatorService elevatorService, ILogger logger, NotificationManager notificationManager,
        INavigationViewService navigationViewService, IWindowManagerService windowManagerService,
        ISkinManagerService skinManagerService, UpdateChecker updateChecker,
        IGameService gameService, AutoUpdaterService autoUpdaterService, ILanguageLocalizer localizer,
        SelectedGameService selectedGameService, ModUpdateAvailableChecker modUpdateAvailableChecker,
        LifeCycleService lifeCycleService, INavigationService navigationService,
        ModArchiveRepository modArchiveRepository)
    {
        _themeSelectorService = themeSelectorService;
        _localSettingsService = localSettingsService;
        ElevatorService = elevatorService;
        _notificationManager = notificationManager;
        _navigationViewService = navigationViewService;
        _windowManagerService = windowManagerService;
        _skinManagerService = skinManagerService;
        _updateChecker = updateChecker;
        _gameService = gameService;
        _autoUpdaterService = autoUpdaterService;
        _localizer = localizer;
        _selectedGameService = selectedGameService;
        _modUpdateAvailableChecker = modUpdateAvailableChecker;
        _lifeCycleService = lifeCycleService;
        _navigationService = navigationService;
        _modArchiveRepository = modArchiveRepository;
        _logger = logger.ForContext<SettingsViewModel>();
        _elementTheme = _themeSelectorService.Theme;
        _versionDescription = GetVersionDescription();

        _updateChecker.NewVersionAvailable += UpdateCheckerOnNewVersionAvailable;

        if (_updateChecker.LatestRetrievedVersion is not null &&
            _updateChecker.LatestRetrievedVersion != _updateChecker.CurrentVersion)
        {
            LatestVersion = VersionFormatter(_updateChecker.LatestRetrievedVersion);
            ShowNewVersionAvailable = true;
            if (_updateChecker.LatestRetrievedVersion != _updateChecker.IgnoredVersion)
                CanIgnoreUpdate = true;
        }

        ArchiveCacheFolderPath = _modArchiveRepository.ArchiveDirectory;

        _modManagerOptions = localSettingsService.ReadSetting<ModManagerOptions>(ModManagerOptions.Section);
        PathToModsFolderPicker = new PathPicker(ModsFolderValidator.Validators);

        CharacterAsSkinsCheckbox = _modManagerOptions?.CharacterSkinsAsCharacters ?? false;

        PathToModsFolderPicker.Path = _modManagerOptions?.ModsFolderPath;


        PathToModsFolderPicker.IsValidChanged +=
            (sender, args) => SaveSettingsCommand.NotifyCanExecuteChanged();


        PathToModsFolderPicker.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(PathPicker.Path))
                SaveSettingsCommand.NotifyCanExecuteChanged();
        };
        ElevatorService.CheckStatus();

        MaxCacheLimit = localSettingsService.ReadSetting<ModArchiveSettings>(ModArchiveSettings.Key)
            ?.MaxLocalArchiveCacheSizeGb ?? new ModArchiveSettings().MaxLocalArchiveCacheSizeGb;
        SetCacheString(MaxCacheLimit);

        var cultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
        cultures = cultures.Append(new CultureInfo("zh-cn")).ToArray();


        var supportedCultures = _localizer.AvailableLanguages.Select(l => l.LanguageCode).ToArray();

        foreach (var culture in cultures)
        {
            if (!supportedCultures.Contains(culture.Name.ToLower())) continue;

            Languages.Add(culture.NativeName);
            _nameToLangCode.Add(culture.NativeName, culture.Name.ToLower());

            if (_localizer.CurrentLanguage.Equals(culture))
                SelectedLanguage = culture.NativeName;
        }

        ModCheckerStatus = _localizer.GetLocalizedStringOrDefault(_modUpdateAvailableChecker.Status.ToString(),
            _modUpdateAvailableChecker.Status.ToString());
        NextModCheckTime = _modUpdateAvailableChecker.NextRunAt;
        _modUpdateAvailableChecker.OnUpdateCheckerEvent += (sender, args) =>
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                ModCheckerStatus = _localizer.GetLocalizedStringOrDefault(_modUpdateAvailableChecker.Status.ToString(),
                    _modUpdateAvailableChecker.Status.ToString());
                NextModCheckTime = args.NextRunAt;
            });
        };
    }


    [RelayCommand]
    private async Task SwitchThemeAsync(ElementTheme param)
    {
        if (ElementTheme != param)
        {
            var result = await _windowManagerService.ShowDialogAsync(new ContentDialog()
            {
                Title = _localizer.GetLocalizedStringOrDefault("Dialog.ThemeRestartRequired.Title", defaultValue: "需要重启"),
                Content = new TextBlock()
                {
                    Text = _localizer.GetLocalizedStringOrDefault("Dialog.ThemeRestartRequired.Text",
                        defaultValue:
                        "需要重启应用以使主题生效，否则应用可能不稳定（可能是主题配置不完善导致）。建议使用深色模式。\n\n" +
                        "给你带来不便，敬请谅解。"),
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Restart", defaultValue: "重启"),
                CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
                DefaultButton = ContentDialogButton.Primary
            });

            if (result != ContentDialogResult.Primary) return;

            ElementTheme = param;
            await _themeSelectorService.SetThemeAsync(param);
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.Restarting.Title", defaultValue: "正在重启…"),
                _localizer.GetLocalizedStringOrDefault("Notification.Restarting.Message", defaultValue: "应用即将重启。"),
                null);
            await RestartAppAsync();
        }
    }

    [RelayCommand]
    private async Task WindowSizePositionToggle(string? type)
    {
        if (type != "size" && type != "position") return;

        var windowSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ScreenSizeSettings>(ScreenSizeSettings.Key);

        if (type == "size")
        {
            PersistWindowSize = !PersistWindowSize;
            windowSettings.PersistWindowSize = PersistWindowSize;
        }
        else
        {
            PersistWindowPosition = !PersistWindowPosition;
            windowSettings.PersistWindowPosition = PersistWindowPosition;
        }

        await _localSettingsService.SaveSettingAsync(ScreenSizeSettings.Key, windowSettings).ConfigureAwait(false);
    }

    private static string GetVersionDescription()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version!;

        return
            $"{"AppDisplayName".GetLocalized()} - {VersionFormatter(version)}";
    }


    private bool ValidFolderSettings()
    {
        return PathToModsFolderPicker.IsValid && PathToModsFolderPicker.Path != _modManagerOptions?.ModsFolderPath;
    }


    [RelayCommand(CanExecute = nameof(ValidFolderSettings))]
    private async Task SaveSettings()
    {
        var dialog = new ContentDialog();
        dialog.XamlRoot = App.MainWindow.Content.XamlRoot;
        dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;
        dialog.Title = _localizer.GetLocalizedStringOrDefault("Dialog.UpdateModsFolder.Title", defaultValue: "更新文件夹路径？");
        dialog.CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消");
        dialog.PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Save", defaultValue: "保存");
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.Content = _localizer.GetLocalizedStringOrDefault("Dialog.UpdateModsFolder.Text",
            defaultValue: "确定要保存新的 Mods 文件夹路径吗？保存后应用将自动重启。");

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            var modsFolderPath = PathToModsFolderPicker.Path?.Trim();
            if (!modsFolderPath.IsNullOrEmpty() && Directory.Exists(modsFolderPath!) &&
                !FileSystemAccessHelper.CanReadWriteDirectory(modsFolderPath!))
            {
                await _windowManagerService.ShowDialogAsync(new ContentDialog
                {
                    Title = _localizer.GetLocalizedStringOrDefault("Dialog.ModsFolderAccessDenied.Title",
                        defaultValue: "无法访问所选文件夹"),
                    Content = _localizer.GetLocalizedStringOrDefault("Dialog.ModsFolderAccessDenied.Text",
                        defaultValue: "所选 Mods 文件夹需要管理员权限才能访问或写入。\n\n请修改文件夹权限，或以管理员身份启动本软件后再选择该文件夹。"),
                    CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.GotIt", defaultValue: "知道了")
                });
                return;
            }

            var gimiRootFolderPath = modsFolderPath.IsNullOrEmpty() ? null : Directory.GetParent(modsFolderPath!)?.FullName;

            var modManagerOptions = await _localSettingsService.ReadSettingAsync<ModManagerOptions>(
                ModManagerOptions.Section) ?? new ModManagerOptions();

            modManagerOptions.GimiRootFolderPath = gimiRootFolderPath;
            modManagerOptions.ModsFolderPath = modsFolderPath;

            await _localSettingsService.SaveSettingAsync(ModManagerOptions.Section,
                modManagerOptions);
            _logger.Information("Saved startup settings: {@ModManagerOptions}", modManagerOptions);
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.Restarting.Title", defaultValue: "正在重启…"),
                _localizer.GetLocalizedStringOrDefault("Notification.Restarting.Message", defaultValue: "应用即将重启。"),
                TimeSpan.FromSeconds(2));


            await RestartAppAsync();
        }
    }


    [RelayCommand]
    private async Task BrowseModsFolderAsync()
    {
        await PathToModsFolderPicker.BrowseFolderPathAsync(App.MainWindow);
    }

    [RelayCommand]
    private async Task ReorganizeModsAsync()
    {
        var result = await _windowManagerService.ShowDialogAsync(new ContentDialog()
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.ReorganizeMods.Title", defaultValue: "整理 Mods？"),
            Content = new TextBlock()
            {
                Text = _localizer.GetLocalizedStringOrDefault("Dialog.ReorganizeMods.Text",
                    defaultValue:
                    "是否要整理 Mods 文件夹？应用会把直接位于 Mods 与 Others 文件夹下的模组，按角色归类移动到对应文件夹。无法匹配的会放入 “Others”。已在 “Others” 中的模组将保持不变。"),
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true
            },
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Yes", defaultValue: "是"),
            DefaultButton = ContentDialogButton.Primary,
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
        });

        if (result == ContentDialogResult.Primary)
        {
            _navigationViewService.IsEnabled = false;

            try
            {
                var movedModsCount = await Task.Run(() =>
                    _skinManagerService.ReorganizeModsAsync()); // Mods folder

                movedModsCount += await Task.Run(() =>
                    _skinManagerService.ReorganizeModsAsync(
                        _gameService.GetCharacterByIdentifier(_gameService.OtherCharacterInternalName)!
                            .InternalName)); // Others folder

                await _skinManagerService.RefreshModsAsync();

                if (movedModsCount == -1)
                    _notificationManager.ShowNotification(
                        _localizer.GetLocalizedStringOrDefault("Notification.ModsReorganizationFailed.Title",
                            defaultValue: "整理 Mods 失败。"),
                        _localizer.GetLocalizedStringOrDefault("Notification.SeeLogs.Message", defaultValue: "详情请查看日志。"),
                        TimeSpan.FromSeconds(5));

                else
                    _notificationManager.ShowNotification(
                        _localizer.GetLocalizedStringOrDefault("Notification.ModsReorganized.Title", defaultValue: "Mods 已整理。"),
                        string.Format(_localizer.GetLocalizedStringOrDefault("Notification.MovedModsToCharacterFolders.Message",
                                defaultValue: "已移动 {0} 个模组到角色文件夹。")!,
                            movedModsCount),
                        TimeSpan.FromSeconds(5));
            }
            finally
            {
                _navigationViewService.IsEnabled = true;
            }
        }
    }


    private async Task RestartAppAsync(int delay = 2)
    {
        _navigationViewService.IsEnabled = false;

        await Task.Delay(TimeSpan.FromSeconds(delay));

        await _lifeCycleService.RestartAsync(notifyOnError: true);
    }

    private bool CanStartElevator()
    {
        return ElevatorService.ElevatorStatus == ElevatorStatus.NotRunning;
    }

    [RelayCommand(CanExecute = nameof(CanStartElevator))]
    private async Task StartElevator()
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords,
            Text = _localizer.GetLocalizedString("Dialog.StartElevator.Text"),
            Margin = new Thickness(0, 0, 0, 12),
            IsTextSelectionEnabled = true
        };


        var doNotShowAgainCheckBox = new CheckBox
        {
            Content = _localizer.GetLocalizedStringOrDefault("Common.CheckBox.DoNotShowAgain",
                defaultValue: "不再提示"),
            IsChecked = false
        };

        var stackPanel = new StackPanel
        {
            Children =
            {
                text,
                doNotShowAgainCheckBox
            }
        };


        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.StartElevator.Title", defaultValue: "启动 Elevator 进程？"),
            Content = stackPanel,
            DefaultButton = ContentDialogButton.Primary,
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Start", defaultValue: "启动"),
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var start = true;

        if (_showElevatorStartDialog)
        {
            var result = await dialog.ShowAsync();
            start = result == ContentDialogResult.Primary;
            if (start)
                _showElevatorStartDialog = !doNotShowAgainCheckBox.IsChecked == true;
        }

        if (start && ElevatorService.ElevatorStatus == ElevatorStatus.NotRunning)
            try
            {
                ElevatorService.StartElevator();
            }
            catch (Win32Exception e)
            {
                _notificationManager.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("Notification.ElevatorStartFailed.Title",
                        defaultValue: "无法启动 Elevator"),
                    e.Message,
                    TimeSpan.FromSeconds(10));
                _showElevatorStartDialog = true;
            }
    }

    private void UpdateCheckerOnNewVersionAvailable(object? sender, UpdateChecker.NewVersionEventArgs e)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (e.Version == new Version())
            {
                CanIgnoreUpdate = _updateChecker.LatestRetrievedVersion != _updateChecker.IgnoredVersion;
                return;
            }

            LatestVersion = VersionFormatter(e.Version);
        });
    }

    private static string VersionFormatter(Version version)
    {
        return $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    [RelayCommand(CanExecute = nameof(CanIgnoreUpdate))]
    private async Task IgnoreNewVersion()
    {
        await _updateChecker.IgnoreCurrentVersionAsync();
    }

    [ObservableProperty] private bool _exportingMods = false;
    [ObservableProperty] private int _exportProgress = 0;
    [ObservableProperty] private string _exportProgressText = string.Empty;
    [ObservableProperty] private string? _currentModName;

    [RelayCommand]
    private async Task ExportMods(ContentDialog contentDialog)
    {
        var dialog = new ContentDialog()
        {
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Export", defaultValue: "导出"),
            IsPrimaryButtonEnabled = true,
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.Title = _localizer.GetLocalizedStringOrDefault("Dialog.ExportMods.Title", defaultValue: "导出 Mods");

        dialog.ContentTemplate = contentDialog.ContentTemplate;

        var model = new ExportModsDialogModel(_gameService.GetAllModdableObjects());
        dialog.DataContext = model;
        var result = await _windowManagerService.ShowDialogAsync(dialog);

        if (result != ContentDialogResult.Primary)
            return;

        var folderPicker = new FolderPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
        folderPicker.FileTypeFilter.Add("*");
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder == null)
            return;

        ExportingMods = true;
        _navigationViewService.IsEnabled = false;

        var charactersToExport =
            model.CharacterModsToBackup.Where(modList => modList.IsChecked).Select(ch => ch.Character);
        var modsList = new List<ICharacterModList>();
        foreach (var character in charactersToExport)
            modsList.Add(_skinManagerService.GetCharacterModList(character.InternalName));

        try
        {
            _skinManagerService.ModExportProgress += HandleProgressEvent;
            await Task.Run(() =>
            {
                _skinManagerService.ExportMods(modsList, folder.Path,
                    removeLocalJasmSettings: model.RemoveJasmSettings, zip: false,
                    keepCharacterFolderStructure: model.KeepFolderStructure, setModStatus: model.SetModStatus);
            });
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.ModsExported.Title", defaultValue: "Mods 已导出"),
                string.Format(_localizer.GetLocalizedStringOrDefault("Notification.ModsExported.Message",
                        defaultValue: "Mods 已导出到 {0}")!,
                    folder.Path),
                TimeSpan.FromSeconds(5));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error exporting mods");
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.ErrorExportingMods.Title", defaultValue: "导出 Mods 出错"),
                e.Message,
                TimeSpan.FromSeconds(10));
        }
        finally
        {
            _skinManagerService.ModExportProgress -= HandleProgressEvent;
            ExportingMods = false;
            _navigationViewService.IsEnabled = true;
        }
    }

    private void HandleProgressEvent(object? sender, ExportProgress args)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            ExportProgress = args.Progress;
            ExportProgressText = args.Operation;
            CurrentModName = args.ModName;
        });
    }


    [RelayCommand]
    private async Task SelectLanguage(string selectedLanguageName)
    {
        if (_nameToLangCode.TryGetValue(selectedLanguageName, out var langCode))
        {
            if (langCode == _localizer.CurrentLanguage.LanguageCode)
                return;

            var restartDialog = new ContentDialog()
            {
                Title = _localizer.GetLocalizedStringOrDefault("Dialog.ChangeLanguage.Title", defaultValue: "需要重启"),
                Content = new TextBlock()
                {
                    Text = _localizer.GetLocalizedStringOrDefault("Dialog.ChangeLanguage.Text",
                        defaultValue:
                        "更改语言需要重启应用。\n" +
                        "这是为了确保应用能按所选语言正确加载配置。\n\n" +
                        "是否要更改语言？"),
                    TextWrapping = TextWrapping.WrapWholeWords,
                    IsTextSelectionEnabled = true
                },
                PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Dialog.ChangeLanguage.Primary",
                    defaultValue: "更改语言并重启"),
                CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await _windowManagerService.ShowDialogAsync(restartDialog);

            var currentLanguage = _localizer.CurrentLanguage.LanguageName;
            if (result != ContentDialogResult.Primary)
            {
                SelectedLanguage = currentLanguage;
                return;
            }

            await _localizer.SetLanguageAsync(langCode);

            var appSettings = await _localSettingsService.ReadOrCreateSettingAsync<AppSettings>(AppSettings.Key);
            appSettings.Language = langCode;
            await _localSettingsService.SaveSettingAsync(AppSettings.Key, appSettings);
            currentLanguage = _localizer.CurrentLanguage.LanguageName;
            SelectedLanguage = currentLanguage;

            await RestartAppAsync();
        }
    }

    [RelayCommand]
    private void UpdateJasm()
    {
        var errors = Array.Empty<Error>();
        try
        {
            errors = _autoUpdaterService.StartSelfUpdateProcess();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error starting update process");
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.ErrorStartingUpdateProcess.Title", defaultValue: "启动更新流程出错"),
                e.Message,
                TimeSpan.FromSeconds(10));
        }

        if (errors is not null && errors.Any())
        {
            var errorMessages = errors.Select(e => e.Description).ToArray();
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.CouldNotStartUpdateProcess.Title", defaultValue: "无法启动更新流程"),
                string.Join('\n', errorMessages),
                TimeSpan.FromSeconds(10));
        }
    }


    [RelayCommand]
    private async Task SelectGameAsync(string? game)
    {
        var jasmSelectedGame = await _selectedGameService.GetSelectedGameAsync();

        if (game.IsNullOrEmpty() || game == jasmSelectedGame)
            return;

        var switchGameDialog = new ContentDialog()
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.SwitchGame.Title", defaultValue: "切换游戏"),
            Content = new TextBlock()
            {
                Text = _localizer.GetLocalizedStringOrDefault("Dialog.SwitchGame.Text",
                    defaultValue:
                    "切换游戏将重启应用。\n" +
                    "这用于确保为所选游戏正确加载配置。\n\n" +
                    "是否继续切换游戏？"),
                TextWrapping = TextWrapping.WrapWholeWords
            },

            PrimaryButtonText = string.Format(CultureInfo.CurrentUICulture,
                _localizer.GetLocalizedStringOrDefault("Dialog.SwitchGame.Primary", defaultValue: "切换到 {0}")!,
                game),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await _windowManagerService.ShowDialogAsync(switchGameDialog);

        if (result != ContentDialogResult.Primary)
        {
            SelectedGame = game;
            return;
        }

        await _selectedGameService.SetSelectedGame(game);
        await RestartAppAsync(0).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ToggleCharacterSkinsAsCharacters()
    {
        var modManagerOptions =
            await _localSettingsService.ReadOrCreateSettingAsync<ModManagerOptions>(ModManagerOptions.Section);

        var result = await new CharacterSkinsDialog().ShowDialogAsync(modManagerOptions.CharacterSkinsAsCharacters);

        if (result != ContentDialogResult.Primary)
        {
            CharacterAsSkinsCheckbox = modManagerOptions.CharacterSkinsAsCharacters;
            return;
        }


        modManagerOptions.CharacterSkinsAsCharacters = !modManagerOptions.CharacterSkinsAsCharacters;

        await _localSettingsService.SaveSettingAsync(ModManagerOptions.Section, modManagerOptions);

        CharacterAsSkinsCheckbox = modManagerOptions.CharacterSkinsAsCharacters;

        await RestartAppAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private Task NavigateToCommandsSettings()
    {
        _navigationService.NavigateTo(typeof(CommandsSettingsViewModel).FullName!,
            transitionInfo: new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ToggleModUpdateChecker()
    {
        var modUpdateCheckerSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<BackGroundModCheckerSettings>(
                BackGroundModCheckerSettings.Key);

        await Task.Run(async () =>
        {
            if (modUpdateCheckerSettings.Enabled)
                await _modUpdateAvailableChecker.DisableAutoCheckerAsync();
            else
                await _modUpdateAvailableChecker.EnableAutoCheckerAsync();

            await Task.Delay(1000).ConfigureAwait(false);
        });

        modUpdateCheckerSettings = await _localSettingsService.ReadOrCreateSettingAsync<BackGroundModCheckerSettings>(
            BackGroundModCheckerSettings.Key);

        IsModUpdateCheckerEnabled = modUpdateCheckerSettings.Enabled;
    }

    public async void OnNavigatedTo(object parameter)
    {
        SelectedGame = await _selectedGameService.GetSelectedGameAsync();
        var modUpdateCheckerOptions =
            await _localSettingsService.ReadOrCreateSettingAsync<BackGroundModCheckerSettings>(
                BackGroundModCheckerSettings.Key);

        IsModUpdateCheckerEnabled = modUpdateCheckerOptions.Enabled;

        var windowSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ScreenSizeSettings>(ScreenSizeSettings.Key);

        var characterDetailsSettings = await _localSettingsService.ReadCharacterDetailsSettingsAsync(SettingScope.App);

        PersistWindowSize = windowSettings.PersistWindowSize;
        PersistWindowPosition = windowSettings.PersistWindowPosition;
        ModCacheSizeGB = _modArchiveRepository.GetTotalCacheSizeInGB().ToString("F");
    }

    [ObservableProperty] private string _maxCacheSizeString = string.Empty;

    private void SetCacheString(int value)
    {
        MaxCacheSizeString = $"{value} GB";
    }

    [RelayCommand]
    private async Task SetCacheLimit(int maxValue)
    {
        var modArchiveSettings =
            await _localSettingsService.ReadOrCreateSettingAsync<ModArchiveSettings>(ModArchiveSettings.Key);

        modArchiveSettings.MaxLocalArchiveCacheSizeGb = maxValue;

        await _localSettingsService.SaveSettingAsync(ModArchiveSettings.Key, modArchiveSettings);

        MaxCacheLimit = maxValue;
        SetCacheString(maxValue);
    }


    [RelayCommand]
    private static Task ShowCleanModsFolderDialogAsync()
    {
        var dialog = new ClearEmptyFoldersDialog();
        return dialog.ShowDialogAsync();
    }


    [RelayCommand]
    private Task ShowDisableAllModsDialogAsync()
    {
        var dialog = new DisableAllModsDialog();
        return dialog.ShowDialogAsync();
    }

    public void OnNavigatedFrom()
    {
    }
}

public partial class ExportModsDialogModel : ObservableObject
{
    [ObservableProperty] private bool _zipMods = false;
    [ObservableProperty] private bool _keepFolderStructure = true;

    [ObservableProperty] private bool _removeJasmSettings = false;

    public ObservableCollection<CharacterCheckboxModel> CharacterModsToBackup { get; set; } = new();

    public ObservableCollection<SetModStatus> SetModStatuses { get; set; } = new()
    {
        SetModStatus.KeepCurrent,
        SetModStatus.EnableAllMods,
        SetModStatus.DisableAllMods
    };

    [ObservableProperty] private SetModStatus _setModStatus = SetModStatus.KeepCurrent;

    public ExportModsDialogModel(IEnumerable<IModdableObject> characters)
    {
        SetModStatus = SetModStatus.KeepCurrent;
        foreach (var character in characters) CharacterModsToBackup.Add(new CharacterCheckboxModel(character));
    }
}

public partial class CharacterCheckboxModel : ObservableObject
{
    [ObservableProperty] private bool _isChecked = true;
    [ObservableProperty] private IModdableObject _character;

    public CharacterCheckboxModel(IModdableObject character)
    {
        _character = character;
    }
}
