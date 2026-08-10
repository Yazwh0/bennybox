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
    // can tick this every few microseconds. Throttled to roughly 4 updates/second, plus always on the
    // final chunk (see RunDownloadAsync).
    private static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(250);

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
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _concurrencyLimiter = new(MaxConcurrentDownloads);
    private readonly Dictionary<Guid, CancellationTokenSource> _activeCancellations = [];
    private readonly Lock _activeCancellationsLock = new();

    // Fired whenever a Download's status/progress changes - subscribed to directly by whichever
    // ViewModels care (the Downloads panel, and the row-level download button state on Movie/Episode/
    // Clip items). A plain event, not a WeakReferenceMessenger message, since this is a Core service
    // with no App-layer dependency - see the plan discussion for why.
    public event EventHandler<Download>? DownloadChanged;

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
        _httpClient = httpClient;
    }

    public async Task<string> GetDownloadsRootAsync(CancellationToken cancellationToken = default) =>
        await _settingsStore.GetAsync(DownloadsRootPathKey, cancellationToken)
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BennyBox", "Downloads");

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
        _ = Task.Run(() => RunDownloadAsync(download, sourceProfile, kind, streamUrl, buildDestinationPath, cts.Token), cts.Token);
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

        _ = Task.Run(() => RunDownloadAsync(download, sourceProfile, r.Kind, r.StreamUrl, r.BuildDestinationPath, cts.Token), cts.Token);
    }

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
        await _concurrencyLimiter.WaitAsync(cancellationToken);
        string? destinationPath = null;
        try
        {
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

            var lastReportedUtc = DateTime.UtcNow;
            var progress = new Progress<(long BytesTransferred, long? TotalBytes)>(p =>
            {
                download.BytesDownloaded = p.BytesTransferred;
                download.TotalBytes = p.TotalBytes;

                var now = DateTime.UtcNow;
                var isDone = p.TotalBytes is { } total && p.BytesTransferred >= total;
                if (!isDone && now - lastReportedUtc < ProgressThrottle)
                {
                    return;
                }
                lastReportedUtc = now;

                _ = _downloadRepository.UpdateProgressAsync(download.Id, p.BytesTransferred, p.TotalBytes, CancellationToken.None);
                DownloadChanged?.Invoke(this, download);
            });

            if (sourceProfile.SourceType == ProfileSourceType.XtreamCodes)
            {
                await DownloadHttpAsync(streamUrl, destinationPath, progress, cancellationToken);
            }
            else
            {
                await using var fileSystem = _fileSystemFactory.Create(sourceProfile, kind)
                    ?? throw new InvalidOperationException($"'{sourceProfile.Name}' has no matching path configured for this content anymore.");

                var sourceRelativePath = kind == MediaKind.Series
                    ? ExtractEpisodeSourceRelativePath(download.OriginalSourceId)
                    : download.OriginalSourceId;
                await fileSystem.DownloadToFileAsync(sourceRelativePath, destinationPath, progress, cancellationToken);
            }

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
            _concurrencyLimiter.Release();
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

    private static string GetExtension(ProfileSourceType sourceType, string sourceId, string streamUrl) =>
        sourceType == ProfileSourceType.XtreamCodes
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

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (startingBytes > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(startingBytes, null);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
        await using var destination = new FileStream(destinationPath, isResuming ? FileMode.Append : FileMode.Create, FileAccess.Write);

        var buffer = new byte[81920];
        var bytesTransferred = startingBytes;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            bytesTransferred += read;
            progress.Report((bytesTransferred, totalBytes));
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
