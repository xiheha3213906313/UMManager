using UMManager.Core.Contracts.Services;
using UMManager.WinUI.Services.AppManagement;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UMManager.WinUI.ViewModels.SettingsViewModels;

internal class CharacterSkinsDialog
{
    private readonly IWindowManagerService _windowManagerService = App.GetService<IWindowManagerService>();
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public async Task<ContentDialogResult> ShowDialogAsync(bool isEnabled)
    {
        var dialog = new ContentDialog()
        {
            Title = isEnabled
                ? _localizer.GetLocalizedStringOrDefault("Dialog.CharacterSkins.DisableTitle", defaultValue: "将角色皮肤视为独立角色？（禁用）")
                : _localizer.GetLocalizedStringOrDefault("Dialog.CharacterSkins.EnableTitle", defaultValue: "将角色皮肤视为独立角色？（启用）"),
            Content = new TextBlock()
            {
                Text = isEnabled
                    ? _localizer.GetLocalizedStringOrDefault("Dialog.CharacterSkins.DisableText",
                        defaultValue:
                        "禁用后，UMManager 会把游戏内皮肤在“角色总览”中作为基础角色的皮肤展示。\n" +
                        "这目前是 UMManager 的默认行为。\n" +
                        "UMManager 不会移动或删除你的任何模组。\n\n" +
                        "确定要禁用吗？禁用后 UMManager 将重启……")
                    : _localizer.GetLocalizedStringOrDefault("Dialog.CharacterSkins.EnableText",
                        defaultValue:
                        "启用后，UMManager 会把游戏内皮肤在“角色总览”中作为独立角色展示。\n" +
                        "该选项未来可能成为默认设置。\n" +
                        "UMManager 不会移动或删除你的任何模组。\n\n" +
                        "确定要启用吗？启用后 UMManager 将重启……"),
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true
            },
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonText = isEnabled
                ? _localizer.GetLocalizedStringOrDefault("Common.Button.Disable", defaultValue: "禁用")
                : _localizer.GetLocalizedStringOrDefault("Common.Button.Enable", defaultValue: "启用"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消")
        };


        return await _windowManagerService.ShowDialogAsync(dialog).ConfigureAwait(false);
    }

}
