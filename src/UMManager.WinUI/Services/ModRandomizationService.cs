using System;
using System.Linq;
using System.Threading.Tasks;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.Services;
using UMManager.WinUI.Contracts.Services;
using UMManager.WinUI.Services.AppManagement;
using UMManager.WinUI.Services.Notifications;
using UMManager.WinUI.Services.ModHandling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace UMManager.WinUI.Services;

public class ModRandomizationService
{
    private readonly IGameService _gameService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly IWindowManagerService _windowManagerService;
    private readonly CharacterSkinService _characterSkinService;
    private readonly ElevatorService _elevatorService;
    private readonly NotificationManager _notificationManager;
    private readonly ILogger _logger;
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();
    private static readonly Random Random = new();

    public ModRandomizationService(
        IGameService gameService,
        ISkinManagerService skinManagerService,
        IWindowManagerService windowManagerService,
        CharacterSkinService characterSkinService,
        ElevatorService elevatorService,
        NotificationManager notificationManager,
        ILogger logger)
    {
        _gameService = gameService;
        _skinManagerService = skinManagerService;
        _windowManagerService = windowManagerService;
        _characterSkinService = characterSkinService;
        _elevatorService = elevatorService;
        _notificationManager = notificationManager;
        _logger = logger.ForContext<ModRandomizationService>();
    }

    public async Task ShowRandomizeModsDialog()
    {
        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.RandomizeMods.Title", defaultValue: "随机启用模组"),
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Dialog.RandomizeMods.Primary", defaultValue: "随机"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
            DefaultButton = ContentDialogButton.Primary
        };

        var categories = _gameService.GetCategories();
        var stackPanel = new StackPanel();

        stackPanel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("Dialog.RandomizeMods.SelectCategories", defaultValue: "选择要随机的分类：")
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("Dialog.RandomizeMods.Note",
                defaultValue:
                "注意：仅会对“预期同时只启用一个模组”的文件夹进行随机；“Others __”文件夹不会随机。对于有多个游戏内皮肤的角色，将在每个皮肤下最多启用一个模组。"),
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 0, 0, 10)
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

        stackPanel.Children.Add(new CheckBox
        {
            Margin = new Thickness(0, 10, 0, 0),
            Content = _localizer.GetLocalizedStringOrDefault("Dialog.RandomizeMods.AllowNone",
                defaultValue: "允许结果为空（某个模组文件夹可能最终不启用任何模组）"),
            IsChecked = false
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("Dialog.RandomizeMods.SuggestPreset",
                defaultValue: "如果启用的模组很多，建议先创建预设（或备份）再随机。"),
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
            .SkipLast(1)
            .Where(c => c.IsChecked == true)
            .Select(c => categories.First(cat => cat.DisplayNamePlural.Equals(c.Content)))
            .ToList();

        var allowNoMods = stackPanel.Children
            .OfType<CheckBox>()
            .Last()
            .IsChecked == true;

        if (selectedCategories.Count == 0)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.NoCategoriesSelected.Title", defaultValue: "未选择分类"),
                _localizer.GetLocalizedStringOrDefault("Notification.NoCategoriesSelected.RandomizeMessage",
                    defaultValue: "未选择任何要随机的分类。"),
                TimeSpan.FromSeconds(5));
            return;
        }

        try
        {
            await Task.Run(async () =>
            {
                var modLists = _skinManagerService.CharacterModLists
                    .Where(modList => selectedCategories.Contains(modList.Character.ModCategory))
                    .Where(modList => !modList.Character.IsMultiMod)
                    .ToList();

                foreach (var modList in modLists)
                {
                    var mods = modList.Mods.ToList();

                    if (mods.Count == 0)
                        continue;

                    // Need special handling for characters because they have an in game skins
                    if (modList.Character is ICharacter { Skins.Count: > 1 } character)
                    {
                        var skinModMap = await _characterSkinService.GetAllModsBySkinAsync(character)
                            .ConfigureAwait(false);
                        if (skinModMap is null)
                            continue;

                        // Don't know what to do with undetectable mods
                        skinModMap.UndetectableMods.ForEach(mod => modList.DisableMod(mod.Id));

                        foreach (var (_, skinMods) in skinModMap.ModsBySkin)
                        {
                            if (skinMods.Count == 0)
                                continue;

                            foreach (var mod in skinMods.Where(mod => modList.IsModEnabled(mod)))
                            {
                                modList.DisableMod(mod.Id);
                            }

                            var randomModIndex = Random.Next(0, skinMods.Count + (allowNoMods ? 1 : 0));

                            if (randomModIndex == skinMods.Count)
                                continue;

                            modList.EnableMod(skinMods.ElementAt(randomModIndex).Id);
                        }

                        continue;
                    }

                    foreach (var characterSkinEntry in mods.Where(characterSkinEntry => characterSkinEntry.IsEnabled))
                    {
                        modList.DisableMod(characterSkinEntry.Id);
                    }

                    var randomIndex = Random.Next(0, mods.Count + (allowNoMods ? 1 : 0));
                    if (randomIndex == mods.Count)
                        continue;

                    modList.EnableMod(mods[randomIndex].Id);
                }
            });
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to randomize mods");
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.RandomizeFailed.Title", defaultValue: "随机模组失败"),
                e.Message,
                TimeSpan.FromSeconds(5));
            return;
        }

        if (_elevatorService.ElevatorStatus == ElevatorStatus.Running)
        {
            await Task.Run(() => _elevatorService.RefreshGenshinMods());
        }

        _notificationManager.ShowNotification(
            _localizer.GetLocalizedStringOrDefault("Notification.ModsRandomized.Title", defaultValue: "模组已随机"),
            string.Format(_localizer.GetLocalizedStringOrDefault("Notification.ModsRandomized.Message",
                    defaultValue: "已对以下分类随机：{0}")!,
                string.Join(", ", selectedCategories.Select(c => c.DisplayNamePlural))),
            TimeSpan.FromSeconds(5));
    }
}
