using CommunityToolkit.Mvvm.ComponentModel;

namespace UMManager.WinUI.ViewModels.CharacterManagerViewModels;

public sealed partial class StringInputField(string value) : InputField<string>(value)
{
    [ObservableProperty] private string _placeHolderText = string.Empty;
}