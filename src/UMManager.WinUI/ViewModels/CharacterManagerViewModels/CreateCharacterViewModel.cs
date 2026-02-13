using System.Collections.ObjectModel;
using System.Globalization;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.GamesService.Models;
using UMManager.Core.GamesService.Requests;
using UMManager.Core.Helpers;
using UMManager.WinUI.Contracts.Services;
using UMManager.WinUI.Services;
using UMManager.WinUI.Services.Notifications;
using UMManager.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using static UMManager.WinUI.Helpers.Extensions;

namespace UMManager.WinUI.ViewModels.CharacterManagerViewModels;

public partial class CreateCharacterViewModel : ObservableObject
{
    private readonly ISkinManagerService _skinManagerService;
    private readonly IGameService _gameService;
    private readonly NotificationManager _notificationManager;
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();
    private readonly ImageHandlerService _imageHandlerService;
    private readonly INavigationService _navigationService;
    private readonly ILogger _logger;

    private readonly List<IModdableObject> _allModObjects;

    public bool IsFinished { get; private set; }

    public CreateCharacterForm Form { get; } = new();

    [ObservableProperty] private ElementItemVM _selectedElement;
    public ObservableCollection<ElementItemVM> Elements { get; } = new();

    public CreateCharacterViewModel(ISkinManagerService skinManagerService, IGameService gameService, NotificationManager notificationManager,
        ImageHandlerService imageHandlerService, ILogger logger, INavigationService navigationService)
    {
        _skinManagerService = skinManagerService;
        _gameService = gameService;
        _notificationManager = notificationManager;
        _imageHandlerService = imageHandlerService;
        _navigationService = navigationService;
        _logger = logger.ForContext<CreateCharacterViewModel>();

        _allModObjects = _gameService.GetAllModdableObjects(GetOnly.Both);
        var elements = _gameService.GetElements();

        Form.Initialize(_allModObjects, elements);

        Elements.AddRange(elements.Select(e => new ElementItemVM(e.InternalName, e.DisplayName)));

        SelectedElement = Elements.First(e => e.InternalName.Equals("None", StringComparison.OrdinalIgnoreCase));
        Form.Element.Value = SelectedElement.InternalName;

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SelectedElement))
                Form.Element.Value = SelectedElement.InternalName;
        };

        Form.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(CreateCharacterForm.IsValid) or nameof(CreateCharacterForm.AnyFieldDirty))
            {
                SaveCharacterCommand.NotifyCanExecuteChanged();
                ExportCharacterCommand.NotifyCanExecuteChanged();
            }
        };
    }


    [RelayCommand]
    private async Task CopyCharacterToClipboardAsync()
    {
        return;
    }

    private bool CanOpenCustomCharacterJsonFile => File.Exists(_gameService.GameServiceSettingsFilePath);

    [RelayCommand(CanExecute = nameof(CanOpenCustomCharacterJsonFile))]
    private async Task OpenCustomCharacterJsonFile()
    {
        if (!CanOpenCustomCharacterJsonFile)
            return;

        var settingsFile = await StorageFile.GetFileFromPathAsync(_gameService.GameServiceSettingsFilePath);

        await Launcher.LaunchFileAsync(settingsFile);
    }

    private bool CanSaveCharacter() => Form is { IsValid: true, AnyFieldDirty: true } && !IsFinished;

    [RelayCommand(CanExecute = nameof(CanSaveCharacter))]
    private async Task SaveCharacterAsync()
    {
        Form.ValidateAllFields();
        if (!Form.IsValid) return;

        var createCharacterRequest = NewCharacterRequest();

        ICharacter character;
        try
        {
            character = await Task.Run(() => _gameService.CreateCharacterAsync(createCharacterRequest));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to create character");
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.CreateCharacterFailed.Title", defaultValue: "创建角色失败"),
                e.Message,
                null);
            return;
        }

        try
        {
            await Task.Run(() => _skinManagerService.EnableModListAsync(character));
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to enable mod list for character");
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.CreateCharacterEnableModListFailed.Title",
                    defaultValue: "角色已创建，但启用该角色的模组列表失败"),
                e.Message,
                null);
            return;
        }

        IsFinished = true;
        _navigationService.NavigateTo(typeof(CharacterEditModeViewModel).FullName!, character.InternalName);
        _notificationManager.ShowNotification(
            _localizer.GetLocalizedStringOrDefault("Notification.CharacterCreated.Title", defaultValue: "角色已创建"),
            string.Format(CultureInfo.CurrentUICulture,
                _localizer.GetLocalizedStringOrDefault("Notification.CharacterCreated.Message",
                    defaultValue: "角色“{0}”已成功创建")!,
                character.DisplayName),
            null);
    }

    #region ImageCommands

    [RelayCommand]
    private async Task PasteImageAsync()
    {
        try
        {
            var image = await _imageHandlerService.GetImageFromClipboardAsync();
            if (image is not null)
                Form.Image.Value = image;
            else
                _notificationManager.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("Notification.PasteImageFailed.Title", defaultValue: "粘贴图片失败"),
                    _localizer.GetLocalizedStringOrDefault("Notification.PasteImageFailed.NoImage", defaultValue: "剪贴板中没有图片"),
                    null);
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
    private void ClearImage() => Form.Image.Value = ImageHandlerService.StaticPlaceholderImageUri;

    [RelayCommand]
    private async Task SelectImageAsync()
    {
        var image = await _imageHandlerService.PickImageAsync(copyToTmpFolder: false);
        if (image is not null && Uri.TryCreate(image.Path, UriKind.Absolute, out var imagePath))
            Form.Image.Value = imagePath;
    }

    #endregion

    private bool CanExportCharacter() => Form is { IsValid: true, AnyFieldDirty: true };

    [RelayCommand(CanExecute = nameof(CanExportCharacter))]
    private async Task ExportCharacterAsync()
    {
        Form.ValidateAllFields();
        if (!Form.IsValid) return;


        var createCharacterRequest = NewCharacterRequest();

        var json = "";
        ICharacter character;
        try
        {
            var exportResult = await Task.Run(() => _gameService.CreateJsonCharacterExportAsync(createCharacterRequest));
            json = exportResult.json;
            character = exportResult.character;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to create json export");
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.CreateJsonExportFailed.Title", defaultValue: "生成 JSON 导出失败"),
                e.Message,
                null);
            return;
        }


        var content = new ScrollViewer()
        {
            Content = new TextBlock()
            {
                Text = json,
                IsTextSelectionEnabled = true,
                Margin = new Thickness(4)
            }
        };


        var dialogHeight = App.MainWindow.Height * 0.5;
        var dialogWidth = App.MainWindow.Width * 0.7;
        var contentWrapper = new StackPanel()
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
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.CharacterJsonExport.Title", defaultValue: "角色模型 JSON 导出"),
            Content = contentWrapper,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Dialog.CharacterJsonExport.Primary",
                defaultValue: "复制到剪贴板并关闭"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Close", defaultValue: "关闭"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Resources =
            {
                ["ContentDialogMaxWidth"] = 8000,
                ["ContentDialogMaxHeight"] = 4000
            }
        };

        var result = await characterModelDialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
            return;

        DataPackage package = new();
        package.SetText(json);
        Clipboard.SetContent(package);

        _notificationManager.ShowNotification(
            _localizer.GetLocalizedStringOrDefault("Notification.CharacterJsonCopied.Title", defaultValue: "角色 JSON 已复制到剪贴板"),
            "",
            null);

        if (createCharacterRequest.Image is null || !File.Exists(createCharacterRequest.Image.LocalPath))
            return;

        await Task.Run(async () =>
        {
            var imageFolder = App.GetUniqueTmpFolder();

            var imageFilePath = createCharacterRequest.Image.LocalPath;

            var tmpImageFilePath = Path.Combine(imageFolder.FullName, character.InternalName + Path.GetExtension(imageFilePath));

            File.Copy(imageFilePath, tmpImageFilePath, true);

            await Launcher.LaunchFolderAsync(await StorageFolder.GetFolderFromPathAsync(imageFolder.FullName));
        }).ConfigureAwait(false);
    }

    private CreateCharacterRequest NewCharacterRequest()
    {
        var internalName = new InternalName(Form.InternalName.Value);
        var displayName = Form.DisplayName.Value.Trim();
        var releaseDate = Form.ReleaseDate.Value.Date;
        var isMultiMod = Form.IsMultiMod.Value;

        var createCharacterRequest = new CreateCharacterRequest()
        {
            InternalName = internalName,
            DisplayName = displayName.IsNullOrEmpty() ? Form.InternalName.Value.Trim() : displayName,
            Image = Form.Image.Value == ImageHandlerService.StaticPlaceholderImageUri ? null : Form.Image.Value,
            Rarity = Form.Rarity.Value,
            Element = Form.Element.Value,
            Class = null,
            ReleaseDate = releaseDate,
            IsMultiMod = isMultiMod
        };

        return createCharacterRequest;
    }

    public class ElementItemVM(string internalName, string displayText)
    {
        public string InternalName { get; } = internalName;
        public string DisplayText { get; } = displayText;

        public override string ToString() => DisplayText;
    }
}
