using System.Globalization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UMManager.Core.Contracts.Entities;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.Helpers;
using UMManager.Core.Services;
using UMManager.Core.Services.GameBanana;
using UMManager.Core.Services.GameBanana.Models;
using UMManager.WinUI.Services.ModHandling;
using UMManager.WinUI.Services.Notifications;
using Microsoft.UI.Dispatching;
using Serilog;

namespace UMManager.WinUI.ViewModels.ModPageViewModels;

public partial class ModPageVM : ObservableRecipient
{
    private readonly GameBananaCoreService _gameBananaCoreService = App.GetService<GameBananaCoreService>();
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly ModNotificationManager _modNotificationManager = App.GetService<ModNotificationManager>();
    private readonly NotificationManager _notificationManager = App.GetService<NotificationManager>();
    private readonly ModInstallerService _modInstallerService = App.GetService<ModInstallerService>();
    private readonly ArchiveService _archiveService = App.GetService<ArchiveService>();

    private readonly DispatcherQueue _dispatcherQueue;
    private ICharacterModList _characterModList = null!;
    private readonly IModdableObject _moddableObject;
    private readonly WindowEx _window;
    private List<ModFileInfo> _modFiles = new();
    private GbModId _gbModId = null!;
    private readonly CancellationToken _ct;

    private ModPageInfo? _modPageInfo;

    [ObservableProperty] private string _initializing = "true";

    [ObservableProperty] private string _modName = string.Empty;

    [ObservableProperty] private Uri _modPage = new("https://gamebanana.com/");

    [ObservableProperty] private Uri? _characterModListPath = null;

