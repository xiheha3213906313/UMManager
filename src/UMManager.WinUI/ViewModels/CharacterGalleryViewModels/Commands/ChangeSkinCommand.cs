using CommunityToolkit.Mvvm.Input;
using UMManager.Core.GamesService.Interfaces;
using UMManager.Core.Helpers;
using UMManager.WinUI.Models.CustomControlTemplates;

namespace UMManager.WinUI.ViewModels.CharacterGalleryViewModels;

public partial class CharacterGalleryViewModel
{
    private bool CanChangeSkin()
    {
        return !IsNavigating && !IsBusy && CharacterSkins.Count > 0 && _selectedSkin != null &&
               _moddableObject is ICharacter;
    }

    [RelayCommand(CanExecute = nameof(CanChangeSkin))]
    private async Task ChangeSkin(SelectCharacterTemplate characterSkin)
    {
        var character = (ICharacter)_moddableObject!;
        var selectedSkin = character.Skins.FirstOrDefault(sk => sk.InternalNameEquals(characterSkin.InternalName));

        if (selectedSkin is null)
            return;

        _selectedSkin = selectedSkin;
        characterSkin.IsSelected = true;
        CharacterSkins.Where(c => !selectedSkin.InternalNameEquals(c.InternalName)).ForEach(c => c.IsSelected = false);

        OnPropertyChanged(nameof(ModdableObjectImagePath));
        OnPropertyChanged(nameof(ModdableObjectName));

        await ReloadModsAsync();
    }
}