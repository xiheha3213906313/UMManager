using CommunityToolkit.Mvvm.ComponentModel;
using UMManager.WinUI.Contracts.ViewModels;

namespace UMManager.WinUI.ViewModels;

public partial class DebugViewModel() : ObservableRecipient, INavigationAware
{
    public static bool UseNewModel = true;

    public void OnNavigatedTo(object parameter)
    {
    }

    public void OnNavigatedFrom()
    {
    }
}