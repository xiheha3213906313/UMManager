using System.Globalization;
using System.Collections.ObjectModel;
using Windows.Storage.Pickers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.GamesService.Models;
using UMManager.Core.GamesService.Requests;
using UMManager.Core.Helpers;
using UMManager.WinUI.Contracts.ViewModels;
using UMManager.WinUI.Services.Notifications;
using UMManager.WinUI.ViewModels.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UMManager.WinUI.ViewModels;

public sealed partial class LayoutEditModeViewModel(
    IGameService gameService,
    NotificationManager notificationManager)
    : ObservableRecipient, INavigationAware
{
    private readonly IGameService _gameService = gameService;
    private readonly NotificationManager _notificationManager = notificationManager;
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public ObservableCollection<UiCategoryVm> Categories { get; } = new();
    public ObservableCollection<CharacterTagVm> AllCharacters { get; } = new();
    public ObservableCollection<CharacterTagVm> VisibleCharacters { get; } = new();
    public ObservableCollection<CharacterTagVm> AvailableCharacters { get; } = new();

    [ObservableProperty] private UiCategoryVm? _selectedCategory;
    [ObservableProperty] private CharacterTagVm? _selectedCharacter;

    public bool CanEditSelectedCategory => SelectedCategory is not null && !SelectedCategory.IsAll;
    public string HideButtonText => SelectedCategory?.IsHidden == true
        ? _localizer.GetLocalizedStringOrDefault("Common.Button.Show", defaultValue: "显示")!
        : _localizer.GetLocalizedStringOrDefault("Common.Button.Hide", defaultValue: "隐藏")!;
    public bool CanHideSelectedCategory => SelectedCategory is not null;
    public double SelectedCategoryCharactersOpacity => SelectedCategory?.IsHidden == true ? 0.45 : 1.0;
    public bool IsSelectedCategoryHidden => SelectedCategory?.IsHidden == true;

    public async void OnNavigatedTo(object parameter)
    {
        await LoadAsync();
    }

    public void OnNavigatedFrom()
    {
    }

    partial void OnSelectedCategoryChanged(UiCategoryVm? value)
    {
        RefreshVisibleCharacters();
        RefreshAvailableCharacters();
        OnPropertyChanged(nameof(CanEditSelectedCategory));
        OnPropertyChanged(nameof(CanHideSelectedCategory));
        OnPropertyChanged(nameof(HideButtonText));
        OnPropertyChanged(nameof(SelectedCategoryCharactersOpacity));
        OnPropertyChanged(nameof(IsSelectedCategoryHidden));
        DeleteSelectedCategoryCommand.NotifyCanExecuteChanged();
        EditSelectedCategoryCommand.NotifyCanExecuteChanged();
        HideSelectedCategoryCommand.NotifyCanExecuteChanged();
        BulkRemoveCharactersFromSelectedCategoryCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadAsync()
    {
        Categories.Clear();
        AllCharacters.Clear();
        VisibleCharacters.Clear();
        AvailableCharacters.Clear();

        var categories = await _gameService.GetUiCategoriesAsync();
        var allVm = UiCategoryVm.CreateAll();
        var storedAll = categories.FirstOrDefault(c => c.Id == Guid.Empty);
        if (storedAll is not null)
            allVm.IsHidden = storedAll.IsHidden;
        Categories.Add(allVm);

        foreach (var category in categories.Where(c => c.Id != Guid.Empty))
            Categories.Add(UiCategoryVm.FromCategory(category));

        var characters = _gameService.GetCharacters().Concat(_gameService.GetDisabledCharacters()).ToList();
        foreach (var character in characters)
        {
            var tags = await _gameService.GetCharacterTagsAsync(character.InternalName);
            AllCharacters.Add(new CharacterTagVm(character, tags));
        }

        SelectCategory(Categories.FirstOrDefault()!);
        RefreshCategoryCounts();
        RefreshVisibleCharacters();
        RefreshAvailableCharacters();
    }

    private void RefreshVisibleCharacters()
    {
        VisibleCharacters.Clear();
        if (SelectedCategory is null)
            return;

        if (SelectedCategory.IsAll)
        {
            foreach (var character in AllCharacters)
                VisibleCharacters.Add(character);
            return;
        }

        foreach (var character in AllCharacters.Where(c => c.TagIds.Contains(SelectedCategory.Id)))
            VisibleCharacters.Add(character);
    }

    private void RefreshAvailableCharacters()
    {
        AvailableCharacters.Clear();
        if (SelectedCategory is null || SelectedCategory.IsAll)
            return;

        foreach (var character in AllCharacters.Where(c => !c.TagIds.Contains(SelectedCategory.Id)))
            AvailableCharacters.Add(character);
    }

    private void RefreshCategoryCounts()
    {
        foreach (var category in Categories)
        {
            var count = category.IsAll ? AllCharacters.Count : AllCharacters.Count(c => c.TagIds.Contains(category.Id));
            category.CharacterCount = count.ToString();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private async Task CreateCategoryAsync()
    {
        var xamlRoot = App.MainWindow.Content.XamlRoot;
        var nameBox = new TextBox
        {
            Header = _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Name.Header", defaultValue: "分类名称"),
            PlaceholderText = _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Name.Placeholder", defaultValue: "请输入分类名称")
        };

        var imagePathBlock = new TextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords,
            Text = _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Image.NotSelected", defaultValue: "未选择图片")
        };

        string? imagePath = null;

        var pickImageButton = new Button
        {
            Content = _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Image.Pick", defaultValue: "选择图片")
        };

        pickImageButton.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker();
            picker.SettingsIdentifier = "CategoryImagePicker";
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            imagePath = file.Path;
            imagePathBlock.Text = file.Path;
        };

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                nameBox,
                pickImageButton,
                imagePathBlock
            }
        };

        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Create.Title", defaultValue: "创建分类"),
            XamlRoot = xamlRoot,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Create", defaultValue: "创建"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
            DefaultButton = ContentDialogButton.Primary,
            Content = content
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        var name = nameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.CreateFailed.Title", defaultValue: "创建分类失败"),
                _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.NameEmpty.Message", defaultValue: "分类名称不能为空。"),
                TimeSpan.FromSeconds(3));
            return;
        }

        try
        {
            var created = await _gameService.CreateUiCategoryAsync(name, imagePath.IsNullOrEmpty() ? null : new Uri(imagePath));
            await LoadAsync();
            SelectCategoryById(created.Id);
            Messenger.Send(new UiCategoriesChangedMessage(this));
            return;
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.CreateFailed.Title", defaultValue: "创建分类失败"),
                e.Message,
                TimeSpan.FromSeconds(5));
        }
    }

    private bool CanDeleteSelectedCategory() => CanEditSelectedCategory;

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedCategory))]
    private async Task DeleteSelectedCategoryAsync()
    {
        if (SelectedCategory is null)
            return;

        var xamlRoot = App.MainWindow.Content.XamlRoot;
        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Delete.Title", defaultValue: "删除分类"),
            Content = string.Format(CultureInfo.CurrentUICulture,
                _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Delete.Text",
                    defaultValue: "确定要删除分类“{0}”吗？删除后将同时删除其图片，并从所有角色中移除该分类标签。")!,
                SelectedCategory.Name),
            XamlRoot = xamlRoot,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Delete", defaultValue: "删除"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        await _gameService.DeleteUiCategoryAsync(SelectedCategory.Id);
        await LoadAsync();
        Messenger.Send(new UiCategoriesChangedMessage(this));
    }

    private bool CanEditCategory(UiCategoryVm? category) => category is not null && !category.IsAll;

    [RelayCommand(CanExecute = nameof(CanEditCategory))]
    private async Task EditCategoryAsync(UiCategoryVm category)
    {
        SelectCategory(category);

        var xamlRoot = App.MainWindow.Content.XamlRoot;
        var nameBox = new TextBox
        {
            Header = _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Name.Header", defaultValue: "分类名称"),
            PlaceholderText = _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Name.Placeholder", defaultValue: "请输入分类名称"),
            Text = category.Name
        };

        var imagePathBlock = new TextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords,
            Text = category.ImageUri?.LocalPath ??
                   _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Image.NotSelected", defaultValue: "未选择图片")
        };

        Uri? newImage = null;
        var imageIsSet = false;

        var pickImageButton = new Button
        {
            Content = _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Image.Pick", defaultValue: "选择图片")
        };

        pickImageButton.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker();
            picker.SettingsIdentifier = "CategoryImagePicker";
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            imageIsSet = true;
            newImage = new Uri(file.Path);
            imagePathBlock.Text = file.Path;
        };

        var clearImageButton = new Button
        {
            Content = _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Image.Remove", defaultValue: "移除图片"),
            IsEnabled = category.ImageUri is not null
        };

        clearImageButton.Click += (_, _) =>
        {
            imageIsSet = true;
            newImage = null;
            imagePathBlock.Text = _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Image.NotSelected", defaultValue: "未选择图片");
        };

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                nameBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        pickImageButton,
                        clearImageButton
                    }
                },
                imagePathBlock
            }
        };

        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.Edit.Title", defaultValue: "修改分类"),
            XamlRoot = xamlRoot,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Save", defaultValue: "保存"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
            DefaultButton = ContentDialogButton.Primary,
            Content = content
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        var name = nameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.EditFailed.Title", defaultValue: "修改分类失败"),
                _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.NameEmpty.Message", defaultValue: "分类名称不能为空。"),
                TimeSpan.FromSeconds(3));
            return;
        }

        try
        {
            var request = new UpdateUiCategoryRequest
            {
                Name = NewValue<string>.Set(name)
            };

            if (imageIsSet)
                request.Image = NewValue<Uri?>.Set(newImage);

            await _gameService.UpdateUiCategoryAsync(category.Id, request);
            await LoadAsync();
            SelectCategoryById(category.Id);
            Messenger.Send(new UiCategoriesChangedMessage(this));
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.EditFailed.Title", defaultValue: "修改分类失败"),
                e.Message,
                TimeSpan.FromSeconds(5));
        }
    }

    private bool CanEditSelectedCategoryAction() => CanEditSelectedCategory;
    private bool CanHideSelectedCategoryAction() => CanHideSelectedCategory;

    [RelayCommand(CanExecute = nameof(CanEditSelectedCategoryAction))]
    private Task EditSelectedCategoryAsync()
    {
        if (SelectedCategory is null)
            return Task.CompletedTask;

        return EditCategoryAsync(SelectedCategory);
    }

    [RelayCommand(CanExecute = nameof(CanHideSelectedCategoryAction))]
    private async Task HideSelectedCategoryAsync()
    {
        if (SelectedCategory is null)
            return;

        var request = new UpdateUiCategoryRequest
        {
            IsHidden = NewValue<bool>.Set(!SelectedCategory.IsHidden)
        };

        try
        {
            await _gameService.UpdateUiCategoryAsync(SelectedCategory.Id, request);
            var categoryId = SelectedCategory.Id;
            await LoadAsync();
            SelectCategoryById(categoryId);
            Messenger.Send(new UiCategoriesChangedMessage(this));
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.HideFailed.Title", defaultValue: "隐藏分类失败"),
                e.Message,
                TimeSpan.FromSeconds(5));
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedCategoryAction))]
    private async Task BulkRemoveCharactersFromSelectedCategoryAsync()
    {
        if (SelectedCategory is null || SelectedCategory.IsAll)
            return;

        var categoryId = SelectedCategory.Id;
        var affected = AllCharacters.Where(c => c.TagIds.Contains(categoryId)).ToList();
        if (affected.Count == 0)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.BulkRemove.Title", defaultValue: "批量移除"),
                _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.BulkRemove.None.Message",
                    defaultValue: "当前分类没有可移除的角色。"),
                TimeSpan.FromSeconds(3));
            return;
        }

        var xamlRoot = App.MainWindow.Content.XamlRoot;
        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.BulkRemove.Title", defaultValue: "批量移除"),
            Content = string.Format(CultureInfo.CurrentUICulture,
                _localizer.GetLocalizedStringOrDefault("Dialog.UiCategory.BulkRemove.Text",
                    defaultValue: "确定要从分类“{0}”中移除 {1} 个角色吗？")!,
                SelectedCategory.Name,
                affected.Count),
            XamlRoot = xamlRoot,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Remove", defaultValue: "移除"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common.Button.Cancel", defaultValue: "取消"),
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        var failed = new List<string>();
        foreach (var character in affected)
        {
            var original = character.TagIds.ToArray();
            if (character.TagIds.Contains(categoryId))
                character.TagIds.Remove(categoryId);

            try
            {
                await _gameService.SetCharacterTagsAsync(character.Character.InternalName, character.TagIds);
                Messenger.Send(new CharacterTagsChangedMessage(this, character.Character.InternalName.Id, character.TagIds.ToArray()));
            }
            catch (Exception e)
            {
                character.TagIds.Clear();
                foreach (var id in original)
                    character.TagIds.Add(id);
                failed.Add(character.DisplayName);
                _notificationManager.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.BulkRemoveFailed.Title",
                        defaultValue: "批量移除失败"),
                    e.Message,
                    TimeSpan.FromSeconds(5));
                break;
            }
        }

        RefreshCategoryCounts();
        RefreshVisibleCharacters();
        RefreshAvailableCharacters();

        if (failed.Count == 0)
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.BulkRemoveCompleted.Title",
                    defaultValue: "批量移除完成"),
                string.Format(CultureInfo.CurrentUICulture,
                    _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.BulkRemoveCompleted.Message",
                        defaultValue: "已移除 {0} 个角色。")!,
                    affected.Count),
                TimeSpan.FromSeconds(3));
    }

    private bool CanTagCharacter(CharacterTagVm? _) => CanEditSelectedCategory;

    [RelayCommand(CanExecute = nameof(CanTagCharacter))]
    private async Task AddCharacterToSelectedCategoryAsync(CharacterTagVm character)
    {
        if (SelectedCategory is null)
            return;

        try
        {
            await SetCharacterTagAsync(character, SelectedCategory.Id, true);
            RefreshCategoryCounts();
            RefreshVisibleCharacters();
            RefreshAvailableCharacters();
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.AddCharacterFailed.Title",
                    defaultValue: "添加失败"),
                e.Message,
                TimeSpan.FromSeconds(5));
        }
    }

    [RelayCommand(CanExecute = nameof(CanTagCharacter))]
    private async Task RemoveCharacterFromSelectedCategoryAsync(CharacterTagVm character)
    {
        if (SelectedCategory is null)
            return;

        try
        {
            await SetCharacterTagAsync(character, SelectedCategory.Id, false);
            RefreshCategoryCounts();
            RefreshVisibleCharacters();
            RefreshAvailableCharacters();
        }
        catch (Exception e)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("Notification.UiCategory.RemoveCharacterFailed.Title",
                    defaultValue: "移除失败"),
                e.Message,
                TimeSpan.FromSeconds(5));
        }
    }

    public async Task SetCharacterTagAsync(CharacterTagVm character, Guid categoryId, bool isTagged)
    {
        var original = character.TagIds.ToArray();
        if (isTagged)
        {
            if (!character.TagIds.Contains(categoryId))
                character.TagIds.Add(categoryId);
        }
        else
        {
            if (character.TagIds.Contains(categoryId))
                character.TagIds.Remove(categoryId);
        }

        try
        {
            await _gameService.SetCharacterTagsAsync(character.Character.InternalName, character.TagIds);
        }
        catch
        {
            character.TagIds.Clear();
            foreach (var id in original)
                character.TagIds.Add(id);
            throw;
        }

        Messenger.Send(new CharacterTagsChangedMessage(this, character.Character.InternalName.Id, character.TagIds.ToArray()));
        RefreshCategoryCounts();
        RefreshVisibleCharacters();
        RefreshAvailableCharacters();
    }

    [RelayCommand]
    private void SelectCategory(UiCategoryVm category)
    {
        foreach (var c in Categories)
            c.IsSelected = false;

        category.IsSelected = true;
        SelectedCategory = category;
    }

    private void SelectCategoryById(Guid categoryId)
    {
        var category = Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category is null)
            return;

        SelectCategory(category);
    }
}

