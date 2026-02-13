﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using UMManager.WinUI.ViewModels.Messages;
using UMManager.Core.Contracts.Entities;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.GamesService.Models;
using UMManager.Core.GamesService.Requests;
using UMManager.Core.Helpers;
using UMManager.WinUI.Contracts.Services;
using UMManager.WinUI.Contracts.ViewModels;
using UMManager.WinUI.Helpers;
using UMManager.WinUI.Services;
using UMManager.WinUI.Services.Notifications;
using UMManager.WinUI.ViewModels.CharacterManagerViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;
using Serilog;

namespace UMManager.WinUI.ViewModels;

public partial class EditCharacterViewModel : ObservableRecipient, INavigationAware
{
    private readonly IGameService _gameService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly ILogger _logger;
    private readonly NotificationManager _notificationManager;
    private readonly ImageHandlerService _imageHandlerService;
    private readonly INavigationService _navigationService;
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    private ICharacter _character = null!;
    private ICharacterModList? _characterModList;

    private static readonly JsonSerializerOptions _skinFolderJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record SkinFolderMetadata
    {
        public string SkinInternalName { get; init; } = string.Empty;
    }


    [ObservableProperty] private Uri _modFolderUri = null!;
    [ObservableProperty] private string _modFolderString = "";
    [ObservableProperty] private string _modsCount;
    [ObservableProperty] private string _enabledModsCount;

    private bool _suppressOptionSync;

    public CharacterStatus CharacterStatus { get; } = new();

    public EditCharacterForm Form { get; } = new();

