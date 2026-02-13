using System.Diagnostics;
using System.Reflection;
using System.Threading.RateLimiting;
using UMManager.Core.Contracts.Services;
using UMManager.Core.GamesService;
using UMManager.Core.Services;
using UMManager.Core.Services.CommandService;
using UMManager.Core.Services.GameBanana;
using UMManager.Core.Services.ModPresetService;
using UMManager.WinUI.Activation;
using UMManager.WinUI.Configuration;
using UMManager.WinUI.Contracts.Services;
using UMManager.WinUI.Models.Options;
using UMManager.WinUI.Services;
using UMManager.WinUI.Services.AppManagement;
using UMManager.WinUI.Services.AppManagement.Updating;
using UMManager.WinUI.Services.ModExport;
using UMManager.WinUI.Services.ModHandling;
using UMManager.WinUI.Services.Notifications;
using UMManager.WinUI.ViewModels;
using UMManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;
using UMManager.WinUI.ViewModels.CharacterGalleryViewModels;
using UMManager.WinUI.ViewModels.CharacterManagerViewModels;
using UMManager.WinUI.ViewModels.SettingsViewModels;
using UMManager.WinUI.Views;
using UMManager.WinUI.Views.CharacterManager;
using UMManager.WinUI.Views.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Extensions.Http;
using Polly.RateLimiting;
using Polly.Retry;
using Serilog;
using Serilog.Events;
using Serilog.Templates;
using CreateCommandViewModel = UMManager.WinUI.ViewModels.SettingsViewModels.CreateCommandViewModel;
using GameBananaService = UMManager.WinUI.Services.ModHandling.GameBananaService;
using NotificationManager = UMManager.WinUI.Services.Notifications.NotificationManager;

namespace UMManager.WinUI;

