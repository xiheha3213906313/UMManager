using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using UMManager.Core.Contracts.Services;
using UMManager.WinUI.Services.AppManagement;
using UMManager.WinUI.ViewModels;
using UMManager.WinUI.Views.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace UMManager.WinUI.Views;

public sealed partial class PresetPage : Page
{
    public PresetViewModel ViewModel { get; } = App.GetService<PresetViewModel>();

    public PresetPage()
    {
        InitializeComponent();
        PresetsList.DragItemsCompleted += PresetsList_DragItemsCompleted;
    }

    private async void PresetsList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult == DataPackageOperation.Move && ViewModel.ReorderPresetsCommand.CanExecute(null))
        {
            await ViewModel.ReorderPresetsCommand.ExecuteAsync(null);
        }
    }

    private async void UIElement_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var presetVm = (ModPresetVm)((EditableTextBlock)sender).DataContext;

        if (e.Key == VirtualKey.Enter && ViewModel.RenamePresetCommand.CanExecute(presetVm))
        {
            await ViewModel.RenamePresetCommand.ExecuteAsync(presetVm);
        }
    }

    private TextBlock CreateTextBlock(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true
        };
    }

    private async void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        var localizer = App.GetService<ILanguageLocalizer>();
        var text = localizer.GetLocalizedStringOrDefault("Dialog.PresetsHowWork.Text",
                       defaultValue:
                       "预设是要启用的模组列表及其偏好设置（例如 .UMManager_ModConfig.json）。\n\n" +
                       "创建预设时，会保存当前启用的模组及其偏好设置。\n\n" +
                       "可通过启动 Elevator 并启用 Auto Sync 让 UMManager 处理 3Dmigoto 重载，从而在游戏内自动刷新已启用模组。\n\n" +
                       "也可以不使用预设，只用手动控件来持久化偏好设置。") ??
                   string.Empty;
        var paragraphs = text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        var dialog = new ContentDialog
        {
            Title = localizer.GetLocalizedStringOrDefault("Dialog.PresetsHowWork.Title", defaultValue: "预设如何工作"),
            CloseButtonText = localizer.GetLocalizedStringOrDefault("Common.Button.Close", defaultValue: "关闭"),
            DefaultButton = ContentDialogButton.Close,
            Content = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    CreateTextBlock(paragraphs.ElementAtOrDefault(0) ?? string.Empty),
                    CreateTextBlock(paragraphs.ElementAtOrDefault(1) ?? string.Empty),
                    CreateTextBlock(paragraphs.ElementAtOrDefault(2) ?? string.Empty),
                    CreateTextBlock(paragraphs.ElementAtOrDefault(3) ?? string.Empty)
                }
            }
        };

        await App.GetService<IWindowManagerService>().ShowDialogAsync(dialog).ConfigureAwait(false);
    }

    private void DragHandleIcon_OnPointerEntered(object sender, PointerRoutedEventArgs e) =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);

    private void DragHandleIcon_OnPointerExited(object sender, PointerRoutedEventArgs e) =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
}
