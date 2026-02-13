using UMManager.WinUI.ViewModels;
using UMManager.WinUI.Views.CharacterManager;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace UMManager.WinUI.Views;

public sealed partial class CharacterEditModePage : Page
{
    public CharacterEditModeViewModel ViewModel { get; }

    public CharacterEditModePage()
    {
        ViewModel = App.GetService<CharacterEditModeViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void CharacterList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.SelectedCharacter is null)
            return;

        EditFrame.Navigate(typeof(EditCharacterPage), ViewModel.SelectedCharacter.InternalName);
    }

    private void AddCharacterButton_OnClick(object sender, RoutedEventArgs e)
    {
        EditFrame.Navigate(typeof(CreateCharacterPage));
    }
}
