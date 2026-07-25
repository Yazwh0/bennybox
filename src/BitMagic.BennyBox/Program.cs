using System;
using System.IO;
using Avalonia;
using LibVLCSharp.Shared;
using Serilog;

namespace BitMagic.BennyBox;

internal static class Program
{
    // Set once at the top of Main, read from App.OnFrameworkInitializationCompleted once the splash
    // window is already showing. Kept as statics rather than threaded through Avalonia's startup APIs
    // because AppBuilder/BuildAvaloniaApp() has no constructor-injection story of its own.
    internal static string[] StartupArgs { get; private set; } = [];
    internal static Task<LibVLC>? LibVlcTask { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        MigrateAppDataFolder();

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BennyBox", "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(logDirectory, "iptv-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (IsTerminating={IsTerminating})", e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        try
        {
            LibVLCSharp.Shared.Core.Initialize();

            // Native LibVLC init (plugin discovery, etc.) can take a noticeable moment - start it on a
            // background thread immediately so it overlaps with everything else instead of blocking
            // startup later when PlayerViewModel first needs it.
            LibVlcTask = Task.Run(() => new LibVLC());
            StartupArgs = args;

            // Deliberately nothing else happens here: DI/host construction, DB migrations, and the
            // settings lookup are real I/O and CPU work, and doing them here would run before any
            // window exists - which is exactly what made the process look "Not Responding" at launch.
            // That work now happens in App.OnFrameworkInitializationCompleted, on a background thread,
            // after a splash window is already visible. See AppBootstrapper.
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // One-time migration for the "IPTV Player" -> "Benny Box" rebrand: moves the existing settings/
    // channels/logs folder instead of leaving it behind and silently starting the user over with an
    // empty database. Must run before anything (logger, DB) touches either folder.
    private static void MigrateAppDataFolder()
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var oldDir = Path.Combine(appDataRoot, "IptvPlayer");
        var newDir = Path.Combine(appDataRoot, "BennyBox");

        if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
        {
            try
            {
                Directory.Move(oldDir, newDir);
            }
            catch
            {
                // Best-effort - if this fails (e.g. a file is locked), the app just starts fresh
                // under the new folder name; there's no logger yet to report it to.
            }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