    public ObservableCollection<ModModel> Mods { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExtraSkins), nameof(CanDeleteActiveSkin), nameof(ActiveSkinDisplayName))]
    private ICharacterSkin? _activeSkin;

    [ObservableProperty] private Uri _activeSkinImageUri = ImageHandlerService.StaticPlaceholderImageUri;

    public bool HasExtraSkins => _character is not null && _character.Skins.Count > 1;

    public bool CanDeleteActiveSkin =>
        ActiveSkin is { IsDefault: false } &&
        ActiveSkin.InternalName.Id.StartsWith("Skin_", StringComparison.OrdinalIgnoreCase);

    public bool CanRenameActiveSkin => ActiveSkin is { IsDefault: false };

    public string ActiveSkinDisplayName => ActiveSkin is { IsDefault: true }
        ? "默认皮肤"
        : ActiveSkin?.DisplayName ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveSkinDisplayName))]
    private string _activeSkinDisplayNameEdit = string.Empty;


    public EditCharacterViewModel(IGameService gameService, ILogger logger, ISkinManagerService skinManagerService,
        ImageHandlerService imageHandlerService, NotificationManager notificationManager, INavigationService navigationService)
    {
        _gameService = gameService;
        _logger = logger.ForContext<EditCharacterViewModel>();
        _skinManagerService = skinManagerService;
        _imageHandlerService = imageHandlerService;
        _notificationManager = notificationManager;
        _navigationService = navigationService;
        Form.PropertyChanged += NotifyAllCommands;
    }


    public void OnNavigatedTo(object parameter)
    {
        Mods.Clear();

        if (parameter is not string internalName)
        {
            _logger.Error($"Invalid parameter type, {parameter}");
            internalName = _gameService.GetCharacters().First().InternalName;
        }

        if (parameter is InternalName id)
            internalName = id;


        var character = _gameService.GetCharacterByIdentifier(internalName, true);

        if (character is null)
        {
            _logger.Error($"Invalid character identifier, {internalName}");
            character = _gameService.GetCharacters().First();
        }

        _character = character;

        if (!_gameService.GetDisabledCharacters().Contains(character))
        {
            CharacterStatus.SetEnabled(true);
            var modList = _skinManagerService.GetCharacterModList(_character);
            _characterModList = modList;
            ModFolderUri = new Uri(modList.AbsModsFolderPath);
            ModFolderString = ModFolderUri.LocalPath;
        }
        else
        {
            CharacterStatus.SetEnabled(false);
            _characterModList = null;
            ModFolderUri = new Uri(_skinManagerService.GetCharacterModsFolderPath(_character, disabledPrefix: true));
            ModFolderString = ModFolderUri.LocalPath;
            ModsCount = "-";
            EnabledModsCount = "-";
        }


        var allModObjects = _gameService.GetAllModdableObjects(GetOnly.Both);
        Form.Initialize(character, allModObjects);

        _suppressOptionSync = true;
        _suppressOptionSync = false;

        SetActiveSkin(_character.Skins.FirstOrDefault(s => s.IsDefault) ?? _character.Skins.FirstOrDefault());
        NotifyAllCommands();
    }

    public void OnNavigatedFrom()
    {
    }

    #region ImageCommands

    [RelayCommand]
    private async Task PickImage()
    {
        var image = await _imageHandlerService.PickImageAsync(copyToTmpFolder: true);

        if (image is null || string.IsNullOrWhiteSpace(image.Path) || !File.Exists(image.Path))
            return;

        var imageUri = new Uri(image.Path);
        if (ActiveSkin is { IsDefault: false })
        {
            try
            {
                await _gameService.SetCharacterSkinImageAsync(_character.InternalName, ActiveSkin.InternalName, imageUri);
                if (ActiveSkin is CharacterSkin internalSkin)
                    internalSkin.ImageUri = imageUri;
                ActiveSkinImageUri = imageUri;
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to set skin image for character {InternalName}", _character.InternalName);
                _notificationManager.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("Notification.CharacterSkinImage.SetFailed.Title",
                        defaultValue: "设置皮肤图片失败"),
                    e.Message,
                    null);
            }
            return;
        }

        Form.Image.Value = imageUri;
        ActiveSkinImageUri = imageUri;
    }

    [RelayCommand]
    private async Task PasteImageAsync()
    {
        try
        {
            var image = await _imageHandlerService.GetImageFromClipboardAsync();
            if (image is null)
            {
                _notificationManager.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("Notification.PasteImageFailed.Title", defaultValue: "粘贴图片失败"),
                    _localizer.GetLocalizedStringOrDefault("Notification.PasteImageFailed.NoImage", defaultValue: "剪贴板中没有图片"),
                    null);
                return;
            }

            if (ActiveSkin is { IsDefault: false })
            {
                if (!image.IsFile || !File.Exists(image.LocalPath))
                {
                    _notificationManager.ShowNotification(
                        _localizer.GetLocalizedStringOrDefault("Notification.CharacterSkinImage.SetFailed.Title",
                            defaultValue: "设置皮肤图片失败"),
                        _localizer.GetLocalizedStringOrDefault("Notification.CharacterSkinImage.InvalidClipboardFile.Message",
                            defaultValue: "剪贴板图片不是有效的本地文件"),
                        null);
                    return;
                }

                await _gameService.SetCharacterSkinImageAsync(_character.InternalName, ActiveSkin.InternalName, image);
                if (ActiveSkin is CharacterSkin internalSkin)
                    internalSkin.ImageUri = image;
                ActiveSkinImageUri = image;
                return;
            }

            Form.Image.Value = image;
            ActiveSkinImageUri = image;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to paste image");
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.PasteImageFailed.Title", defaultValue: "粘贴图片失败"),
                ex.Message,
                null);
        }
    }

    [RelayCommand]
    private void ClearImage()
    {
        if (ActiveSkin is { IsDefault: false })
            return;

        Form.Image.Value = _character.DefaultCharacter.ImageUri ?? ImageHandlerService.StaticPlaceholderImageUri;
        ActiveSkinImageUri = Form.Image.Value;
    }

    [RelayCommand]
    private async Task SelectImageAsync()
    {
        var image = await _imageHandlerService.PickImageAsync(copyToTmpFolder: true);
        if (image is not null && Uri.TryCreate(image.Path, UriKind.Absolute, out var imagePath) && File.Exists(imagePath.LocalPath))
        {
            if (ActiveSkin is { IsDefault: false })
            {
                try
                {
                    await _gameService.SetCharacterSkinImageAsync(_character.InternalName, ActiveSkin.InternalName, imagePath);
                    if (ActiveSkin is CharacterSkin internalSkin)
                        internalSkin.ImageUri = imagePath;
                    ActiveSkinImageUri = imagePath;
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to set skin image for character {InternalName}", _character.InternalName);
                    _notificationManager.ShowNotification(
                        _localizer.GetLocalizedStringOrDefault("Notification.CharacterSkinImage.SetFailed.Title",
                            defaultValue: "设置皮肤图片失败"),
                        e.Message,
                        null);
                }
                return;
            }

            Form.Image.Value = imagePath;
            ActiveSkinImageUri = imagePath;
        }
    }

    #endregion

    [RelayCommand]
    private async Task AddSkinAsync()
    {
        var image = await _imageHandlerService.PickImageAsync(copyToTmpFolder: true);
        if (image is null || string.IsNullOrWhiteSpace(image.Path) || !File.Exists(image.Path))
            return;

        try
        {
            await _gameService.AddCharacterSkinAsync(_character.InternalName, new AddCharacterSkinRequest
            {
                Image = new Uri(image.Path)
            });

            RefreshCharacterReference();
            var newSkin = _character.Skins.LastOrDefault(s => !s.IsDefault && s.ImageUri is not null);
            SetActiveSkin(newSkin);
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.CharacterSkin.Added.Title", defaultValue: "添加皮肤成功"),
                _localizer.GetLocalizedStringOrDefault("Notification.CharacterSkin.Added.Message", defaultValue: "已添加皮肤分类"),
                null);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to add skin for character {InternalName}", _character.InternalName);
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.CharacterSkin.AddFailed.Title", defaultValue: "添加皮肤失败"),
                e.Message,
                null);
        }
    }

    private void RefreshCharacterReference()
    {
        var latest = _gameService.GetCharacterByIdentifier(_character.InternalName, true);
        if (latest is not null)
            _character = latest;

        OnPropertyChanged(nameof(HasExtraSkins));
    }

    private void SetActiveSkin(ICharacterSkin? skin)
    {
        if (skin is null)
            return;

        ActiveSkin = skin;
        ActiveSkinDisplayNameEdit = ActiveSkinDisplayName;
        ActiveSkinImageUri = skin.IsDefault
            ? Form.Image.Value
            : skin.ImageUri ?? ImageHandlerService.StaticPlaceholderImageUri;
        OnPropertyChanged(nameof(HasExtraSkins));
        OnPropertyChanged(nameof(CanRenameActiveSkin));
        SaveActiveSkinNameCommand.NotifyCanExecuteChanged();
        DeleteSkinCommand.NotifyCanExecuteChanged();
        LoadModsForActiveSkin();
    }

    private string? TryGetSkinInternalNameForMod(ISkinMod mod)
    {
        var skinFolder = Directory.GetParent(mod.FullPath);
        if (skinFolder is null)
            return null;

        var metadataPath = Path.Combine(skinFolder.FullName, UMManager.Core.Helpers.Constants.SkinFolderMetadataFileName);
        if (!File.Exists(metadataPath))
            return null;

        try
        {
            var metadata = System.Text.Json.JsonSerializer.Deserialize<SkinFolderMetadata>(File.ReadAllText(metadataPath), _skinFolderJsonOptions);
            return metadata?.SkinInternalName;
        }
        catch
        {
            return null;
        }
    }

    private void LoadModsForActiveSkin()
    {
        Mods.Clear();

        if (_characterModList is null)
            return;

        var mods = _characterModList.Mods.ToArray();
        if (ActiveSkin is not null)
        {
            mods = mods.Where(m =>
            {
                var skinInternalName = TryGetSkinInternalNameForMod(m.Mod);
                if (skinInternalName.IsNullOrEmpty())
                    return ActiveSkin.IsDefault;
                return skinInternalName.Equals(ActiveSkin.InternalName.Id, StringComparison.OrdinalIgnoreCase);
            }).ToArray();
        }

        ModsCount = mods.Length.ToString();
        EnabledModsCount = mods.Count(m => m.IsEnabled).ToString();
        Mods.AddRange(mods.Select(m => ModModel.FromMod(m.Mod)));
    }

    [RelayCommand(CanExecute = nameof(CanRenameActiveSkin))]
    private async Task SaveActiveSkinNameAsync()
    {
        if (ActiveSkin is null || ActiveSkin.IsDefault)
            return;

        var newName = ActiveSkinDisplayNameEdit.Trim();
        if (newName.Length == 0)
            return;

        try
        {
            await _gameService.SetCharacterSkinDisplayNameAsync(_character.InternalName, ActiveSkin.InternalName, newName);
            await _skinManagerService.SyncCharacterSkinFolderNameAsync(_character, ActiveSkin.InternalName, newName);
            RefreshCharacterReference();
            var refreshed = _character.Skins.FirstOrDefault(s => s.InternalNameEquals(ActiveSkin.InternalName));
            if (refreshed is not null)
                SetActiveSkin(refreshed);
            _notificationManager.QueueNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.CharacterSkin.RenameSuccess.Title",
                    defaultValue: "修改皮肤名称成功"));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to set skin display name for character {InternalName}", _character.InternalName);
            var message = e.Message.IsNullOrEmpty() ? e.GetType().Name : e.Message;
            _notificationManager.QueueNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.CharacterSkin.RenameFailed.Title",
                    defaultValue: "修改皮肤名称失败"),
                subtitle: message);
        }
    }

    [RelayCommand]
    private void NextSkin()
    {
        var skins = _character.Skins.ToList();
        if (skins.Count <= 1)
            return;

        var currentIndex = ActiveSkin is null
            ? 0
            : skins.FindIndex(s => s.InternalName.Equals(ActiveSkin.InternalName));

        if (currentIndex < 0)
            currentIndex = 0;

        var next = skins[(currentIndex + 1) % skins.Count];
        SetActiveSkin(next);
    }

    private bool CanDeleteSkin() => CanDeleteActiveSkin;

    [RelayCommand(CanExecute = nameof(CanDeleteSkin))]
    private async Task DeleteSkinAsync()
    {
        if (ActiveSkin is null || ActiveSkin.IsDefault)
            return;

        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.DeleteSkin.Title", defaultValue: "删除皮肤"),
            Content = new TextBlock
            {
                Text = string.Format(CultureInfo.CurrentUICulture,
                    _localizer.GetLocalizedStringOrDefault("Dialog.DeleteSkin.Text", defaultValue: "确定删除皮肤“{0}”吗？")!,
                    ActiveSkin.DisplayName),
                TextWrapping = TextWrapping.WrapWholeWords
            },
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Delete", defaultValue: "删除"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        try
        {
            await _gameService.DeleteCharacterSkinAsync(_character.InternalName, ActiveSkin.InternalName);
            RefreshCharacterReference();
            SetActiveSkin(_character.Skins.FirstOrDefault(s => s.IsDefault) ?? _character.Skins.FirstOrDefault());
            DeleteSkinCommand.NotifyCanExecuteChanged();
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.CharacterSkin.Deleted.Title", defaultValue: "删除皮肤成功"),
                _localizer.GetLocalizedStringOrDefault("Notification.CharacterSkin.Deleted.Message", defaultValue: "已删除皮肤分类"),
                null);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to delete skin for character {InternalName}", _character.InternalName);
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.CharacterSkin.DeleteFailed.Title", defaultValue: "删除皮肤失败"),
                e.Message,
                null);
        }
    }

    private bool CanDisableCharacter() => CharacterStatus.IsEnabled && !AnyChanges();

    [RelayCommand(CanExecute = nameof(CanDisableCharacter))]
    private async Task DisableCharacter()
    {
        var deleteFolderCheckBox = new CheckBox()
        {
            Content = _localizer.GetLocalizedStringOrDefault("Dialog.DisableCharacter.DeleteFolder",
                defaultValue: "删除角色文件夹及其内容/模组？\n文件将被永久删除！"),
            IsChecked = false
        };

        var dialogContent = new StackPanel()
        {
            Children =
            {
                new TextBlock()
                {
                    Text = _localizer.GetLocalizedStringOrDefault("Dialog.DisableCharacter.Text",
                        defaultValue:
                        "确定要禁用该角色吗？这不会删除角色，但 UMManager 将不再识别该角色，之后可重新启用。点击“是”将立即执行。"),
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Margin = new Thickness(0, 0, 0, 8)
                },
                deleteFolderCheckBox
            }
        };


        var disableDialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.DisableCharacter.Title", defaultValue: "禁用角色"),
            Content = dialogContent,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Dialog.DisableCharacter.Primary", defaultValue: "是，禁用该角色"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.No", defaultValue: "否"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var result = await disableDialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
            return;

        _logger.Information(
            $"Disabling character {_character.InternalName} with deleteFolder={deleteFolderCheckBox.IsChecked}");
        var deleteFolder = deleteFolderCheckBox.IsChecked ?? false;
        await Task.Run(async () =>
        {
            await _gameService.DisableCharacterAsync(_character).ConfigureAwait(false);
            await _skinManagerService.DisableModListAsync(_character, deleteFolder).ConfigureAwait(false);
        });
        ResetState();
    }


    private bool CanDeleteCharacter() => !AnyChanges();

    [RelayCommand(CanExecute = nameof(CanDeleteCharacter))]
    private async Task DeleteCharacterAsync()
    {
        var deleteFolderCheckBox = new CheckBox()
        {
            Content = _localizer.GetLocalizedStringOrDefault("Dialog.DeleteCharacter.DeleteFolder",
                defaultValue: "删除自定义角色文件夹及其内容/模组？\n文件将被永久删除！"),
            IsChecked = false
        };

        var dialogContent = new StackPanel()
        {
            Children =
            {
                new TextBlock()
                {
                    Text = _localizer.GetLocalizedStringOrDefault("Dialog.DeleteCharacter.Text",
                        defaultValue:
                        "确定要删除该自定义角色吗？这会移除角色，且 UMManager 将不再识别该角色。点击“是”将立即执行。"),
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Margin = new Thickness(0, 0, 0, 8)
                },
                deleteFolderCheckBox
            }
        };


        var disableDialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.DeleteCharacter.Title", defaultValue: "删除角色"),
            Content = dialogContent,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Dialog.DeleteCharacter.Primary", defaultValue: "是，删除该角色"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.No", defaultValue: "否"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var result = await disableDialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
            return;


        _logger.Information($"Deleting character {_character.InternalName}");
        var deleteFolder = deleteFolderCheckBox.IsChecked ?? false;
        await Task.Run(async () =>
        {
            await _gameService.DeleteCharacterAsync(_character.InternalName).ConfigureAwait(false);
            await _skinManagerService.DisableModListAsync(_character, deleteFolder).ConfigureAwait(false);
        });

        Messenger.Send(new CustomCharacterDeletedMessage(_character.InternalName.Id));
        _notificationManager.ShowNotification(
            _localizer.GetLocalizedStringOrDefault("Notification.CharacterDeleted.Title", defaultValue: "角色已删除"),
            string.Format(_localizer.GetLocalizedStringOrDefault("Notification.CharacterDeleted.Message",
                    defaultValue: "角色“{0}”已成功删除")!,
                _character.DisplayName),
            null);
    }

    private bool CanEnableCharacter() => CharacterStatus.IsDisabled && !AnyChanges();

    [RelayCommand(CanExecute = nameof(CanEnableCharacter))]
    private async Task EnableCharacter()
    {
        var dialogContent = new TextBlock()
        {
            Text = _localizer.GetLocalizedStringOrDefault("Dialog.EnableCharacter.Text",
                defaultValue: "确定要启用该角色吗？点击“是”将立即执行。"),
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var enableDialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.EnableCharacter.Title", defaultValue: "启用角色"),
            Content = dialogContent,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Dialog.EnableCharacter.Primary", defaultValue: "是，启用该角色"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.No", defaultValue: "否"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var result = await enableDialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
            return;

        _logger.Information($"Enabling character {_character.InternalName}");

        await _gameService.EnableCharacterAsync(_character);
        await _skinManagerService.EnableModListAsync(_character);
        ResetState();
    }

    [RelayCommand]
    private void GoToCharacter()
    {
        _navigationService.NavigateToCharacterDetails(_character.InternalName);
    }

    private bool CanSaveChanges() => Form.AnyFieldDirty && Form.IsValid;

    [RelayCommand(CanExecute = nameof(CanSaveChanges))]
    private async Task SaveChangesAsync()
    {
        _logger.Debug($"Saving changes to character {_character.InternalName}");
        Form.ValidateAllFields();

        if (!AnyChanges() || !Form.IsValid)
            return;

        var previousDisplayName = _character.DisplayName;
        try
        {
            await InternalSaveChangesAsync();
            if (Form.DisplayName.IsDirty)
                await _skinManagerService.SyncCharacterFolderNameAsync(_character, previousDisplayName);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to save changes to character");
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.SaveCharacterFailed.Title", defaultValue: "保存角色更改失败"),
                e.Message,
                null);
            return;
        }


        ResetState();

        return;

        async Task InternalSaveChangesAsync()
        {
            if (Form.InternalName.IsDirty)
            {
                Debug.Assert(Form.InternalName.IsValid);
                var oldInternalName = _character.InternalName;
                var newInternalName = new InternalName(Form.InternalName.Value.Trim());
                if (!_character.InternalNameEquals(newInternalName))
                {
                    _character = await Task.Run(() => _gameService.RenameCharacterAsync(oldInternalName, newInternalName)).ConfigureAwait(false);
                    await _skinManagerService.RenameCharacterInternalNameAsync(oldInternalName, newInternalName).ConfigureAwait(false);
                }
            }

            var updateRequest = new UpdateCharacterRequest();

            if (Form.DisplayName.IsDirty)
            {
                Debug.Assert(Form.DisplayName.IsValid);
                updateRequest.DisplayName = NewValue<string>.Set(Form.DisplayName.Value.Trim());
            }

            if (Form.IsMultiMod.IsDirty)
            {
                Debug.Assert(Form.IsMultiMod.IsValid);
                updateRequest.IsMultiMod = NewValue<bool>.Set(Form.IsMultiMod.Value);
            }

            if (Form.Image.IsDirty)
            {
                Debug.Assert(Form.Image.IsValid);
                var image = Form.Image.Value == ImageHandlerService.StaticPlaceholderImageUri ? null : Form.Image.Value;
                updateRequest.Image = NewValue<Uri?>.Set(image);
            }

            if (Form.Rarity.IsDirty)
            {
                Debug.Assert(Form.Rarity.IsValid);
                updateRequest.Rarity = NewValue<int>.Set(Form.Rarity.Value);
            }

            if (updateRequest.AnyValuesSet)
                await Task.Run(() => _gameService.UpdateCharacterAsync(_character.InternalName, updateRequest)).ConfigureAwait(false);
        }
    }


    private bool CanRevertChanges() => AnyChanges();

    [RelayCommand(CanExecute = nameof(CanRevertChanges))]
    private void RevertChanges()
    {
        _logger.Debug($"Reverting draft changes to character {_character.InternalName}");
        ResetState();
    }

    private void ResetState()
    {
        Mods.Clear();
        OnNavigatedTo(_character.InternalName.Id);
        NotifyAllCommands();
    }

    private bool AnyChanges() => Form.AnyFieldDirty;

    private void NotifyAllCommands(object? sender = null, PropertyChangedEventArgs? propertyChangedEventArgs = null)
    {
        DisableCharacterCommand.NotifyCanExecuteChanged();
        EnableCharacterCommand.NotifyCanExecuteChanged();
        SaveChangesCommand.NotifyCanExecuteChanged();
        RevertChangesCommand.NotifyCanExecuteChanged();
        DeleteCharacterCommand.NotifyCanExecuteChanged();
    }


    [RelayCommand]
    private async Task ShowCharacterModelAsync()
    {
        var json = JsonConvert.SerializeObject(_character, Formatting.Indented);


        var content = new ScrollViewer()
        {
            Content = new TextBlock()
            {
                Text = json,
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true,
                Margin = new Thickness(4)
            }
        };


        var dialogHeight = App.MainWindow.Height * 0.5;
        var dialogWidth = App.MainWindow.Width * 0.7;
        var contentWrapper = new Grid()
        {
            MinHeight = dialogHeight,
            MinWidth = dialogWidth,
            Children =
            {
                content
            }
        };

        var characterModelDialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.CharacterModel.Title", defaultValue: "角色模型"),
            Content = contentWrapper,
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Close", defaultValue: "关闭"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Resources =
            {
                ["ContentDialogMaxWidth"] = 8000,
                ["ContentDialogMaxHeight"] = 4000
            }
        };

        await characterModelDialog.ShowAsync();
    }
}

public class CharacterStatus : ObservableObject
{
    private bool _isEnabled;

    public bool IsEnabled => _isEnabled;

    private bool _isDisabled;

    public bool IsDisabled => _isDisabled;

    public void SetEnabled(bool enabled)
    {
        SetProperty(ref _isEnabled, enabled, nameof(IsEnabled));
        SetProperty(ref _isDisabled, !enabled, nameof(IsDisabled));
    }
}

public sealed class NameableOptionVm(string internalName, string displayText)
{
    public string InternalName { get; } = internalName;
    public string DisplayText { get; } = displayText;

    public override string ToString() => DisplayText;
}
