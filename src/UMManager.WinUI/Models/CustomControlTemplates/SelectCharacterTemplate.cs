using CommunityToolkit.Mvvm.ComponentModel;
using UMManager.Core.GamesService.Interfaces;
using UMManager.WinUI.Models.ViewModels;
using UMManager.WinUI.Services;

namespace UMManager.WinUI.Models.CustomControlTemplates;

public partial class SelectCharacterTemplate : ObservableObject
{
    [ObservableProperty] private string _imagePath = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _internalName = string.Empty;
    [ObservableProperty] bool _isSelected;

    public SelectCharacterTemplate(SkinVM skinVm)
    {
        ImagePath = skinVm.ImageUri.ToString();
        DisplayName = skinVm.DisplayName;
        InternalName = skinVm.InternalName;
    }

    public SelectCharacterTemplate(ICharacterSkin skinVm)
    {
        ImagePath = skinVm.ImageUri?.ToString() ?? ImageHandlerService.StaticPlaceholderImageUri.ToString();
        DisplayName = skinVm.DisplayName;
        InternalName = skinVm.InternalName;
    }

    public SelectCharacterTemplate(string displayName, string internalName, string image)
    {
        DisplayName = displayName;
        InternalName = internalName;
        ImagePath = image;
    }
}