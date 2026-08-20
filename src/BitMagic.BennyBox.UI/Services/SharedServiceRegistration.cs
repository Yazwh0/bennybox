using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.Data.Sqlite;
using BitMagic.BennyBox.Services;
using BitMagic.BennyBox.Sources.Folder;
using BitMagic.BennyBox.Sources.GitHub;
using BitMagic.BennyBox.Sources.M3u;
using BitMagic.BennyBox.Sources.Tmdb;
using BitMagic.BennyBox.Sources.Xmltv;
using BitMagic.BennyBox.Sources.Xtream;
using BitMagic.BennyBox.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BitMagic.BennyBox.UI.Services;

// Everything in the DI graph that's platform-neutral - repositories, content sources, import
// services, DownloadManager, ChannelLogoCache, and the page ViewModels that don't need a platform
// head's own types. Each head (desktop's AppBootstrapper, Android's App.axaml.cs) calls this and
// then adds its own platform-specific registrations on top: ICredentialProtector, IAppPaths,
// IPlayerEngine, and whatever owns navigation/the app shell (MainWindow/MainWindowViewModel on
// desktop; no Android equivalent yet - see the Android port plan, Phase 3).
//
// Requires ICredentialProtector and IAppPaths to already be registered by the caller before this
// runs - DownloadManager, MediaFileSystemFactory, and the Xtream sources all resolve them.
public static class SharedServiceRegistration
{
    public static void AddSharedServices(IServiceCollection services)
    {
        services.AddTransient<SearchViewModel>();
        services.AddTransient<LiveTvViewModel>();
        services.AddTransient<GuideViewModel>();
        services.AddTransient<SeriesViewModel>();
        services.AddTransient<MoviesViewModel>();
        services.AddTransient<ClipsViewModel>();
        services.AddTransient<DownloadsViewModel>();
        services.AddTransient<FavoritesViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AddProfileViewModel>();
        services.AddSingleton<PlayerViewModel>();

        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<IProfileRepository, SqliteProfileRepository>();
        services.AddSingleton<IChannelRepository, SqliteChannelRepository>();
        services.AddSingleton<IEpgRepository, SqliteEpgRepository>();
        services.AddSingleton<ISeriesRepository, SqliteSeriesRepository>();
        services.AddSingleton<IMovieRepository, SqliteMovieRepository>();
        services.AddSingleton<IClipRepository, SqliteClipRepository>();
        services.AddSingleton<IDownloadRepository, SqliteDownloadRepository>();
        services.AddSingleton<IEpisodeCacheRepository, SqliteEpisodeCacheRepository>();
        services.AddSingleton<IMetadataEnrichmentCacheRepository, SqliteMetadataEnrichmentCacheRepository>();
        services.AddSingleton<IFavoriteRepository, SqliteFavoriteRepository>();
        services.AddSingleton<IWatchProgressRepository, SqliteWatchProgressRepository>();
        services.AddSingleton<TimeshiftUrlService>();
        services.AddSingleton<IReminderRepository, SqliteReminderRepository>();
        services.AddSingleton<IWatchedItemRepository, SqliteWatchedItemRepository>();
        services.AddSingleton<ISettingsStore, SqliteSettingsStore>();
        services.AddSingleton<PlaylistImportService>();
        services.AddSingleton<EpgImportService>();
        services.AddSingleton<SeriesImportService>();
        services.AddSingleton<MovieImportService>();
        services.AddSingleton<ClipImportService>();
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

        // LocalFolder and Sftp are two registrations apiece of the same FolderMovieSource/
        // FolderSeriesSource class, each fixed to its own SourceType - see FolderMovieSource's
        // comment for why (matches the "one registered instance per SourceType" pattern the import
        // services already dispatch on).
        services.AddSingleton<IMediaFileSystemFactory, MediaFileSystemFactory>();

        // TMDb fills in Plot/Genre/ReleaseDate/poster for LocalFolder/Sftp titles with no local NFO/
        // poster (see IMetadataEnrichmentService) - CachingMetadataEnrichmentService sits in front so a
        // title is only ever sent to TMDb once, including "nothing found" results.
        services.AddHttpClient<TmdbMetadataEnrichmentService>(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent));
        services.AddSingleton<IMetadataEnrichmentService>(sp => new CachingMetadataEnrichmentService(
            sp.GetRequiredService<TmdbMetadataEnrichmentService>(),
            sp.GetRequiredService<IMetadataEnrichmentCacheRepository>()));

