using Iptv.App.Services;
using Iptv.App.ViewModels;
using Iptv.App.Views;
using Iptv.Core.Models;
using Iptv.Core.Services;
using Iptv.Data.Sqlite;
using Iptv.Sources.M3u;
using Iptv.Sources.Xmltv;
using Iptv.Sources.Xtream;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
#if DEBUG
using CommunityToolkit.Mvvm.Messaging;
using Iptv.App.Messages;
using Microsoft.Extensions.Configuration;
#endif

namespace Iptv.App;

// Everything needed to build the DI/host graph - this is real work (DB open + migration, HttpClient
// factory setup, etc.) and is deliberately kept out of Program.Main so it can run on a background
// thread after the splash window is already on screen, instead of blocking before any window exists.
internal static class AppBootstrapper
{
    public static IHost BuildHost(string[] args, Task<LibVLC> libVlcTask)
    {
        var builder = Host.CreateApplicationBuilder(args);
#if DEBUG
        // Local-only dev convenience: pulls from %APPDATA%\Microsoft\UserSecrets\<id>\secrets.json,
        // which lives outside the repo entirely and is never part of a Release build - see
        // "dotnet user-secrets set DevXtream:ServerUrl/Username/Password/ProfileName".
        builder.Configuration.AddUserSecrets<App>();
#endif
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog();
        ConfigureServices(builder.Services, libVlcTask);
        return builder.Build();
    }

    private static void ConfigureServices(IServiceCollection services, Task<LibVLC> libVlcTask)
    {
        services.AddTransient<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();
        // By the time this actually resolves, libVlcTask has very likely already finished in the
        // background (it started at the very top of Main) - this mostly just picks up the result.
        services.AddSingleton(_ => libVlcTask.GetAwaiter().GetResult());
        services.AddSingleton<PlayerViewModel>();
        services.AddTransient<LiveTvViewModel>();
        services.AddTransient<GuideViewModel>();
        services.AddTransient<SeriesViewModel>();
        services.AddTransient<MoviesViewModel>();
        services.AddTransient<FavoritesViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<IProfileRepository, SqliteProfileRepository>();
        services.AddSingleton<IChannelRepository, SqliteChannelRepository>();
        services.AddSingleton<IEpgRepository, SqliteEpgRepository>();
        services.AddSingleton<ISeriesRepository, SqliteSeriesRepository>();
        services.AddSingleton<IMovieRepository, SqliteMovieRepository>();
        services.AddSingleton<IFavoriteRepository, SqliteFavoriteRepository>();
        services.AddSingleton<IWatchProgressRepository, SqliteWatchProgressRepository>();
        services.AddSingleton<TimeshiftUrlService>();
        services.AddSingleton<IReminderRepository, SqliteReminderRepository>();
        services.AddSingleton<ISettingsStore, SqliteSettingsStore>();
        services.AddSingleton<PlaylistImportService>();
        services.AddSingleton<EpgImportService>();
        services.AddSingleton<SeriesImportService>();
        services.AddSingleton<MovieImportService>();
        services.AddSingleton<AccountInfoService>();

        // Some IPTV providers block requests with no User-Agent header (HttpClient sends none by default, unlike curl/browsers).
        const string userAgent = "BennyBox/1.0";
        // EPG feeds can be tens/hundreds of MB - the default 100s HttpClient timeout can be too tight.
        var epgTimeout = TimeSpan.FromMinutes(5);

        services.AddHttpClient<M3uChannelSource>(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent));
        services.AddSingleton<IChannelSource>(sp => sp.GetRequiredService<M3uChannelSource>());

        services.AddHttpClient<XtreamClient>(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent));
        services.AddSingleton<XtreamChannelSource>();
        services.AddSingleton<IChannelSource>(sp => sp.GetRequiredService<XtreamChannelSource>());
        services.AddSingleton<XtreamSeriesSource>();
        services.AddSingleton<ISeriesSource>(sp => sp.GetRequiredService<XtreamSeriesSource>());
        services.AddSingleton<XtreamMovieSource>();
        services.AddSingleton<IMovieSource>(sp => sp.GetRequiredService<XtreamMovieSource>());
        services.AddSingleton<XtreamAccountInfoSource>();
        services.AddSingleton<IAccountInfoSource>(sp => sp.GetRequiredService<XtreamAccountInfoSource>());
        services.AddTransient<AddProfileViewModel>();

        services.AddHttpClient<XmltvEpgSource>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            client.Timeout = epgTimeout;
        });
        services.AddSingleton<IEpgSource>(sp => sp.GetRequiredService<XmltvEpgSource>());

        services.AddHttpClient<XtreamEpgSource>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            client.Timeout = epgTimeout;
        });
        services.AddSingleton<IEpgSource>(sp => sp.GetRequiredService<XtreamEpgSource>());

        services.AddHttpClient<ChannelLogoCache>(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent));
        services.AddSingleton<IChannelLogoCache>(sp => sp.GetRequiredService<ChannelLogoCache>());
    }

#if DEBUG
    // Dev-only convenience so `dotnet run`/F5 from Visual Studio has real data with zero clicks.
    // No-ops silently if the machine has no DevXtream user secrets configured, and only seeds once
    // (checked by profile name) so it doesn't re-import on every launch.
    public static async Task SeedDevXtreamProfileAsync(IServiceProvider services)
    {
        try
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var serverUrl = configuration["DevXtream:ServerUrl"];
            var username = configuration["DevXtream:Username"];
            var password = configuration["DevXtream:Password"];
            var profileName = configuration["DevXtream:ProfileName"] ?? "Dev Xtream (local)";

            if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return;
            }

            var profileRepository = services.GetRequiredService<IProfileRepository>();
            var existingProfiles = await profileRepository.GetAllAsync();
            if (existingProfiles.Any(p => p.Name == profileName))
            {
                return;
            }

            var profile = new ProfileSource
            {
                Name = profileName,
                SourceType = ProfileSourceType.XtreamCodes,
                XtreamServerUrl = serverUrl.TrimEnd('/'),
                XtreamUsername = username,
                XtreamPasswordEncrypted = CredentialProtector.Protect(password),
                EpgSourceType = EpgSourceType.XtreamEmbedded,
                SortOrder = existingProfiles.Count
            };

            await profileRepository.AddAsync(profile);

            var importService = services.GetRequiredService<PlaylistImportService>();
            var result = await importService.ImportAsync(profile);

            var epgImportService = services.GetRequiredService<EpgImportService>();
            await epgImportService.ImportAsync(profile);

            Log.Information(
                "Seeded dev Xtream profile {ProfileName} from user secrets: {ChannelCount} channels, {CategoryCount} categories",
                profileName, result.Channels.Count, result.Categories.Count);

            WeakReferenceMessenger.Default.Send(new ChannelsUpdatedMessage());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to seed dev Xtream profile from user secrets");
        }
    }
#endif
}