public sealed partial class UiCategoryVm : ObservableObject
{
    public Guid Id { get; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private Uri? _imageUri;
    [ObservableProperty] private int _order;
    [ObservableProperty] private string _characterCount = "0";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isHidden;

    public bool IsAll => Id == Guid.Empty;
    public bool PreferImage => !IsAll && ImageUri is not null;
    public bool PreferText => IsAll || ImageUri is null;

    private UiCategoryVm(Guid id)
    {
        Id = id;
    }

    partial void OnImageUriChanged(Uri? value)
    {
        OnPropertyChanged(nameof(PreferImage));
        OnPropertyChanged(nameof(PreferText));
    }

    public static UiCategoryVm CreateAll()
    {
        return new UiCategoryVm(Guid.Empty)
        {
            Name = "全部",
            Order = int.MinValue
        };
    }

    public static UiCategoryVm FromCategory(UiCategory category)
    {
        return new UiCategoryVm(category.Id)
        {
            Name = category.Name,
            ImageUri = category.ImageUri,
            Order = category.Order,
            IsHidden = category.IsHidden
        };
    }
}

public sealed class CharacterTagVm
{
    public ICharacter Character { get; }
    public ObservableCollection<Guid> TagIds { get; }

    public string DisplayName => Character.DisplayName;
    public Uri? ImageUri => Character.ImageUri;

    public CharacterTagVm(ICharacter character, IEnumerable<Guid> tagIds)
    {
        Character = character;
        TagIds = new ObservableCollection<Guid>(tagIds.Distinct());
    }

    public override string ToString()
    {
        return DisplayName;
    }
}