        // Checked once at startup (see MainWindowViewModel) - no caching layer needed, unlike TMDb
        // above, since this is at most one call per launch rather than one per title in a library scan.
        services.AddHttpClient<GitHubUpdateCheckService>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        });
        services.AddTransient<IUpdateCheckService>(sp => sp.GetRequiredService<GitHubUpdateCheckService>());

        services.AddSingleton<IMovieSource>(sp => new FolderMovieSource(ProfileSourceType.LocalFolder, sp.GetRequiredService<IMediaFileSystemFactory>(), sp.GetRequiredService<IMetadataEnrichmentService>()));
        services.AddSingleton<IMovieSource>(sp => new FolderMovieSource(ProfileSourceType.Sftp, sp.GetRequiredService<IMediaFileSystemFactory>(), sp.GetRequiredService<IMetadataEnrichmentService>()));
        services.AddSingleton<ISeriesSource>(sp => new FolderSeriesSource(ProfileSourceType.LocalFolder, sp.GetRequiredService<IMediaFileSystemFactory>(), sp.GetRequiredService<IEpisodeCacheRepository>(), sp.GetRequiredService<IMetadataEnrichmentService>()));
        services.AddSingleton<ISeriesSource>(sp => new FolderSeriesSource(ProfileSourceType.Sftp, sp.GetRequiredService<IMediaFileSystemFactory>(), sp.GetRequiredService<IEpisodeCacheRepository>(), sp.GetRequiredService<IMetadataEnrichmentService>()));

        // No IMetadataEnrichmentService passed - Clips never call TMDb, by design (see
        // FolderMediaScanner.ScanClipsAsync).
        services.AddSingleton<IClipSource>(sp => new FolderClipSource(ProfileSourceType.LocalFolder, sp.GetRequiredService<IMediaFileSystemFactory>()));
        services.AddSingleton<IClipSource>(sp => new FolderClipSource(ProfileSourceType.Sftp, sp.GetRequiredService<IMediaFileSystemFactory>()));

        // DownloadManager needs one long-lived HttpClient (for Xtream-sourced downloads) but must
        // itself be a genuine singleton - its semaphore/active-download tracking and DownloadChanged
        // event are shared state every subscriber (DownloadsViewModel, Movie/Episode/Clip row items,
        // DownloadPlaybackResolver) needs to see the same instance of. AddHttpClient<T>() registers T
        // itself as transient, which would defeat that, so the HttpClient is fetched via a named
        // client instead and handed to an explicitly-singleton registration - HttpClient instances are
        // safe to hold and reuse for the app's whole lifetime (that's IHttpClientFactory's point).
        // Movie/episode files can take well over the default 100s HttpClient timeout to pull down
        // (that timeout covers the whole response body being read, not just headers - see
        // DownloadManager.DownloadHttpAsync) - unbounded here since a genuine stall/user cancel is
        // already handled per-download via DownloadManager's own CancellationTokenSource.
        services.AddHttpClient("Downloads", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton(sp => new DownloadManager(
            sp.GetRequiredService<IDownloadRepository>(),
            sp.GetRequiredService<IProfileRepository>(),
            sp.GetRequiredService<IMediaFileSystemFactory>(),
            sp.GetRequiredService<MovieImportService>(),
            sp.GetRequiredService<SeriesImportService>(),
            sp.GetRequiredService<ClipImportService>(),
            sp.GetRequiredService<IMovieRepository>(),
            sp.GetRequiredService<IClipRepository>(),
            sp.GetRequiredService<ISeriesRepository>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IAppPaths>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Downloads")));
        services.AddSingleton<DownloadPlaybackResolver>();

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
}