// To learn more about WinUI 3, see https://docs.microsoft.com/windows/apps/winui/winui3/.
public partial class App : Application
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    // https://docs.microsoft.com/dotnet/core/extensions/dependency-injection
    // https://docs.microsoft.com/dotnet/core/extensions/configuration
    // https://docs.microsoft.com/dotnet/core/extensions/logging
    public IHost Host { get; }

    public static T GetService<T>()
        where T : class
    {
        if ((Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");

        return service;
    }

    public static DirectoryInfo GetUniqueTmpFolder()
    {
        var tmpFolder = new DirectoryInfo(Path.Combine(TMP_DIR, Guid.NewGuid().ToString()));
        if (!tmpFolder.Exists)
            tmpFolder.Create();
        return tmpFolder;
    }

    public static string TMP_DIR { get; } = Path.Combine(Path.GetTempPath(), "UMManager_TMP");
    public static string ROOT_DIR { get; } = AppDomain.CurrentDomain.BaseDirectory;
    public static string ASSET_DIR { get; } = Path.Combine(ROOT_DIR, "Assets");

    public static WindowEx MainWindow { get; } = new MainWindow();
    public static UIElement? AppTitlebar { get; set; }

    public static bool OverrideShutdown { get; set; }
    public static bool IsShuttingDown { get; set; }
    public static bool ShutdownComplete { get; set; }

    public static bool UnhandledExceptionHandled { get; set; }

    public App()
    {
        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "zh-CN";
        }
        catch
        {
        }

        InitializeComponent();

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .UseSerilog((context, configuration) =>
            {
                configuration.MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning);

                configuration.Filter.ByExcluding(logEvent =>
                    logEvent.Exception is RateLimiterRejectedException);
                configuration.Enrich.FromLogContext();


                configuration.ReadFrom.Configuration(context.Configuration);
                var mt = new ExpressionTemplate(
                    "[{@t:yyyy-MM-dd'T'HH:mm:ss} {@l:u3} {Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)}] {@m}\n{@x}");
                configuration.WriteTo.File(formatter: mt, "logs\\log.txt");
                if (Debugger.IsAttached) configuration.WriteTo.Debug();
            })
            .ConfigureServices((context, services) =>
            {
                // Default Activation Handler
                services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

                // Other Activation Handlers
                services.AddTransient<IActivationHandler, FirstTimeStartupActivationHandler>();

                // Services
                services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
                services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
                services.AddSingleton<INavigationViewService, NavigationViewService>();

                services.AddSingleton<IActivationService, ActivationService>();
                services.AddSingleton<IPageService, PageService>();
                services.AddSingleton<INavigationService, NavigationService>();

                services.AddSingleton<IWindowManagerService, WindowManagerService>();
                services.AddSingleton<NotificationManager>();
                services.AddSingleton<ModNotificationManager>();
                services.AddTransient<ModDragAndDropService>();
                services.AddSingleton<CharacterSkinService>();

                services.AddSingleton<ElevatorService>();

                services.AddSingleton<UpdateChecker>();
                services.AddSingleton<AutoUpdaterService>();

                services.AddSingleton<ImageHandlerService>();
                services.AddSingleton<SelectedGameService>();

                services.AddSingleton<LifeCycleService>();
                services.AddSingleton<BusyService>();
                services.AddSingleton<JsonExporterService>();

                // Core Services
                services.AddSingleton<IFileService, FileService>();
                services.AddSingleton<IGameService, GameService>();
                services.AddSingleton<ISkinManagerService, SkinManagerService>();
                services.AddSingleton<ModCrawlerService>();
                services.AddSingleton<ModSettingsService>();
                services.AddSingleton<KeySwapService>();
                services.AddSingleton<ILanguageLocalizer, Localizer>();
                services.AddSingleton<UserPreferencesService>();
                services.AddSingleton<ArchiveService>();
                services.AddSingleton<ModArchiveRepository>();
                services.AddSingleton<GameBananaCoreService>();
                services.AddSingleton<CommandService>();
                services.AddSingleton<CommandHandlerService>();

                services.AddTransient<HttpLoggerHandler>();
                services.AddSingleton<GameBananaService>();

                services.AddSingleton<ModPresetService>();

                // Even though I've followed the docs, I keep getting "Exception thrown: 'System.IO.IOException' in System.Net.Sockets.dll"
                // I've read just about every microsoft docs page httpclients, and I can't figure out what I'm doing wrong
                // Also tried with httpclientfactory, but that didn't work either

                services.AddHttpClient<IApiGameBananaClient, ApiGameBananaClient>(client =>
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "UMManager-Update-Checker");
                        client.DefaultRequestHeaders.Add("Jasm-Version", $"{Assembly.GetExecutingAssembly().GetName().Version!}");
                        client.DefaultRequestHeaders.Add("Accept", "application/json");
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler()
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
                    }).AddHttpMessageHandler<HttpLoggerHandler>();

                // I'm preeeetty sure this is not correctly set up, not used to polly 8.x.x
                // But it does rate limit, so I guess it's fine for now
                services.AddResiliencePipeline(ApiGameBananaClient.HttpClientName, (builder, context) =>
                {
                    var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions()
                    {
                        QueueProcessingOrder = QueueProcessingOrder.NewestFirst,
                        QueueLimit = 20,
                        TokenLimit = 5,
                        AutoReplenishment = true,
#if DEBUG
                        TokensPerPeriod = 1,
#else
                        TokensPerPeriod = 5,
#endif
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1)
                    });

                    builder
                        .AddRateLimiter(limiter)
                        .AddRetry(new RetryStrategyOptions()
                        {
                            BackoffType = DelayBackoffType.Linear,
                            UseJitter = true,
                            MaxRetryAttempts = 8,
                            Delay = TimeSpan.FromMilliseconds(200)
                        });

                    builder.TelemetryListener = null;
                    context.OnPipelineDisposed(() =>
                    {
                        // This is never called, so I'm not sure if this is correct
                        Log.Debug("Disposing rate limiter");
                        limiter.Dispose();
                    });
                });
                services.AddSingleton<ModUpdateAvailableChecker>();
                services.AddSingleton<ModInstallerService>();


                services.AddHttpClient(Options.DefaultName, (client) =>
                    {
                        client.DefaultRequestHeaders.Add("User-Agent", "UMManager");
                        client.DefaultRequestHeaders.Add("Jasm-Version", $"{Assembly.GetExecutingAssembly().GetName().Version!}");
                        client.DefaultRequestHeaders.Add("Accept", "application/json");
                    })
                    .AddHttpMessageHandler<HttpLoggerHandler>()
                    .AddPolicyHandler(
                        HttpPolicyExtensions.HandleTransientHttpError()
                            .WaitAndRetryAsync(Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromMilliseconds(500), 3, null, true))
                    );

                // Views and ViewModels
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<StartupViewModel>();
                services.AddTransient<StartupPage>();
                services.AddTransient<ShellPage>();
                services.AddTransient<ShellViewModel>();
                services.AddTransient<NotificationsViewModel>();
                services.AddTransient<NotificationsPage>();
                services.AddTransient<CharactersViewModel>();
                services.AddTransient<CharactersPage>();
                services.AddTransient<LayoutEditModeViewModel>();
                services.AddTransient<LayoutEditModePage>();
                services.AddTransient<CharacterEditModeViewModel>();
                services.AddTransient<CharacterEditModePage>();
                services.AddTransient<DebugViewModel>();
                services.AddTransient<DebugPage>();
                services.AddTransient<CharacterManagerViewModel>();
                services.AddTransient<CharacterManagerPage>();
                services.AddTransient<EditCharacterViewModel>();
                services.AddTransient<EditCharacterPage>();
                services.AddTransient<EasterEggVM>();
                services.AddTransient<EasterEggPage>();
                services.AddTransient<ModsOverviewVM>();
                services.AddTransient<ModsOverviewPage>();
                services.AddTransient<ModInstallerVM>();
                services.AddTransient<ModInstallerPage>();
                services.AddTransient<ModSelectorViewModel>();
                services.AddTransient<ModSelector>();
                services.AddTransient<CommandsSettingsViewModel>();
                services.AddTransient<CommandsSettingsPage>();
                services.AddTransient<CharacterGalleryPage>();
                services.AddTransient<CharacterGalleryViewModel>();
                services.AddTransient<CommandProcessViewer>();
                services.AddTransient<CommandProcessViewerViewModel>();
                services.AddTransient<CreateCommandView>();
                services.AddTransient<CreateCommandViewModel>();
                services.AddTransient<ViewModels.CharacterDetailsViewModels.CharacterDetailsViewModel>();
                services.AddTransient<ModPaneVM>();
                services.AddTransient<ModGridVM>();
                services.AddTransient<ContextMenuVM>();
                services.AddTransient<CreateCharacterPage>();
                services.AddTransient<CreateCharacterViewModel>();

                // Configuration
                services.Configure<LocalSettingsOptions>(
                    context.Configuration.GetSection(nameof(LocalSettingsOptions)));
            }).Build();

        UnhandledException += App_UnhandledException;
    }

    // Incremented when an error window is opened, decremented when it is closed
    // Just avoid spamming the user with error windows
    private int _ErrorWindowsOpen = 0;

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var localizer = GetService<ILanguageLocalizer>();
        Log.Error(e.Exception, """

                               --------------------------------------------------------------------
                                    _   _    ____  __  __                                        
                                   | | / \  / ___||  \/  |                                       
                                _  | |/ _ \ \___ \| |\/| |                                       
                               | |_| / ___ \ ___) | |  | |                                       
                                \___/_/   \_\____/|_|  |_| _ _   _ _____ _____ ____  _____ ____  
                               | ____| \ | |/ ___/ _ \| | | | \ | |_   _| ____|  _ \| ____|  _ \ 
                               |  _| |  \| | |  | | | | | | |  \| | | | |  _| | |_) |  _| | | | |
                               | |___| |\  | |__| |_| | |_| | |\  | | | | |___|  _ <| |___| |_| |
                               |_____|_|_\_|\____\___/_\___/|_|_\_| |_| |_____|_| \_\_____|____/ 
                                  / \  | \ | | | | | | \ | | |/ / \ | |/ _ \ \      / / \ | |    
                                 / _ \ |  \| | | | | |  \| | ' /|  \| | | | \ \ /\ / /|  \| |    
                                / ___ \| |\  | | |_| | |\  | . \| |\  | |_| |\ V  V / | |\  |    
                               /_/___\_\_| \_|__\___/|_|_\_|_|\_\_| \_|\___/  \_/\_/  |_| \_|    
                               | ____|  _ \|  _ \ / _ \|  _ \                                    
                               |  _| | |_) | |_) | | | | |_) |                                   
                               | |___|  _ <|  _ <| |_| |  _ <                                    
                               |_____|_| \_\_| \_\\___/|_| \_\                                   
                               --------------------------------------------------------------------
                               """);

        // show error dialog
        var window = new ErrorWindow(e.Exception, () => _ErrorWindowsOpen--)
        {
            IsAlwaysOnTop = true,
            Title = localizer.GetLocalizedStringOrDefault("Window.UnhandledException.Title", defaultValue: "UMManager - 未处理异常"),
            SystemBackdrop = new MicaBackdrop()
        };

        window.Activate();
        _ErrorWindowsOpen++;
        window.CenterOnScreen();

        GetService<NotificationManager>()
            .ShowNotification(
                localizer.GetLocalizedStringOrDefault("Notification.UnhandledException.Title", defaultValue: "发生错误！"),
                localizer.GetLocalizedStringOrDefault("Notification.UnhandledException.Message",
                    defaultValue: "应用可能处于不稳定状态，随时可能崩溃。建议重启应用。"),
                TimeSpan.FromMinutes(60));

        if (_ErrorWindowsOpen > 4)
        {
            // If there are too many error windows open, just close the app
            // This is to prevent the app from spamming the user with error windows
            Environment.Exit(1);
        }
    }


    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        Environment.SetEnvironmentVariable("WEBVIEW2_USE_VISUAL_HOSTING_FOR_OWNED_WINDOWS", "1");
        await GetService<ILanguageLocalizer>().InitializeAsync();
        NotImplemented.NotificationManager = GetService<NotificationManager>();
        base.OnLaunched(args);
        await GetService<IActivationService>().ActivateAsync(args).ConfigureAwait(false);
    }
}
