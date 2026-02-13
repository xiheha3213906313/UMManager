using Microsoft.UI.Xaml.Controls;

namespace UMManager.AutoUpdater;

public sealed partial class MainPage : Page
{
    public MainPageVM ViewModel { get; }

    public MainPage(string currentJasmVersion)
    {
        InitializeComponent();

        ViewModel = new MainPageVM(currentJasmVersion);
    }
}
