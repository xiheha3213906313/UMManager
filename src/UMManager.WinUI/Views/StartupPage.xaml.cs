﻿using UMManager.WinUI.ViewModels;
using UMManager.WinUI.ViewModels.SubVms;
using UMManager.WinUI.Views.Controls;
using Microsoft.UI.Xaml.Controls;

namespace UMManager.WinUI.Views;

public sealed partial class StartupPage : Page
{
    public StartupViewModel ViewModel { get; }

    public StartupPage()
    {
        ViewModel = App.GetService<StartupViewModel>();
        InitializeComponent();
    }

    private void ModsFolder_OnPathChangedEvent(object? sender, FolderSelector.StringEventArgs e)
        => ViewModel.PathToModsFolderPicker.Validate(e.Value);

    private async void GameSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        await ViewModel.SetGameCommand.ExecuteAsync(((GameComboBoxEntryVM)e.AddedItems[0]!).Value.ToString()).ConfigureAwait(false);
    }
}
