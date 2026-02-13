using Windows.Storage.Pickers;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;
using UMManager.Core.GamesService.Models;
using UMManager.Core.GamesService.Requests;
using UMManager.Core.Helpers;
using UMManager.Core.Services.CommandService;
using UMManager.WinUI.Services.AppManagement;
using UMManager.WinUI.Services.ModExport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

//using NativeFileDialogs.Net;

namespace UMManager.WinUI.Views;

public sealed partial class DebugPage : Page
{
    public IGameService GameService = App.GetService<IGameService>();
    public CommandService CommandService = App.GetService<CommandService>();

    public IWindowManagerService WindowManagerService = App.GetService<IWindowManagerService>();

    public JsonExporterService JsonExporterService = App.GetService<JsonExporterService>();

    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();

    public DebugPage()
    {
        InitializeComponent();
    }


    private async void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        var createCharacterRequest = new CreateCharacterRequest()
        {
            DisplayName = "DebugTest",
            Element = "Pyro",
            Rarity = 5,
            InternalName = new InternalName("DebugTest"),
            IsMultiMod = false,
            Region = new[] { "Mondstadt" },
            Keys = new[] { "DebugTest", "Debugger" }
        };

        var filePicker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(filePicker, hwnd);

        Constants.SupportedImageExtensions.ForEach(x => filePicker.FileTypeFilter.Add(x));

        var file = await filePicker.PickSingleFileAsync();

        if (file != null)
        {
            createCharacterRequest.Image = new Uri(file.Path);
        }

        var newCharacter = await GameService.CreateCharacterAsync(createCharacterRequest);

        await _skinManagerService.EnableModListAsync(newCharacter);
    }

    private async void ButtonBase_OnClick1(object sender, RoutedEventArgs e)
    {
        var character = GameService.GetCharacterByIdentifier("DebugTest");

        character ??= GameService.GetCharacterByIdentifier("NewName");

        var editCharacterRequest = new UpdateCharacterRequest()
        {
            DisplayName = NewValue<string>.Set(character.DisplayName == "DebugTest" ? "NewName" : "DebugTest")
        };

        await GameService.UpdateCharacterAsync(character.InternalName, editCharacterRequest);
    }

    private async void ButtonBase_OnClick2(object sender, RoutedEventArgs e)
    {
        var character = GameService.GetCharacterByIdentifier("DebugTest");

        character ??= GameService.GetCharacterByIdentifier("NewName");

        await GameService.DeleteCharacterAsync(character.InternalName);
        await _skinManagerService.DisableModListAsync(character);
    }
}
