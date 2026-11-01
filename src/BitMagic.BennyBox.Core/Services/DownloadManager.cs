using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

// Downloads a Movie/Episode/Clip to disk, then hands it to the existing FolderMediaScanner pipeline
// by writing it into one designated Local Folder profile ("the Downloads profile") and re-running
// that profile's own import - the same way any other Local Folder content gets scanned. There is no
// separate "downloaded content" browsing model: a downloaded title is just another Local Folder item,
// reusing everything MoviesViewModel/SeriesViewModel/ClipsViewModel already do (badges, favorites,
// search, etc.) for free. See DownloadPlaybackResolver for how playback prefers this copy once it exists.
public class DownloadManager
{
    private const int MaxConcurrentDownloads = 2;
    private const string DownloadsProfileIdKey = "DownloadsProfileId";
    private const string DownloadsRootPathKey = "DownloadsRootPath";
    private const string DownloadsProfileName = "Downloads";

    // Progress is reported far more often than it's worth persisting/broadcasting - a fast local copy
    // can tick this every few microseconds. The database copy only matters after a restart, so it's
    // written much less often than the panel is updated - see DownloadProgressReporter.
    private const long ProgressNotifyIntervalMs = 500;
    private const long ProgressPersistIntervalMs = 5000;

    // Long enough overall for Wi-Fi to come back after the machine wakes.
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)
    ];

    private const int DiskFullWin32 = 0x70;
    private const int HandleDiskFullWin32 = 0x27;
    private const int DiskFullUnix = 28;

    private readonly IDownloadRepository _downloadRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly IMediaFileSystemFactory _fileSystemFactory;
    private readonly MovieImportService _movieImportService;
    private readonly SeriesImportService _seriesImportService;
    private readonly ClipImportService _clipImportService;
    private readonly IMovieRepository _movieRepository;
    private readonly IClipRepository _clipRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly ISettingsStore _settingsStore;
    private readonly IAppPaths _appPaths;
    private readonly ISleepInhibitor _sleepInhibitor;
    private readonly Lock _activeDownloadCountLock = new();
    private int _activeDownloadCount;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _concurrencyLimiter = new(MaxConcurrentDownloads);
    private readonly Dictionary<Guid, CancellationTokenSource> _activeCancellations = [];
    private readonly Lock _activeCancellationsLock = new();

    // Fired whenever a Download's status changes - subscribed to directly by whichever ViewModels care
    // (the Downloads panel, and the row-level download button state on Movie/Episode/Clip items). A
    // plain event, not a WeakReferenceMessenger message, since this is a Core service with no App-layer
    // dependency - see the plan discussion for why.
    public event EventHandler<Download>? DownloadChanged;

    // Fired periodically while a download is transferring. Separate from DownloadChanged because the
    // library pages only care about status and find their row by scanning the whole library, which is
    // too expensive to repeat on the UI thread for every tick.
    public event EventHandler<Download>? DownloadProgressChanged;

    public DownloadManager(
        IDownloadRepository downloadRepository,
        IProfileRepository profileRepository,
        IMediaFileSystemFactory fileSystemFactory,
        MovieImportService movieImportService,
        SeriesImportService seriesImportService,
        ClipImportService clipImportService,
        IMovieRepository movieRepository,
        IClipRepository clipRepository,
        ISeriesRepository seriesRepository,
        ISettingsStore settingsStore,
        IAppPaths appPaths,
        ISleepInhibitor sleepInhibitor,
        HttpClient httpClient)
    {
        _downloadRepository = downloadRepository;
        _profileRepository = profileRepository;
        _fileSystemFactory = fileSystemFactory;
        _movieImportService = movieImportService;
        _seriesImportService = seriesImportService;
        _clipImportService = clipImportService;
        _movieRepository = movieRepository;
        _clipRepository = clipRepository;
        _seriesRepository = seriesRepository;
        _settingsStore = settingsStore;
        _appPaths = appPaths;
        _sleepInhibitor = sleepInhibitor;
        _httpClient = httpClient;
    }

    public async Task<string> GetDownloadsRootAsync(CancellationToken cancellationToken = default) =>
        await _settingsStore.GetAsync(DownloadsRootPathKey, cancellationToken)
        ?? _appPaths.DownloadsRoot;

    // Changes where FUTURE downloads land - existing downloaded files stay where they are (see
    // SettingsViewModel's Downloads section). Doesn't touch the Downloads profile's paths until the
    // next download actually runs and re-ensures them under the new root.
    public Task SetDownloadsRootAsync(string root, CancellationToken cancellationToken = default) =>
        _settingsStore.SetAsync(DownloadsRootPathKey, root, cancellationToken);

    public async Task<ProfileSource?> GetDownloadsProfileIfExistsAsync(CancellationToken cancellationToken = default)
    {
        var idText = await _settingsStore.GetAsync(DownloadsProfileIdKey, cancellationToken);
        return idText is not null && Guid.TryParse(idText, out var id)
            ? await _profileRepository.GetByIdAsync(id, cancellationToken)
            : null;
    }

    // Lazily creates the hidden Local Folder profile downloads land in - called the first time a
    // download is actually requested, not eagerly at startup, so a user who never downloads anything
    // never gets a stray profile/folder tree created on their behalf.
    public async Task<ProfileSource> EnsureDownloadsProfileAsync(CancellationToken cancellationToken = default)
    {
        var existing = await GetDownloadsProfileIfExistsAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var root = await GetDownloadsRootAsync(cancellationToken);
        var moviesPath = Path.Combine(root, "Movies");
        var seriesPath = Path.Combine(root, "Series");
        var clipsPath = Path.Combine(root, "Clips");
        Directory.CreateDirectory(moviesPath);
        Directory.CreateDirectory(seriesPath);
        Directory.CreateDirectory(clipsPath);

        var profile = new ProfileSource
        {
            Name = DownloadsProfileName,
            SourceType = ProfileSourceType.LocalFolder,
            LocalMoviesPath = moviesPath,
            LocalSeriesPath = seriesPath,
            LocalClipsPath = clipsPath
        };

        await _profileRepository.AddAsync(profile, cancellationToken);
        await _settingsStore.SetAsync(DownloadsProfileIdKey, profile.Id.ToString(), cancellationToken);
        return profile;
    }

    public Task<Guid> QueueMovieDownloadAsync(ProfileSource sourceProfile, Movie movie, CancellationToken cancellationToken = default) =>
        QueueAsync(sourceProfile, MediaKind.Movies, WatchProgressContentType.Movie, movie.SourceMovieId, movie.Name, movie.CoverUrl, movie.StreamUrl,
            downloadsProfile => Path.Combine(downloadsProfile.LocalMoviesPath!, BuildMovieOrClipFileName(movie.Name, GetExtension(sourceProfile.SourceType, movie.SourceMovieId, movie.StreamUrl))),
            cancellationToken);

    public Task<Guid> QueueClipDownloadAsync(ProfileSource sourceProfile, Movie clip, CancellationToken cancellationToken = default) =>
        QueueAsync(sourceProfile, MediaKind.Clips, WatchProgressContentType.Clip, clip.SourceMovieId, clip.Name, clip.CoverUrl, clip.StreamUrl,
            downloadsProfile => Path.Combine(downloadsProfile.LocalClipsPath!, BuildMovieOrClipFileName(clip.Name, GetExtension(sourceProfile.SourceType, clip.SourceMovieId, clip.StreamUrl))),
            cancellationToken);

    public Task<Guid> QueueEpisodeDownloadAsync(ProfileSource sourceProfile, Series series, Episode episode, CancellationToken cancellationToken = default)
    {
        var title = $"{series.Name} - S{episode.Season:00}E{episode.EpisodeNumber:00} - {episode.Title}";
        return QueueAsync(sourceProfile, MediaKind.Series, WatchProgressContentType.Episode,
            ContentKeys.ForEpisode(series.SourceSeriesId, episode.SourceEpisodeId), title, series.CoverUrl, episode.StreamUrl,
            downloadsProfile => BuildEpisodeDestinationPath(downloadsProfile, series, episode, GetExtension(sourceProfile.SourceType, episode.SourceEpisodeId, episode.StreamUrl)),
            cancellationToken);
    }

    private async Task<Guid> QueueAsync(
        ProfileSource sourceProfile, MediaKind kind, WatchProgressContentType contentType, string sourceId, string title, string? coverUrl, string streamUrl,
        Func<ProfileSource, string> buildDestinationPath, CancellationToken cancellationToken)
    {
        // Reuse a prior Failed/Canceled attempt at the exact same title instead of inserting a
        // duplicate row - the destination path is deterministic (see BuildMovieOrClipFileName/
        // BuildEpisodeDestinationPath), so whatever partial file that attempt left behind (see
        // RunDownloadAsync - a genuine failure no longer deletes it) gets picked up as a resume by
        // DownloadToFileAsync/DownloadHttpAsync automatically, without this call needing to know or
        // care whether it's "starting fresh" or "resuming".
        var existing = (await _downloadRepository.GetAllAsync(cancellationToken)).FirstOrDefault(d =>
            d.OriginalProfileId == sourceProfile.Id && d.ContentType == contentType && d.OriginalSourceId == sourceId &&
            d.Status is DownloadStatus.Failed or DownloadStatus.Canceled);

        Download download;
        if (existing is not null)
        {
            download = existing;
            download.Status = DownloadStatus.Queued;
            download.ErrorMessage = null;
            download.CompletedUtc = null;
            await _downloadRepository.RequeueAsync(download.Id, cancellationToken);
        }
        else
        {
            download = new Download
            {
                OriginalProfileId = sourceProfile.Id,
                ContentType = contentType,
                OriginalSourceId = sourceId,
                Title = title,
                CoverUrl = coverUrl,
                Status = DownloadStatus.Queued,
                StartedUtc = DateTime.UtcNow
            };
            await _downloadRepository.CreateAsync(download, cancellationToken);
        }

        DownloadChanged?.Invoke(this, download);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_activeCancellationsLock)
        {
            _activeCancellations[download.Id] = cts;
        }

        // Task.Run, not a bare fire-and-forget call - QueueAsync is invoked from a ViewModel command
        // handler on the UI thread, and without this, everything up to RunDownloadAsync's first await
        // (and thus the Progress<T> constructed inside it) would capture the UI SynchronizationContext,
        // silently marshaling every throttled progress tick through the UI dispatcher. Running on a
        // pool thread from the start means DownloadChanged fires from a background thread instead -
        // subscribers (DownloadsViewModel, row view models) are expected to Dispatcher.UIThread.Post
        // their own UI updates, the same convention already used everywhere else in this app for
        // background-thread-originated updates.
        // No token on Task.Run itself: a cancel before the task starts would skip RunDownloadAsync, and
        // with it the handling that records the row as Canceled.
        _ = Task.Run(() => RunDownloadAsync(download, sourceProfile, kind, streamUrl, buildDestinationPath, cts.Token));
        return download.Id;
    }

    public void CancelDownload(Guid downloadId)
    {
        lock (_activeCancellationsLock)
        {
            if (_activeCancellations.TryGetValue(downloadId, out var cts))
            {
                cts.Cancel();
            }
        }
    }

    // Re-queues a Failed/Canceled row in place, for the Downloads panel's Retry button - QueueAsync
    // handles the same "click Download again on the row itself" path by finding this same row, but the
    // panel only has the Download row itself to work from, not a live Movie/Episode/Clip, so this has
    // to re-fetch the original item first (mirroring what QueueMovieDownloadAsync/etc. already do,
    // just working backward from OriginalProfileId/ContentType/OriginalSourceId instead of forward
    // from an already-in-hand object). Resumes from wherever the prior attempt's partial file left off,
    // same as any other requeue - see QueueAsync's doc comment.
    public async Task RetryDownloadAsync(Guid downloadId, CancellationToken cancellationToken = default)
    {
        var download = (await _downloadRepository.GetAllAsync(cancellationToken)).FirstOrDefault(d => d.Id == downloadId);
        if (download is null || download.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Completed)
        {
            return;
        }

        var sourceProfile = await _profileRepository.GetByIdAsync(download.OriginalProfileId, cancellationToken);
        if (sourceProfile is null)
        {
            await _downloadRepository.MarkFailedAsync(downloadId, "The original profile no longer exists.", cancellationToken);
            download.Status = DownloadStatus.Failed;
            DownloadChanged?.Invoke(this, download);
            return;
        }

        var resolved = download.ContentType switch
        {
            WatchProgressContentType.Movie => await ResolveMovieAsync(sourceProfile, download.OriginalSourceId, cancellationToken),
            WatchProgressContentType.Clip => await ResolveClipAsync(sourceProfile, download.OriginalSourceId, cancellationToken),
            WatchProgressContentType.Episode => await ResolveEpisodeAsync(sourceProfile, download.OriginalSourceId, cancellationToken),
            _ => null
        };

        if (resolved is not { } r)
        {
            await _downloadRepository.MarkFailedAsync(downloadId, "This title is no longer available from its original source - open it from its page to download it again.", cancellationToken);
            download.Status = DownloadStatus.Failed;
            DownloadChanged?.Invoke(this, download);
            return;
        }

        download.Status = DownloadStatus.Queued;
        download.ErrorMessage = null;
        download.CompletedUtc = null;
        await _downloadRepository.RequeueAsync(downloadId, cancellationToken);
        DownloadChanged?.Invoke(this, download);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_activeCancellationsLock)
        {
            _activeCancellations[downloadId] = cts;
        }

        _ = Task.Run(() => RunDownloadAsync(download, sourceProfile, r.Kind, r.StreamUrl, r.BuildDestinationPath, cts.Token));
    }

    // Resolves where a completed download's playable copy now lives in this app's own library
    // (always the Downloads profile - that's where RunDownloadAsync/RescanDownloadsProfileAsync put
    // it) - for "open it" navigation from the Downloads panel (see DownloadsViewModel.OpenAsync).
    // Matches by normalized title, the same technique DownloadPlaybackResolver uses for playback
    // fallback. For an episode, matched via the ORIGINAL series' true (unsanitized) name rather than
    // trying to recover it from the sanitized folder name in LocalRelativePath - the original
    // profile/series row may itself be gone by now, in which case this simply returns null rather
    // than falling back to a fuzzier match. Returns (item, sourceName): item is a Movie for
    // Movie/Clip content, or a Series for Episode content - the caller already knows which from
    // download.ContentType.
    public async Task<(object Item, string SourceName)?> ResolveLibraryLocationAsync(Download download, CancellationToken cancellationToken = default)
    {
        if (download.Status != DownloadStatus.Completed)
        {
            return null;
        }

        var downloadsProfile = await GetDownloadsProfileIfExistsAsync(cancellationToken);
        if (downloadsProfile is null)
        {
            return null;
        }

        switch (download.ContentType)
        {
            case WatchProgressContentType.Movie:
            {
                var movies = await _movieRepository.GetMoviesAsync(downloadsProfile.Id, cancellationToken);
                var match = movies.FirstOrDefault(m => Normalize(m.Name) == Normalize(download.Title));
                return match is null ? null : (match, downloadsProfile.Name);
            }
            case WatchProgressContentType.Clip:
            {
                var clips = await _clipRepository.GetClipsAsync(downloadsProfile.Id, cancellationToken);
                var match = clips.FirstOrDefault(c => Normalize(c.Name) == Normalize(download.Title));
                return match is null ? null : (match, downloadsProfile.Name);
            }
            case WatchProgressContentType.Episode:
            {
                var seriesSourceId = ExtractEpisodeSeriesId(download.OriginalSourceId);
                var originalSeries = seriesSourceId is null
                    ? null
                    : (await _seriesRepository.GetSeriesAsync(download.OriginalProfileId, cancellationToken))
                        .FirstOrDefault(s => s.SourceSeriesId == seriesSourceId);
                if (originalSeries is null)
                {
                    return null;
                }

                var downloadsSeriesList = await _seriesRepository.GetSeriesAsync(downloadsProfile.Id, cancellationToken);
                var matchedSeries = downloadsSeriesList.FirstOrDefault(s => Normalize(s.Name) == Normalize(originalSeries.Name));
                return matchedSeries is null ? null : (matchedSeries, downloadsProfile.Name);
            }
            default:
                return null;
        }
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private async Task<(string StreamUrl, MediaKind Kind, Func<ProfileSource, string> BuildDestinationPath)?> ResolveMovieAsync(
        ProfileSource sourceProfile, string sourceMovieId, CancellationToken cancellationToken)
    {
        var movies = await _movieRepository.GetMoviesAsync(sourceProfile.Id, cancellationToken);
        var movie = movies.FirstOrDefault(m => m.SourceMovieId == sourceMovieId);
        if (movie is null)
        {
            return null;
        }

        return (movie.StreamUrl, MediaKind.Movies,
            downloadsProfile => Path.Combine(downloadsProfile.LocalMoviesPath!, BuildMovieOrClipFileName(movie.Name, GetExtension(sourceProfile.SourceType, movie.SourceMovieId, movie.StreamUrl))));
    }

    private async Task<(string StreamUrl, MediaKind Kind, Func<ProfileSource, string> BuildDestinationPath)?> ResolveClipAsync(
        ProfileSource sourceProfile, string sourceClipId, CancellationToken cancellationToken)
    {
        var clips = await _clipRepository.GetClipsAsync(sourceProfile.Id, cancellationToken);
        var clip = clips.FirstOrDefault(c => c.SourceMovieId == sourceClipId);
        if (clip is null)
        {
            return null;
        }

        return (clip.StreamUrl, MediaKind.Clips,
            downloadsProfile => Path.Combine(downloadsProfile.LocalClipsPath!, BuildMovieOrClipFileName(clip.Name, GetExtension(sourceProfile.SourceType, clip.SourceMovieId, clip.StreamUrl))));
    }

    private async Task<(string StreamUrl, MediaKind Kind, Func<ProfileSource, string> BuildDestinationPath)?> ResolveEpisodeAsync(
        ProfileSource sourceProfile, string episodeContentKey, CancellationToken cancellationToken)
    {
        var seriesSourceId = ExtractEpisodeSeriesId(episodeContentKey);
        if (seriesSourceId is null)
        {
            return null;
        }

        var seriesList = await _seriesRepository.GetSeriesAsync(sourceProfile.Id, cancellationToken);
        var series = seriesList.FirstOrDefault(s => s.SourceSeriesId == seriesSourceId);
        if (series is null)
        {
            return null;
        }

        var episodes = await _seriesImportService.GetEpisodesAsync(sourceProfile, series, cancellationToken);
        var episode = episodes.FirstOrDefault(e => ContentKeys.ForEpisode(series.SourceSeriesId, e.SourceEpisodeId) == episodeContentKey);
        if (episode is null)
        {
            return null;
        }

        return (episode.StreamUrl, MediaKind.Series,
            downloadsProfile => BuildEpisodeDestinationPath(downloadsProfile, series, episode, GetExtension(sourceProfile.SourceType, episode.SourceEpisodeId, episode.StreamUrl)));
    }

    // Removes a completed download's file and DB row. Does not attempt to remove an in-progress
    // download's file - cancel it first (see CancelDownload), which cleans up its own partial file.
    public async Task DeleteDownloadAsync(Download download, CancellationToken cancellationToken = default)
    {
        if (download.LocalRelativePath is { } relativePath)
        {
            var downloadsProfile = await GetDownloadsProfileIfExistsAsync(cancellationToken);
            var root = download.ContentType switch
            {
                WatchProgressContentType.Movie => downloadsProfile?.LocalMoviesPath,
                WatchProgressContentType.Clip => downloadsProfile?.LocalClipsPath,
                _ => downloadsProfile?.LocalSeriesPath
            };
            if (root is not null)
            {
                var fullPath = Path.Combine(root, relativePath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
        }

        await _downloadRepository.DeleteAsync(download.Id, cancellationToken);

        if (download.Status == DownloadStatus.Completed)
        {
            await RescanDownloadsProfileAsync(ContentTypeToKind(download.ContentType), cancellationToken);
        }
    }

    // Called once at app startup, before anything else touches Downloads - any row still
    // Queued/Downloading belonged to a copy loop that no longer exists (the previous session ended).
    public Task ReconcileInterruptedDownloadsAsync(CancellationToken cancellationToken = default) =>
        _downloadRepository.MarkInterruptedDownloadsAsFailedAsync(cancellationToken);

    // See SettingsViewModel's Downloads section - wipes every download (canceling any in-progress
    // ones first) and every file on disk, then rescans so Movies/Series/Clips stop showing them. A
    // dedicated method rather than looping DeleteDownloadAsync per row - that would rescan the
    // Downloads profile once per download instead of once total.
    public async Task ClearAllDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var downloadsProfile = await GetDownloadsProfileIfExistsAsync(cancellationToken);
        var downloads = await _downloadRepository.GetAllAsync(cancellationToken);
        foreach (var download in downloads)
        {
            if (download.Status is DownloadStatus.Queued or DownloadStatus.Downloading)
            {
                // Left for RunDownloadAsync's own cancellation handling to clean up (partial file +
                // its own DB row) rather than deleted here too - deleting it out from under a task
                // that's still actively running is a race (it would go on to update/delete a row that
                // no longer exists). It'll land as Canceled and can be cleared from the panel like any
                // other finished row once it actually stops.
                CancelDownload(download.Id);
                continue;
            }

            await _downloadRepository.DeleteAsync(download.Id, cancellationToken);
        }

        if (downloadsProfile is null)
        {
            return;
        }

        foreach (var root in new[] { downloadsProfile.LocalMoviesPath, downloadsProfile.LocalSeriesPath, downloadsProfile.LocalClipsPath })
        {
            if (root is null || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(root))
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }

        await _movieImportService.ImportAsync(downloadsProfile, cancellationToken);
        await _seriesImportService.ImportAsync(downloadsProfile, cancellationToken);
        await _clipImportService.ImportAsync(downloadsProfile, cancellationToken);
    }

    private async Task RunDownloadAsync(
        Download download, ProfileSource sourceProfile, MediaKind kind, string streamUrl,
        Func<ProfileSource, string> buildDestinationPath, CancellationToken cancellationToken)
    {
        string? destinationPath = null;
        DownloadProgressReporter? progress = null;
        var holdsConcurrencySlot = false;
        DownloadStarted();
        try
        {
            // Inside the try so canceling a download still waiting in the queue lands as Canceled.
            await _concurrencyLimiter.WaitAsync(cancellationToken);
            holdsConcurrencySlot = true;

            var downloadsProfile = await EnsureDownloadsProfileAsync(cancellationToken);
            destinationPath = buildDestinationPath(downloadsProfile);

            var localRoot = kind switch
            {
                MediaKind.Movies => downloadsProfile.LocalMoviesPath!,
                MediaKind.Clips => downloadsProfile.LocalClipsPath!,
                _ => downloadsProfile.LocalSeriesPath!
            };
            var localRelativePath = Path.GetRelativePath(localRoot, destinationPath);

            // Persisted now, not just on completion - so a Failed/interrupted download's partial file
            // can still be found afterward, both for Delete (DeleteDownloadAsync already checks this
            // field) and for resume (DownloadToFileAsync/DownloadHttpAsync check for an existing file
            // at this same deterministic path on the NEXT attempt, wherever that path is computed from
            // - they don't actually read this DB field, but it needs to be recorded regardless so the
            // rest of the app knows a file exists here at all).
            await _downloadRepository.SetDestinationPathAsync(download.Id, localRelativePath, cancellationToken);
            download.LocalRelativePath = localRelativePath;

            // download.BytesDownloaded/TotalBytes, not a hardcoded 0 - for a resumed row these already
            // hold the prior attempt's last known progress, and resetting to 0 here would flash a
            // misleading "0 bytes" in the panel for the brief moment before the first real progress
            // tick arrives. A genuinely fresh row's BytesDownloaded is already 0 by construction.
            download.Status = DownloadStatus.Downloading;
            await _downloadRepository.UpdateProgressAsync(download.Id, download.BytesDownloaded, download.TotalBytes, cancellationToken);
            DownloadChanged?.Invoke(this, download);

            progress = new DownloadProgressReporter(this, download);

            await TransferWithRetriesAsync(download, sourceProfile, kind, streamUrl, destinationPath, progress, cancellationToken);

            await progress.StopAsync();
            await _downloadRepository.UpdateProgressAsync(download.Id, download.BytesDownloaded, download.TotalBytes, CancellationToken.None);
            await _downloadRepository.MarkCompletedAsync(download.Id, localRelativePath, cancellationToken);
            download.Status = DownloadStatus.Completed;
            download.CompletedUtc = DateTime.UtcNow;

            // Re-scan just the Downloads profile so the new file shows up as a normal Movie/Series/Clip
            // - same ImportAsync every Refresh already calls, just scoped to this one profile.
            await RescanDownloadsProfileAsync(kind, CancellationToken.None);

            DownloadChanged?.Invoke(this, download);
        }
        catch (OperationCanceledException)
        {
            // Cancel is a deliberate "I don't want this" action, unlike a genuine failure below - the
            // partial bytes aren't worth keeping around for a resume that was never asked for.
            TryDeletePartialFile(destinationPath);
            await (progress?.StopAsync() ?? Task.CompletedTask);
            await _downloadRepository.MarkCanceledAsync(download.Id, CancellationToken.None);
            download.Status = DownloadStatus.Canceled;
            DownloadChanged?.Invoke(this, download);
        }
        catch (Exception ex)
        {
            // Deliberately NOT deleting the partial file here (unlike the Cancel case above) - a
            // network drop, a killed connection, or the app closing mid-download are interruptions,
            // not a decision to discard what's already been fetched. Leaving it in place is what lets
            // QueueAsync/RetryDownloadAsync resume from here instead of starting over.
            await (progress?.StopAsync() ?? Task.CompletedTask);
            await _downloadRepository.MarkFailedAsync(download.Id, ex.Message, CancellationToken.None);
            download.Status = DownloadStatus.Failed;
            download.ErrorMessage = ex.Message;
            DownloadChanged?.Invoke(this, download);
        }
        finally
        {
            lock (_activeCancellationsLock)
            {
                _activeCancellations.Remove(download.Id);
            }
            if (holdsConcurrencySlot)
            {
                _concurrencyLimiter.Release();
            }
            DownloadFinished();
        }
    }

    // Picks up by itself after anything that interrupts the connection, such as the machine sleeping or
    // the network dropping. Each attempt resumes from the partial file where the source allows it. The
    // failure count resets whenever an attempt gets further, so only a download that has really stopped
    // moving ends up Failed.
    private async Task TransferWithRetriesAsync(
        Download download, ProfileSource sourceProfile, MediaKind kind, string streamUrl, string destinationPath,
        DownloadProgressReporter progress, CancellationToken cancellationToken)
    {
        var failures = 0;
        while (true)
        {
            var bytesBeforeAttempt = download.BytesDownloaded;
            try
            {
                await TransferOnceAsync(download, sourceProfile, kind, streamUrl, destinationPath, progress, cancellationToken);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsWorthRetrying(ex))
            {
                failures = download.BytesDownloaded > bytesBeforeAttempt ? 1 : failures + 1;
                if (failures > RetryDelays.Length)
                {
                    throw;
                }
            }

            await Task.Delay(RetryDelays[failures - 1], cancellationToken);
            progress.Rewind();
        }
    }

    private async Task TransferOnceAsync(
        Download download, ProfileSource sourceProfile, MediaKind kind, string streamUrl, string destinationPath,
        DownloadProgressReporter progress, CancellationToken cancellationToken)
    {
        if (IsHttpSource(sourceProfile.SourceType))
        {
            await DownloadHttpAsync(streamUrl, destinationPath, progress, cancellationToken);
            return;
        }

        await using var fileSystem = _fileSystemFactory.Create(sourceProfile, kind)
            ?? throw new InvalidOperationException($"'{sourceProfile.Name}' has no matching path configured for this content anymore.");

        var sourceRelativePath = kind == MediaKind.Series
            ? ExtractEpisodeSourceRelativePath(download.OriginalSourceId)
            : download.OriginalSourceId;
        await fileSystem.DownloadToFileAsync(sourceRelativePath, destinationPath, progress, cancellationToken);
    }

    // Only faults that can clear up by themselves. A 404, a missing file or a full disk would just fail
    // the same way again, several minutes later.
    private static bool IsWorthRetrying(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } status } => (int)status >= 500 || status is System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests,
        HttpRequestException or TimeoutException or System.Net.Sockets.SocketException => true,
        FileNotFoundException or DirectoryNotFoundException or PathTooLongException => false,
        IOException io => (io.HResult & 0xFFFF) is not (DiskFullWin32 or HandleDiskFullWin32 or DiskFullUnix),
        _ => false
    };

    private void DownloadStarted()
    {
        lock (_activeDownloadCountLock)
        {
            if (++_activeDownloadCount == 1)
            {
                _sleepInhibitor.SetActive(true);
            }
        }
    }

    private void DownloadFinished()
    {
        lock (_activeDownloadCountLock)
        {
            if (--_activeDownloadCount == 0)
            {
                _sleepInhibitor.SetActive(false);
            }
        }
    }

    // Called for every chunk, directly on the transfer's own thread, so the common path is just a few
    // field writes. Progress<T> isn't used because with no SynchronizationContext it queues each report
    // to the thread pool, which costs a work item per chunk and lets reports arrive out of order. The
    // lock and the backwards check are still needed for SFTP, where SSH.NET's callback can run on
    // whichever pool thread it likes.
    private sealed class DownloadProgressReporter(DownloadManager owner, Download download) : IProgress<(long BytesTransferred, long? TotalBytes)>
    {
        private readonly Lock _lock = new();
        private long _lastNotifiedTicks = Environment.TickCount64;
        private long _lastPersistedTicks = Environment.TickCount64;
        private long _highestBytes;
        private Task _pendingPersist = Task.CompletedTask;
        private bool _stopped;

        public void Report((long BytesTransferred, long? TotalBytes) value)
        {
            lock (_lock)
            {
                if (_stopped || value.BytesTransferred < _highestBytes)
                {
                    return;
                }

                _highestBytes = value.BytesTransferred;
                download.BytesDownloaded = value.BytesTransferred;
                download.TotalBytes = value.TotalBytes;

                var now = Environment.TickCount64;
                var isDone = value.TotalBytes is { } total && value.BytesTransferred >= total;
                if (!isDone && now - _lastNotifiedTicks < ProgressNotifyIntervalMs)
                {
                    return;
                }
                _lastNotifiedTicks = now;

                // One write in flight at most, so a busy database can't build up a queue of them.
                if (now - _lastPersistedTicks >= ProgressPersistIntervalMs && _pendingPersist.IsCompleted)
                {
                    _lastPersistedTicks = now;
                    _pendingPersist = owner._downloadRepository.UpdateProgressAsync(download.Id, value.BytesTransferred, value.TotalBytes, CancellationToken.None);
                }
            }

            owner.DownloadProgressChanged?.Invoke(owner, download);
        }

        // A retry that couldn't resume starts counting from 0 again, which would otherwise be ignored
        // as an out-of-order report until it caught up.
        public void Rewind()
        {
            lock (_lock)
            {
                _highestBytes = 0;
            }
        }

        // UpdateProgressAsync also writes Status = Downloading, so nothing may still be in flight when
        // the caller records the final status, or it could land afterwards and overwrite it.
        public async Task StopAsync()
        {
            Task pending;
            lock (_lock)
            {
                _stopped = true;
                pending = _pendingPersist;
            }

            try
            {
                await pending;
            }
            catch
            {
                // A missed progress write isn't worth failing the download over - the file itself is
                // what resume goes by.
            }
        }
    }

    private async Task RescanDownloadsProfileAsync(MediaKind kind, CancellationToken cancellationToken)
    {
        var downloadsProfile = await EnsureDownloadsProfileAsync(cancellationToken);
        switch (kind)
        {
            case MediaKind.Movies:
                await _movieImportService.ImportAsync(downloadsProfile, cancellationToken);
                break;
            case MediaKind.Series:
                await _seriesImportService.ImportAsync(downloadsProfile, cancellationToken);
                break;
            case MediaKind.Clips:
                await _clipImportService.ImportAsync(downloadsProfile, cancellationToken);
                break;
        }
    }

    private static MediaKind ContentTypeToKind(WatchProgressContentType contentType) => contentType switch
    {
        WatchProgressContentType.Movie => MediaKind.Movies,
        WatchProgressContentType.Clip => MediaKind.Clips,
        _ => MediaKind.Series
    };

    // An episode's OriginalSourceId is ContentKeys.ForEpisode(seriesId, episodeId) = "seriesId:episodeId"
    // - only the episode half is a real relative path on the source file system.
    private static string ExtractEpisodeSourceRelativePath(string originalSourceId)
    {
        var separatorIndex = originalSourceId.IndexOf(':');
        return separatorIndex >= 0 ? originalSourceId[(separatorIndex + 1)..] : originalSourceId;
    }

    // The other half of ContentKeys.ForEpisode("seriesId:episodeId") - null if the stored id somehow
    // isn't in that shape (shouldn't happen, but RetryDownloadAsync is working from a persisted DB
    // value, not a freshly-built one, so this is defensive rather than assumed).
    private static string? ExtractEpisodeSeriesId(string originalSourceId)
    {
        var separatorIndex = originalSourceId.IndexOf(':');
        return separatorIndex >= 0 ? originalSourceId[..separatorIndex] : null;
    }

    private static string BuildEpisodeDestinationPath(ProfileSource downloadsProfile, Series series, Episode episode, string extension)
    {
        var showFolder = SanitizeFileNameComponent(series.Name);
        var seasonFolder = $"Season {episode.Season:00}";
        var fileName = $"{showFolder} S{episode.Season:00}E{episode.EpisodeNumber:00} - {SanitizeFileNameComponent(episode.Title)}{extension}";
        return Path.Combine(downloadsProfile.LocalSeriesPath!, showFolder, seasonFolder, fileName);
    }

    // A movie/clip's Name is already exactly what FolderMediaScanner would produce from a re-parsed
    // filename with no NFO present (NFO title as-is, or "Title (Year)" with no NFO - see
    // FolderMediaScanner.FormatTitle) - using it directly as the filename is a fixed point: re-scanning
    // it recovers the identical Name, which is what DownloadPlaybackResolver matches on.
    private static string BuildMovieOrClipFileName(string name, string extension) => $"{SanitizeFileNameComponent(name)}{extension}";

    private static string SanitizeFileNameComponent(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim();
    }

    // Xtream and Jellyfin both hand out a plain HTTP URL per title (Jellyfin's static
    // /Videos/{id}/stream.{container} route serves the original file untouched, with Range support) -
    // everything else is a file system path that goes through IMediaFileSystem instead.
    private static bool IsHttpSource(ProfileSourceType sourceType) =>
        sourceType is ProfileSourceType.XtreamCodes or ProfileSourceType.Jellyfin;

    private static string GetExtension(ProfileSourceType sourceType, string sourceId, string streamUrl) =>
        IsHttpSource(sourceType)
            ? Path.GetExtension(new Uri(streamUrl).AbsolutePath)
            : Path.GetExtension(ExtractEpisodeSourceRelativePath(sourceId));

    // Resumable via a standard Range request - see SftpFileSystem/LocalFileSystem's equivalents for
    // the same "check for an existing partial file first" idea. Xtream backends vary in whether their
    // VOD endpoints honor Range at all, so this always confirms the server actually returned 206
    // Partial Content before treating the response as a continuation - a server that ignores the
    // header and sends the whole file from byte 0 (200 OK) falls back to a clean restart instead of
    // corrupting the file by appending a second full copy onto the existing partial one.
    private async Task DownloadHttpAsync(string url, string destinationPath, IProgress<(long BytesTransferred, long? TotalBytes)> progress, CancellationToken cancellationToken)
    {
        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        var startingBytes = File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;

        using var response = await SendWithStallTimeoutAsync(url, startingBytes, cancellationToken);

        // The connection dropped after the last byte was written, so there's nothing left to ask for.
        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable &&
            startingBytes > 0 && response.Content.Headers.ContentRange?.Length == startingBytes)
        {
            progress.Report((startingBytes, startingBytes));
            return;
        }

        response.EnsureSuccessStatusCode();

        var isResuming = startingBytes > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (startingBytes > 0 && !isResuming)
        {
            // Server ignored the Range header - what's arriving now is the whole file from byte 0.
            startingBytes = 0;
        }

        var totalBytes = isResuming
            ? response.Content.Headers.ContentRange?.Length ?? (response.Content.Headers.ContentLength is { } partial ? partial + startingBytes : null)
            : response.Content.Headers.ContentLength;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await DownloadFileCopier.CopyAsync(source, destinationPath, isResuming, startingBytes, totalBytes, progress, cancellationToken);
    }

    // A pooled connection that died during sleep can leave the request waiting on headers forever, the
    // same problem DownloadFileCopier.StallTimeout covers for the body.
    private async Task<HttpResponseMessage> SendWithStallTimeoutAsync(string url, long startingBytes, CancellationToken cancellationToken)
    {
        using var stallTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stallTimeout.CancelAfter(DownloadFileCopier.StallTimeout);
        try
        {
            return await SendFollowingRedirectsAsync(url, startingBytes, stallTimeout.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No response for {DownloadFileCopier.StallTimeout.TotalSeconds:0} seconds.", ex);
        }
    }

    // .NET's HttpClient auto-follows redirects EXCEPT when the redirect would downgrade the scheme
    // from https to http - a security default that silently leaves the 302 itself as the "response"
    // instead of throwing, so it only surfaces later as an opaque EnsureSuccessStatusCode failure.
    // Xtream panels commonly front an https:// URL that 302s straight to the actual media server over
    // plain http, so that default blocks every Xtream download outright. Following redirects manually
    // here (scheme changes included) is the fix; each hop re-sends the Range header since a
    // HttpRequestMessage can't be reused after being sent.
    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(string url, long startingBytes, CancellationToken cancellationToken)
    {
        const int maxRedirects = 5;
        var currentUrl = url;

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            if (startingBytes > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(startingBytes, null);
            }

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            var isRedirect = response.StatusCode is System.Net.HttpStatusCode.MovedPermanently or System.Net.HttpStatusCode.Found
                or System.Net.HttpStatusCode.SeeOther or System.Net.HttpStatusCode.TemporaryRedirect or System.Net.HttpStatusCode.PermanentRedirect;
            if (!isRedirect || response.Headers.Location is null)
            {
                return response;
            }

            if (attempt >= maxRedirects)
            {
                return response;
            }

            currentUrl = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location.ToString()
                : new Uri(new Uri(currentUrl), response.Headers.Location).ToString();
            response.Dispose();
        }
    }

    private static void TryDeletePartialFile(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup - a locked/already-gone file here shouldn't mask the real
            // failure/cancellation reason already being recorded.
        }
    }
}
