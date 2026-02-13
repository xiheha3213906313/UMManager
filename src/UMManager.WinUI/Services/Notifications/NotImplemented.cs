using UMManager.Core.Contracts.Services;

namespace UMManager.WinUI.Services.Notifications;

// This is a static class to easily  launch a not implemented notification from different places in the app.
internal static class NotImplemented
{
    public static NotificationManager NotificationManager { get; set; } = null!;

    public static void Show(string? message = null, TimeSpan? time = null)
    {
        var localizer = App.GetService<ILanguageLocalizer>();
        NotificationManager.ShowNotification(
            localizer.GetLocalizedStringOrDefault("Notification.NotImplemented.Title", defaultValue: "未实现"),
            message ?? localizer.GetLocalizedStringOrDefault("Notification.NotImplemented.Message", defaultValue: "该功能暂未实现。"),
            time ?? TimeSpan.FromSeconds(2));
    }
}
