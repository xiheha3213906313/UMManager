using System.Text;
using UMManager.Core.Contracts.Services;
using UMManager.WinUI.Services.AppManagement;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NotificationManager = UMManager.WinUI.Services.Notifications.NotificationManager;

namespace UMManager.WinUI.ViewModels.SettingsViewModels;

internal class ClearEmptyFoldersDialog
{
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly NotificationManager _notificationManager = App.GetService<NotificationManager>();
    private readonly IWindowManagerService _windowManagerService = App.GetService<IWindowManagerService>();
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();


    public async Task ShowDialogAsync()
    {
        var dialog = new ContentDialog()
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.ClearEmptyFolders.Title", defaultValue: "清理空文件夹"),
            Content = new TextBlock()
            {
                Text = _localizer.GetLocalizedStringOrDefault("Dialog.ClearEmptyFolders.Text",
                    defaultValue:
                    "将删除角色 modList 中的所有空文件夹（空文件夹或仅包含 .UMManager_ 文件/文件夹的目录）。\n" +
                    "如果角色文件夹为空，也会一并删除。\n" +
                    "Mods 根目录下的空文件夹也会被删除"),
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true
            },
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Delete", defaultValue: "删除"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消")
        };


        var result = await _windowManagerService.ShowDialogAsync(dialog);

        if (result == ContentDialogResult.Primary)
        {
            var deletedFolders = await Task.Run(() => _skinManagerService.CleanCharacterFolders());
            var sb = new StringBuilder();
            sb.AppendLine(_localizer.GetLocalizedStringOrDefault("Notification.EmptyFoldersDeleted.Header",
                defaultValue: "已删除文件夹："));
            foreach (var folder in deletedFolders)
            {
                sb.AppendLine(folder.FullName);
            }

            var message = sb.ToString();

            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.EmptyFoldersDeleted.Title", defaultValue: "空文件夹已删除"),
                message,
                TimeSpan.FromSeconds(5));
        }
    }
}
