using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using BitMagic.BennyBox.ViewModels;
using BitMagic.BennyBox.Views;
using BitMagic.BennyBox.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
#if DEBUG
using BitMagic.BennyBox.Debug;
#endif

namespace BitMagic.BennyBox;

public partial class App : Application
{
    public static IServiceProvider? Services { get; set; }

    private IHost? _host;
#if DEBUG
    private DebugRemoteControlServer? _debugRemoteControlServer;
#endif

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Show something on screen before doing any real work - DI/host construction, DB
            // migrations, and the settings lookup are I/O and CPU work that used to run before any
            // window existed, which is exactly what made the process look "Not Responding" at launch.
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            splash.Show();

            desktop.Exit += (_, _) =>
            {
                _host?.Dispose();
#if DEBUG
                _debugRemoteControlServer?.Dispose();
#endif
            };

            _ = LoadAndShowMainWindowAsync(desktop, splash);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task LoadAndShowMainWindowAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
    {
        try
        {
            splash.SetStatus("Starting player engine and loading your library...");

            var libVlcTask = Program.LibVlcTask
                ?? throw new InvalidOperationException("LibVLC initialization was not started.");

            var host = await Task.Run(() => AppBootstrapper.BuildHost(Program.StartupArgs, libVlcTask));
            _host = host;
            Services = host.Services;

            var settingsStore = host.Services.GetRequiredService<ISettingsStore>();
            var savedTheme = await Task.Run(() => settingsStore.GetAsync("Theme").GetAwaiter().GetResult());
            RequestedThemeVariant = savedTheme switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };

#if DEBUG
            _ = Task.Run(() => AppBootstrapper.SeedDevXtreamProfileAsync(host.Services));
#endif

            // Any download still Queued/Downloading belonged to a copy loop from a previous session
            // that no longer exists - mark it Failed so it shows up as retryable rather than stuck
            // "in progress" forever. See DownloadManager.ReconcileInterruptedDownloadsAsync.
            var downloadManager = host.Services.GetRequiredService<DownloadManager>();
            await Task.Run(() => downloadManager.ReconcileInterruptedDownloadsAsync());

            splash.SetStatus("Almost there...");

            var mainWindow = host.Services.GetRequiredService<MainWindow>();
            var mainWindowViewModel = host.Services.GetRequiredService<MainWindowViewModel>();
            mainWindow.DataContext = mainWindowViewModel;

            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            splash.Close();

#if DEBUG
            _debugRemoteControlServer = new DebugRemoteControlServer(mainWindowViewModel, mainWindow, host.Services.GetRequiredService<ILogger<DebugRemoteControlServer>>());
            _debugRemoteControlServer.Start();
#endif
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application failed to start");
            splash.Close();
            desktop.Shutdown(-1);
        }
    }
}
