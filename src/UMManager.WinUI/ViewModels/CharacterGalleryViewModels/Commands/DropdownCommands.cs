using System.Globalization;
using Windows.System;
using CommunityToolkit.Mvvm.Input;
using UMManager.Core.Contracts.Services;
using UMManager.Core.Helpers;
using Microsoft.UI.Xaml.Controls;
using UMManager.WinUI.Services.Notifications;
using Windows.Storage;
using UMManager.WinUI.Services;
using UMManager.WinUI.Contracts.Services;
using UMManager.WinUI.Services.AppManagement;
using UMManager.WinUI.Models.Settings;

namespace UMManager.WinUI.ViewModels.CharacterGalleryViewModels;

public partial class CharacterGalleryViewModel
{
    private bool CanOpenModFolder(ModGridItemVm? vm) =>
        vm is not null && !IsNavigating && !IsBusy && !vm.FolderPath.IsNullOrEmpty() && Directory.Exists(vm.FolderPath);

    [RelayCommand(CanExecute = nameof(CanOpenModFolder))]
    private async Task OpenModFolder(ModGridItemVm vm)
    {
        await Launcher.LaunchFolderPathAsync(vm.FolderPath);
    }


    private bool CanOpenModUrl(ModGridItemVm? vm) => vm is not null && !IsNavigating && !IsBusy && vm.HasModUrl;

    [RelayCommand(CanExecute = nameof(CanOpenModUrl))]
    private async Task OpenModUrl(ModGridItemVm vm)
    {
        await Launcher.LaunchUriAsync(vm.ModUrl);
    }

    /// <summary>
    /// return the result of the dialog and if the checkbox "Do not ask again" is checked
    /// </summary>
    private async Task<(ContentDialogResult, bool)> PromptDeleteDialog(ModGridItemVm vm)
    {
        var windowManager = App.GetService<IWindowManagerService>();
        var localizer = App.GetService<ILanguageLocalizer>();

        var doNotAskAgainCheckBox = new CheckBox()
        {
            Content = localizer.GetLocalizedStringOrDefault("Common.CheckBox.DoNotAskAgain", defaultValue: "不再询问"),
            IsChecked = false,
        };
        var stackPanel = new StackPanel()
        {
            Children =
            {
                new TextBlock()
                {
                    Text = string.Format(CultureInfo.CurrentUICulture,
                        localizer.GetLocalizedStringOrDefault("Dialog.DeleteMod.Text", defaultValue: "确定要删除 {0} 吗？")!,
                        vm.Name),
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.WrapWholeWords,
                },
                doNotAskAgainCheckBox
            }
        };

        var dialog = new ContentDialog()
        {
            Title = localizer.GetLocalizedStringOrDefault("Dialog.DeleteMod.Title", defaultValue: "删除模组"),
            Content = stackPanel,
            PrimaryButtonText = localizer.GetLocalizedStringOrDefault("Common.Button.Delete", defaultValue: "删除"),
            CloseButtonText = localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
            DefaultButton = ContentDialogButton.Primary,
        };

        // get result and check if checkbox is checked
        var result = await windowManager.ShowDialogAsync(dialog);
        var doNotAskAgain = doNotAskAgainCheckBox.IsChecked == true;

        return (result, doNotAskAgain);
    }

    [RelayCommand(CanExecute = nameof(CanOpenModFolder))]
    private async Task DeleteMod(ModGridItemVm vm)
    {
        if (_modList is null) { return; }

        var notificationManager = App.GetService<NotificationManager>();
        var localizer = App.GetService<ILanguageLocalizer>();
        var settings =
            await _localSettingsService
                .ReadOrCreateSettingAsync<CharacterGallerySettings>(CharacterGallerySettings.Key);

        if (settings.CanDeleteDialogPrompt)
        {
            var (result, doNotAskAgainChecked) = await PromptDeleteDialog(vm);
            if (doNotAskAgainChecked)
            {
                settings.CanDeleteDialogPrompt = false;
                await _localSettingsService.SaveSettingAsync(CharacterGallerySettings.Key, settings);
            }

            if (result != ContentDialogResult.Primary)
            {
                return;
            }
        }

        try
        {
            _modList.DeleteModBySkinEntryId(vm.Id);
            await ReloadModsAsync();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to delete mod");
            notificationManager.ShowNotification(
                localizer.GetLocalizedStringOrDefault("Notification.DeleteModFailed.Title", defaultValue: "删除模组失败"),
                e.Message,
                TimeSpan.FromSeconds(10));
            return;
        }

        notificationManager.ShowNotification(
            localizer.GetLocalizedStringOrDefault("Notification.ModDeleted.Title", defaultValue: "模组已删除"),
            string.Format(CultureInfo.CurrentUICulture,
                localizer.GetLocalizedStringOrDefault("Notification.ModDeleted.Message", defaultValue: "已删除 {0}")!,
                vm.Name),
            TimeSpan.FromSeconds(5));
    }
}
