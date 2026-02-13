using UMManager.WinUI.ViewModels.CharacterManagerViewModels;
using Microsoft.UI.Xaml.Controls;

namespace UMManager.WinUI.Views.CharacterManager;

public sealed partial class CreateCharacterPage : Page
{
    public CreateCharacterViewModel ViewModel { get; } = App.GetService<CreateCharacterViewModel>();

    public CreateCharacterPage()
    {
        this.InitializeComponent();
    }
}