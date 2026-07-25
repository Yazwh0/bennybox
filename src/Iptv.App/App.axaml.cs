using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Iptv.App.ViewModels;
using Iptv.App.Views;
using Iptv.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Iptv.App;

public partial class App : Application
{
    public static IServiceProvider? Services { get; set; }

    private IHost? _host;

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

            desktop.Exit += (_, _) => _host?.Dispose();

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

            splash.SetStatus("Almost there...");

            var mainWindow = host.Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = host.Services.GetRequiredService<MainWindowViewModel>();

            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            splash.Close();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application failed to start");
            splash.Close();
            desktop.Shutdown(-1);
        }
    }
}
