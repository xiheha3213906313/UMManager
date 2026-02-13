using System;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using UMManager.Core.Contracts.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace UMManager.WinUI.Views.Controls;

public sealed partial class LinkButton : UserControl
{
    public LinkButton()
    {
        InitializeComponent();
    }


    public static readonly DependencyProperty LinkProperty = DependencyProperty.Register(
        nameof(Link), typeof(Uri), typeof(LinkButton), new PropertyMetadata(default(Uri)));

    public Uri Link
    {
        get { return (Uri)GetValue(LinkProperty); }
        set { SetValue(LinkProperty, value); }
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(LinkButton), new PropertyMetadata(default(string)));

    public string Text
    {
        get { return (string)GetValue(TextProperty); }
        set { SetValue(TextProperty, value); }
    }

    public static readonly DependencyProperty TextStyleProperty = DependencyProperty.Register(
        nameof(TextStyle), typeof(Style), typeof(LinkButton), new PropertyMetadata(default(Style)));

    public Style TextStyle
    {
        get { return (Style)GetValue(TextStyleProperty); }
        set { SetValue(TextStyleProperty, value); }
    }

    private async void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        if (Link is null) return;

        try
        {
            if (Link.IsFile)
            {
                if (!Directory.Exists(Link.LocalPath) && !File.Exists(Link.LocalPath))
                {
                    await ShowLaunchFailedDialogAsync("路径不存在", Link.LocalPath);
                    return;
                }

                var result = await Launcher.LaunchFolderPathAsync(Link.LocalPath);
                if (!result)
                {
                    var localizer = App.GetService<ILanguageLocalizer>();
                    await ShowLaunchFailedDialogAsync(
                        localizer.GetLocalizedStringOrDefault("Dialog.LaunchFailed.Path.Title", defaultValue: "无法打开路径"),
                        Link.LocalPath);
                }
            }
            else
            {
                var result = await Launcher.LaunchUriAsync(Link);
                if (!result)
                {
                    var localizer = App.GetService<ILanguageLocalizer>();
                    await ShowLaunchFailedDialogAsync(
                        localizer.GetLocalizedStringOrDefault("Dialog.LaunchFailed.Uri.Title", defaultValue: "无法打开链接"),
                        Link.ToString());
                }
            }
        }
        catch (Exception ex)
        {
            var localizer = App.GetService<ILanguageLocalizer>();
            await ShowLaunchFailedDialogAsync(
                localizer.GetLocalizedStringOrDefault("Dialog.LaunchFailed.Generic.Title", defaultValue: "无法打开"),
                ex.Message);
        }
    }

    private async Task ShowLaunchFailedDialogAsync(string title, string message)
    {
        var localizer = App.GetService<ILanguageLocalizer>();
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.WrapWholeWords },
            CloseButtonText = localizer.GetLocalizedStringOrDefault("Common.Button.Close", defaultValue: "关闭"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }

    private void MenuFlyoutItem_CopyLink(object sender, RoutedEventArgs e)
    {
        if (Link is null) return;
        var dataPackage = new DataPackage();
        var linkText = Link.Scheme == Uri.UriSchemeFile ? Link.LocalPath : Link.ToString();
        dataPackage.SetText(linkText);
        Clipboard.SetContent(dataPackage);
    }
}
