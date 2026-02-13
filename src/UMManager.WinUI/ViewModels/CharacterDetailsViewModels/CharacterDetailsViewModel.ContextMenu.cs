using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace UMManager.WinUI.ViewModels.CharacterDetailsViewModels;

public partial class CharacterDetailsViewModel
{
    private static bool _removeFromPresetCheckBox = false;
    private static bool _moveToRecycleBinCheckBox = true;

    private record ModToDelete(Guid Id, string DisplayName, string FolderPath, string FolderName)
    {
        public ModToDelete(ModToDelete m, Exception e, string? presetName = null) : this(m.Id, m.DisplayName, m.FolderPath, m.FolderName)
        {
            Exception = e;
            PresetName = presetName;
        }

        public Exception? Exception { get; }
        public string? PresetName { get; }
    }

    private bool CanDeleteMods() => IsNavigationFinished && !IsHardBusy && !IsSoftBusy && ModGridVM.SelectedMods.Count > 0;

    [RelayCommand(CanExecute = nameof(CanDeleteMods))]
    private async Task DeleteModsAsync()
    {
        var selectedMods = ModGridVM.SelectedMods.Select(m => new ModToDelete(m.Id, m.DisplayName, m.AbsFolderPath, m.FolderName)).ToList();

        if (selectedMods.Count == 0)
            return;


        var shownCharacterName = ShownModObject.DisplayName;
        var selectedModsCount = selectedMods.Count;

        var modsToDeleteErrored = new List<ModToDelete>();
        var modsToDeletePresetError = new List<ModToDelete>();

        var modsDeleted = new List<ModToDelete>(selectedModsCount);

        var moveToRecycleBinCheckBox = new CheckBox()
        {
            Content = _localizer.GetLocalizedStringOrDefault("Dialog.DeleteMods.MoveToRecycleBin",
                defaultValue: "移到回收站？"),
            IsChecked = _moveToRecycleBinCheckBox
        };

        var removeFromPresetsCheckBox = new CheckBox()
        {
            Content = _localizer.GetLocalizedStringOrDefault("Dialog.DeleteMods.RemoveFromPresets",
                defaultValue: "从预设中移除？"),
            IsChecked = _removeFromPresetCheckBox
        };


        var mods = new ListView()
        {
            ItemsSource = selectedMods.Select(m => m.DisplayName + " - " + m.FolderName),
            SelectionMode = ListViewSelectionMode.None
        };

        var scrollViewer = new ScrollViewer()
        {
            Content = mods,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 400
        };
        var stackPanel = new StackPanel()
        {
            Children =
            {
                moveToRecycleBinCheckBox,
                removeFromPresetsCheckBox,
                scrollViewer
            }
        };

        var contentWrapper = new Grid()
        {
            MinWidth = 500,
            Children =
            {
                stackPanel
            }
        };

        var dialog = new ContentDialog()
        {
            Title = string.Format(CultureInfo.CurrentUICulture,
                _localizer.GetLocalizedStringOrDefault("Dialog.DeleteMods.Title", defaultValue: "确定删除这 {0} 个模组？")!,
                selectedModsCount),
            Content = contentWrapper,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Delete", defaultValue: "删除"),
            SecondaryButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
            DefaultButton = ContentDialogButton.Primary
        };


        var result = await _windowManagerService.ShowDialogAsync(dialog);

        var recycleMods = moveToRecycleBinCheckBox.IsChecked == true;
        var removeFromPresets = removeFromPresetsCheckBox.IsChecked == true;
        _moveToRecycleBinCheckBox = recycleMods;
        _removeFromPresetCheckBox = removeFromPresets;


        if (result != ContentDialogResult.Primary)
            return;


        await CommandWrapperAsync(true, async () =>
        {
            await Task.Run(async () =>
            {
                if (removeFromPresets)
                {
                    var modIdToPresetMap = await _presetService.FindPresetsForModsAsync(selectedMods.Select(m => m.Id), CancellationToken.None)
                        .ConfigureAwait(false);

                    foreach (var mod in selectedMods)
                    {
                        if (!modIdToPresetMap.TryGetValue(mod.Id, out var presets)) continue;

                        foreach (var preset in presets)
                        {
                            try
                            {
                                await _presetService.DeleteModEntryAsync(preset.Name, mod.Id, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                _logger.Error(e, "Error removing mod: {ModName} from preset: {PresetName} | mod path: {ModPath} ", mod.DisplayName,
                                    mod.FolderPath,
                                    preset.Name);
                                modsToDeletePresetError.Add(new ModToDelete(mod, e));
                            }
                        }
                    }
                }

                foreach (var mod in selectedMods)
                {
                    try
                    {
                        _modList.DeleteModBySkinEntryId(mod.Id, recycleMods);
                        modsDeleted.Add(mod);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Error deleting mod {ModName} | {ModPath}", mod.DisplayName, mod.FolderPath);

                        modsToDeleteErrored.Add(new ModToDelete(mod, e));
                    }
                }
            });

            ModGridVM.QueueModRefresh();


            if (modsToDeleteErrored.Count > 0 || modsToDeletePresetError.Count > 0)
            {
                var content = new StringBuilder();

                content.AppendLine(_localizer.GetLocalizedStringOrDefault("Notification.DeleteModsError.Header",
                    defaultValue: "删除模组出错："));


                if (modsToDeletePresetError.Count > 0)
                {
                    content.AppendLine(_localizer.GetLocalizedStringOrDefault("Notification.DeleteModsError.PresetHeader",
                        defaultValue: "预设处理出错的模组："));
                    foreach (var mod in modsToDeletePresetError)
                    {
                        content.AppendLine($"- {mod.DisplayName}");
                        content.AppendLine($"  - {mod.Exception?.Message}");
                        content.AppendLine($"  - {mod.PresetName}");
                    }
                }

                if (modsToDeleteErrored.Count > 0)
                {
                    content.AppendLine(_localizer.GetLocalizedStringOrDefault("Notification.DeleteModsError.DeleteHeader",
                        defaultValue: "删除出错的模组："));
                    foreach (var mod in modsToDeleteErrored)
                    {
                        content.AppendLine($"- {mod.DisplayName}");
                        content.AppendLine($"  - {mod.Exception?.Message}");
                    }
                }

                _notificationService.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("Notification.DeleteModsError.Title", defaultValue: "删除模组失败"),
                    content.ToString(),
                    TimeSpan.FromSeconds(10));
                return;
            }


            _notificationService.ShowNotification(
                string.Format(CultureInfo.CurrentUICulture,
                    _localizer.GetLocalizedStringOrDefault("Notification.ModsDeleted.Title", defaultValue: "已删除 {0} 个模组")!,
                    modsDeleted.Count),
                string.Format(CultureInfo.CurrentUICulture,
                    _localizer.GetLocalizedStringOrDefault("Notification.ModsDeleted.Message",
                        defaultValue: "已在 {1} 的 Mods 文件夹中成功删除：{0}")!,
                    string.Join(", ", selectedMods.Select(m => m.DisplayName)),
                    shownCharacterName),
                TimeSpan.FromSeconds(5));
        }).ConfigureAwait(false);
    }
}
