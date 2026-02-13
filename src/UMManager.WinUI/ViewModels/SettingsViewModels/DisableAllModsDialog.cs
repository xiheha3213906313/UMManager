using System.Text;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;
using UMManager.WinUI.Services.AppManagement;
using UMManager.WinUI.Services.Notifications;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace UMManager.WinUI.ViewModels.SettingsViewModels;

public class DisableAllModsDialog
{
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly IGameService _gameService = App.GetService<IGameService>();
    private readonly NotificationManager _notificationManager = App.GetService<NotificationManager>();
    private readonly IWindowManagerService _windowManagerService = App.GetService<IWindowManagerService>();
    private readonly ILogger _logger = App.GetService<ILogger>().ForContext<DisableAllModsDialog>();
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public async Task ShowDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.DisableMods.Title", defaultValue: "禁用模组"),
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Dialog.DisableMods.Primary",
                defaultValue: "禁用所选分类中的模组"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
            DefaultButton = ContentDialogButton.Primary
        };


        var categories = _gameService.GetCategories();

        var stackPanel = new StackPanel();

        stackPanel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("Dialog.DisableMods.SelectCategories", defaultValue: "选择要禁用模组的分类："),
            IsTextSelectionEnabled = true
        });


        foreach (var category in categories)
        {
            var checkBox = new CheckBox
            {
                Content = category.DisplayNamePlural,
                IsChecked = true
            };

            stackPanel.Children.Add(checkBox);
        }


        stackPanel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("Dialog.DisableMods.SuggestPreset",
                defaultValue:
                "如果启用的模组很多，建议先创建预设（或备份）再执行禁用。\n\n" +
                "仅会禁用所选分类中由 UMManager 跟踪的模组。"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 10, 0, 0)
        });


        dialog.Content = stackPanel;

        var result = await _windowManagerService.ShowDialogAsync(dialog);


        if (result != ContentDialogResult.Primary)
        {
            return;
        }


        var selectedCategories = stackPanel.Children
            .OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => categories.First(cat => cat.DisplayNamePlural.Equals(c.Content)))
            .ToList();

        if (selectedCategories.Count == 0)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.NoCategoriesSelected.Title", defaultValue: "未选择分类"),
                _localizer.GetLocalizedStringOrDefault("Notification.NoCategoriesSelected.DisableMessage",
                    defaultValue: "未选择任何要禁用模组的分类。"),
                TimeSpan.FromSeconds(5));
            return;
        }


        var modLists = _skinManagerService.CharacterModLists.Where(m => selectedCategories.Contains(m.Character.ModCategory)).ToList();

        var modListDisableTask = new List<Task<List<string>>>();


        foreach (var modList in modLists)
        {
            var task = Task.Run(() =>
            {
                var modsToDisable = modList.Mods.Where(m => m.IsEnabled).ToArray();
                var errors = new List<string>();
                foreach (var modEntry in modsToDisable)
                {
                    try
                    {
                        modList.DisableMod(modEntry.Id);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Error while disabling mod.");
                        errors.Add($"{modEntry.Mod.FullPath}: {e.Message}");
                    }
                }

                return errors;
            });

            modListDisableTask.Add(task);
        }

        var errorsList = await Task.WhenAll(modListDisableTask);
        var errors = errorsList.SelectMany(e => e).ToArray();

        if (errors.Length == 0)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.ModsDisabled.Title", defaultValue: "模组已禁用"),
                string.Format(_localizer.GetLocalizedStringOrDefault("Notification.ModsDisabled.Message",
                        defaultValue: "已禁用所选分类中所有由 UMManager 跟踪的模组：{0}")!,
                    string.Join(',', selectedCategories.Select(c => c.DisplayNamePlural))),
                TimeSpan.FromSeconds(5));
            return;
        }


        var sb = new StringBuilder();
        sb.AppendLine(_localizer.GetLocalizedStringOrDefault("Notification.DisableModsErrors.Header",
            defaultValue: "以下模组发生错误："));

        foreach (var error in errors)
        {
            sb.AppendLine(error);
        }


        _notificationManager.ShowNotification(
            _localizer.GetLocalizedStringOrDefault("Notification.DisableModsErrors.Title", defaultValue: "禁用模组时发生错误"),
            sb.ToString(),
            TimeSpan.FromSeconds(10));
    }
}
