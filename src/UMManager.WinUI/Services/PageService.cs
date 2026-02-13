using CommunityToolkit.Mvvm.ComponentModel;
using UMManager.WinUI.Contracts.Services;
using UMManager.WinUI.ViewModels;
using UMManager.WinUI.ViewModels.CharacterGalleryViewModels;
using UMManager.WinUI.ViewModels.CharacterManagerViewModels;
using UMManager.WinUI.ViewModels.SettingsViewModels;
using UMManager.WinUI.Views;
using UMManager.WinUI.Views.CharacterManager;
using UMManager.WinUI.Views.Settings;
using Microsoft.UI.Xaml.Controls;

namespace UMManager.WinUI.Services;

public class PageService : IPageService
{
    private readonly Dictionary<string, Type> _pages = new();

    public PageService()
    {
        Configure<StartupViewModel, StartupPage>();
        Configure<SettingsViewModel, SettingsPage>();
        Configure<NotificationsViewModel, NotificationsPage>();
        Configure<CharactersViewModel, CharactersPage>();
        Configure<LayoutEditModeViewModel, LayoutEditModePage>();
        Configure<CharacterEditModeViewModel, CharacterEditModePage>();
        Configure<ViewModels.CharacterDetailsViewModels.CharacterDetailsViewModel,
            Views.CharacterDetailsPages.CharacterDetailsPage>();
        Configure<DebugViewModel, DebugPage>();
        Configure<CharacterManagerViewModel, CharacterManagerPage>();
        Configure<EditCharacterViewModel, EditCharacterPage>();
        Configure<EasterEggVM, EasterEggPage>();
        Configure<ModsOverviewVM, ModsOverviewPage>();
        Configure<ModInstallerVM, ModInstallerPage>();
        Configure<ModSelectorViewModel, ModSelector>();
        Configure<CharacterGalleryViewModel, CharacterGalleryPage>();
        Configure<CommandsSettingsViewModel, CommandsSettingsPage>();
        Configure<CreateCharacterViewModel, CreateCharacterPage>();
    }

    public Type GetPageType(string key)
    {
        Type? pageType;
        lock (_pages)
        {
            if (!_pages.TryGetValue(key, out pageType))
            {
                throw new ArgumentException($"Page not found: {key}. Did you forget to call PageService.Configure?");
            }
        }

        return pageType;
    }

    private void Configure<VM, V>()
        where VM : ObservableObject
        where V : Page
    {
        lock (_pages)
        {
            var key = typeof(VM).FullName!;
            if (_pages.ContainsKey(key))
            {
                throw new ArgumentException($"The key {key} is already configured in PageService");
            }

            var type = typeof(V);
            if (_pages.ContainsValue(type))
            {
                throw new ArgumentException(
                    $"This type is already configured with key {_pages.First(p => p.Value == type).Key}");
            }

            _pages.Add(key, type);
        }
    }
}