    [ObservableProperty] private bool _isOpenDownloadButtonEnabled = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand), nameof(StartDownloadCommand),
        nameof(StartInstallCommand))]
    private bool _isWindowBusy = false;

    public bool IsNotBusy => !IsWindowBusy;


    public readonly ObservableCollection<ModFileInfoVm> ModFileInfos = new();
    private readonly ILogger _logger = App.GetService<ILogger>().ForContext<ModUpdateVM>();

    public ModPageVM(Uri modPage, IModdableObject moddableObject, WindowEx window, CancellationToken ctsToken)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _moddableObject = moddableObject;
        ModPage = modPage;
        _window = window;
        var localizer = App.GetService<ILanguageLocalizer>();
        _window.Title = localizer.GetLocalizedStringOrDefault("Window.ModDownloads.Title", defaultValue: "下载模组文件");
        _ct = ctsToken;
        Initialize();
    }

    private async void Initialize()
    {
        try
        {
            await InternalInitialize();
            Initializing = "false";
        }
        catch (OperationCanceledException)
        {
            _window.Close();
        }
        catch (Exception e)
        {
            await LogErrorAndClose(e);
        }
    }


    private async Task InternalInitialize()
    {
        IsWindowBusy = true;

        if (GameBananaUrlHelper.TryGetModIdFromUrl(ModPage, out var modId))
            _gbModId = modId;
        else
            await LogErrorAndClose(new InvalidGameBananaUrlException($"Invalid GameBanana url: {ModPage}"));


        _characterModList = _skinManagerService.GetCharacterModList(_moddableObject);

        if (!await _gameBananaCoreService.HealthCheckAsync(_ct))
        {
            await LogErrorAndClose(
                new InvalidOperationException("Failed to get mod page info, GameBanana Api is not available"));
            return;
        }

        using (var _ = IgnorePollyLimiterScope.Ignore())
        {
            _modPageInfo = await _gameBananaCoreService.GetModProfileAsync(_gbModId, _ct);
        }

        if (_modPageInfo is null)
        {
            await LogErrorAndClose(new InvalidOperationException("Failed to get mod page info, mod does not exist"));
            return;
        }

        var localizer = App.GetService<ILanguageLocalizer>();
        _window.Title = string.Format(CultureInfo.CurrentUICulture,
            localizer.GetLocalizedStringOrDefault("Window.ModDownloads.ForMod.Title",
                defaultValue: "下载：{0}")!,
            _modPageInfo.ModName);
        CharacterModListPath = new Uri(_characterModList.AbsModsFolderPath);

        _modFiles = _modPageInfo.Files.ToList();

        foreach (var modFile in _modFiles)
        {
            var vm = new ModFileInfoVm(modFile, StartDownloadCommand, StartInstallCommand)
            {
                IsBusy = true
            };
            ModFileInfos.Add(vm);
            await InitializeModFileVmAsync(vm);
        }

        IsWindowBusy = false;
    }

    private Task LogErrorAndClose(Exception e)
    {
        _logger.Error(e, "Failed to get mod update info");
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            var localizer = App.GetService<ILanguageLocalizer>();
            App.GetService<NotificationManager>().ShowNotification(
                localizer.GetLocalizedStringOrDefault("Notification.GetModUpdateInfoFailed.Title",
                    defaultValue: "获取模组更新信息失败"),
                e.Message,
                TimeSpan.FromSeconds(10));
        });
        _window.Close();
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private Task CloseAsync()
    {
        _window.Close();
        return Task.CompletedTask;
    }


    private async Task InitializeModFileVmAsync(ModFileInfoVm fileInfoVm)
    {
        ArgumentNullException.ThrowIfNull(fileInfoVm);

        var existingArchive = await _gameBananaCoreService.GetLocalModArchiveByMd5HashAsync(fileInfoVm.Md5Hash, _ct);

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (existingArchive is not null)
            {
                fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
                fileInfoVm.DownloadProgress = 100;
                fileInfoVm.ArchiveFile = new FileInfo(existingArchive.FullName);
            }

            fileInfoVm.IsBusy = false;
        });
    }

    private bool CanStartDownload(ModFileInfoVm? fileInfoVm)
    {
        if (fileInfoVm is null)
            return false;

        var anyOtherDownloading = ModFileInfos.Any(x =>
            fileInfoVm.FileId != x.FileId && x.Status == ModFileInfoVm.InstallStatus.Downloading);

        var canDownload = IsNotBusy && !fileInfoVm.IsBusy && fileInfoVm.Status == ModFileInfoVm.InstallStatus.NotStarted
                          && !anyOtherDownloading && fileInfoVm.ArchiveFile is null &&
                          !fileInfoVm.ModId.IsNullOrEmpty() && !fileInfoVm.FileId.IsNullOrEmpty();


        return canDownload;
    }

    [RelayCommand(CanExecute = nameof(CanStartDownload))]
    private async Task StartDownload(ModFileInfoVm fileInfoVm)
    {
        try
        {
            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloading;
            fileInfoVm.IsBusy = true;

            var identifier = new GbModFileIdentifier(new GbModId(fileInfoVm.ModId), new GbModFileId(fileInfoVm.FileId));

            var archivePath =
                await Task.Run(() => _gameBananaCoreService.DownloadModAsync(identifier, fileInfoVm.Progress, _ct),
                    _ct);

            fileInfoVm.ArchiveFile = new FileInfo(archivePath);
            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
        }
        catch (Exception) when (_ct.IsCancellationRequested)
        {
            Reset();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to download mod file");

            var localizer = App.GetService<ILanguageLocalizer>();
            _notificationManager.ShowNotification(
                localizer.GetLocalizedStringOrDefault("Notification.DownloadModFileFailed.Title",
                    defaultValue: "下载模组文件失败"),
                e.Message,
                TimeSpan.FromSeconds(10));

            Reset();
        }
        finally
        {
            fileInfoVm.IsBusy = false;
        }

        void Reset()
        {
            fileInfoVm.Status = ModFileInfoVm.InstallStatus.NotStarted;
            fileInfoVm.ArchiveFile = null;
            fileInfoVm.DownloadProgress = 0;
        }
    }

    private bool CanInstall(ModFileInfoVm? fileInfoVm)
    {
        if (fileInfoVm is null)
            return false;

        var anyOtherInstalling = ModFileInfos.Any(x =>
            fileInfoVm.FileId != x.FileId && x.Status == ModFileInfoVm.InstallStatus.Installing);

        var canInstall = IsNotBusy && !fileInfoVm.IsBusy &&
                         fileInfoVm.Status == ModFileInfoVm.InstallStatus.Downloaded &&
                         !anyOtherInstalling && fileInfoVm.ArchiveFile is not null;

        return canInstall;
    }


    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task StartInstall(ModFileInfoVm fileInfoVm)
    {
        IsWindowBusy = true;
        fileInfoVm.IsBusy = true;

        fileInfoVm.Status = ModFileInfoVm.InstallStatus.Installing;

        try
        {
            var result = await Task.Run(async () =>
            {
                var modFolder = _archiveService.ExtractArchive(fileInfoVm.ArchiveFile!.FullName,
                    App.GetUniqueTmpFolder().FullName);

                var archiveNameSections = Path.GetFileName(modFolder.Name).Split(ModArchiveRepository.Separator);
                if (archiveNameSections.Length != 4)
                    throw new InvalidArchiveNameFormatException();

                var modFolderName = archiveNameSections[0];
                var modFolderExt = Path.GetExtension(modFolder.Name);

                var modFolderParent = modFolder.Parent!;

                var zipRoot = Directory.CreateDirectory(Path.Combine(modFolderParent.FullName, "ArchiveRoot"));

                modFolder.MoveTo(Path.Combine(zipRoot.FullName, $"{modFolderName}{modFolderExt}"));

                var modUrl = _modPageInfo?.ModPageUrl;

                using var task = await _modInstallerService.StartModInstallationAsync(zipRoot, _characterModList,
                    setup: options =>
                    {
                        options.ModUrl = modUrl;
                    }).ConfigureAwait(false);

                return await task.WaitForCloseAsync(_ct).ConfigureAwait(false);
            }, _ct);

            if (result.CloseReason == CloseRequestedArgs.CloseReasons.Error)
            {
                if (result.Exception is not null)
                {
                    throw new Exception("An error occured during mod install, see logs and inner exception",
                        result.Exception);
                }

                throw new Exception("An error occured during mod install, see logs");
            }

            if (result.CloseReason == CloseRequestedArgs.CloseReasons.Canceled)
            {
                fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
                return;
            }

            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Installed;
        }
        catch (TaskCanceledException)
        {
            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to install mod file");

            var localizer = App.GetService<ILanguageLocalizer>();
            _notificationManager.ShowNotification(
                localizer.GetLocalizedStringOrDefault("Notification.InstallModFileFailed.Title",
                    defaultValue: "安装模组文件失败"),
                e.InnerException?.Message ?? e.Message,
                TimeSpan.FromSeconds(10));

            fileInfoVm.Status = ModFileInfoVm.InstallStatus.Downloaded;
        }
        finally
        {
            IsWindowBusy = false;
            fileInfoVm.IsBusy = false;
        }
    }
}
