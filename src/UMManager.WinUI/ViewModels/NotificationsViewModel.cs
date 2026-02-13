using Windows.Storage;
using Windows.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UMManager.Core.Contracts.Services;
using UMManager.WinUI.Services.Notifications;

namespace UMManager.WinUI.ViewModels;

public partial class NotificationsViewModel : ObservableRecipient
{
    public readonly NotificationManager NotificationManager;

    [ObservableProperty]
    private string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "log.txt");

    public NotificationsViewModel(NotificationManager notificationManager)
    {
        NotificationManager = notificationManager;
    }

    [RelayCommand]
    private async Task CopyLogFilePathAsync()
    {
        var localizer = App.GetService<ILanguageLocalizer>();
        if (!File.Exists(LogFilePath))
        {
            NotificationManager.ShowNotification(
                localizer.GetLocalizedStringOrDefault("Notification.LogFileNotFound.Title", defaultValue: "未找到日志文件"),
                "",
                null);
            return;
        }

        var openResult = await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(LogFilePath));
        if (!openResult)
            NotificationManager.ShowNotification(
                localizer.GetLocalizedStringOrDefault("Notification.LogFileCouldNotBeOpened.Title", defaultValue: "无法打开日志文件"),
                "",
                null);
    }
}
