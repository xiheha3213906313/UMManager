using UMManager.WinUI.ViewModels;
using Microsoft.UI.Xaml.Controls;


namespace UMManager.WinUI.Views;

public sealed partial class NotificationsPage : Page
{
    public NotificationsViewModel ViewModel { get; }

    public NotificationsPage()
    {
        ViewModel = App.GetService<NotificationsViewModel>();
        InitializeComponent();
    }
}