using UMManager.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace UMManager.WinUI.Views;

public sealed partial class LayoutEditModePage : Page
{
    public LayoutEditModeViewModel ViewModel { get; }

    public LayoutEditModePage()
    {
        ViewModel = App.GetService<LayoutEditModeViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }
}
