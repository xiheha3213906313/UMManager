using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UMManager.WinUI.Models.Settings;

namespace UMManager.WinUI.ViewModels.CharacterGalleryViewModels;

public partial class CharacterGalleryViewModel
{
    [ObservableProperty] private bool _isNavPaneVisible;

    private bool CanToggleNavPane()
    {
        return !IsNavigating && !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanToggleNavPane))]
    private async Task ToggleNavPane()
    {
        var settings = await _localSettingsService
            .ReadOrCreateSettingAsync<CharacterGallerySettings>(CharacterGallerySettings.Key);

        settings.IsNavPaneOpen = !IsNavPaneVisible;

        await _localSettingsService.SaveSettingAsync(CharacterGallerySettings.Key, settings);

        IsNavPaneVisible = settings.IsNavPaneOpen;
    }
}