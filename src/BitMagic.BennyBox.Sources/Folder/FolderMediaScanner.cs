using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Folder;

// Shared by both LocalFolder and Sftp profiles (see IMediaFileSystem) - walks one root via the file
// system abstraction and produces the same domain objects Xtream/M3U sources produce, so nothing
// downstream (repositories, view models) needs to know or care where the data came from.
//
// Metadata priority, per the plan discussion: NFO sidecar first (the real, structured metadata a
// Kodi/Jellyfin/Plex-scanned library already has), filename/folder parsing only as a fallback
// identifier when no NFO is present - never the other way around.
//
// Category grouping: everything from one profile lands in a single synthetic category named after
// the profile itself (not auto-detected from folder structure) - see the plan discussion on whether
// local categories should merge with an Xtream provider's category names; this deliberately doesn't
// attempt that automatically.
public class FolderMediaScanner
{
    private static readonly string[] VideoExtensions = [".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts", ".m2ts"];
    private const string CategoryId = "local";

    // Capped, not unbounded - a library with a hundred+ titles all missing local metadata would
    // otherwise fire that many concurrent requests at TMDb at once.
    private const int EnrichmentConcurrency = 4;

    private readonly IMetadataEnrichmentService? _enrichmentService;

    // Null (the parameterless default) means no enrichment provider is wired up at all - both
    // ScanMoviesAsync/ScanSeriesAsync treat that identically to IsAvailableAsync() == false.
    public FolderMediaScanner(IMetadataEnrichmentService? enrichmentService = null)
    {
        _enrichmentService = enrichmentService;
    }

    public async Task<(IReadOnlyList<Category> Categories, IReadOnlyList<Movie> Movies)> ScanMoviesAsync(
        IMediaFileSystem fileSystem, Guid profileId, string categoryName, CancellationToken cancellationToken = default)
    {
        var movies = await ScanMovieUnitsAsync(fileSystem, profileId, cancellationToken);
        await EnrichMoviesAsync(movies, cancellationToken);

        return ([new Category { Id = CategoryId, ProfileId = profileId, Name = categoryName, SortOrder = 0 }], movies);
    }

    // One-off/uncategorized media (sports broadcasts, TV specials) - see IClipRepository. Reuses the
    // exact same "movie unit" grouping/NFO/poster logic as ScanMoviesAsync (a clip is scanned
    // identically to a movie - single video file, optional NFO/poster sidecar), but deliberately never
    // calls EnrichMoviesAsync - TMDb's movie search isn't meant to resolve a sports broadcast or TV
    // special, and a wrong match is worse than no match at all. See the plan discussion on why this is
    // a separate section rather than a "skip enrichment" toggle on Movies.
    public async Task<(IReadOnlyList<Category> Categories, IReadOnlyList<Movie> Clips)> ScanClipsAsync(
        IMediaFileSystem fileSystem, Guid profileId, string categoryName, CancellationToken cancellationToken = default)
    {
        var clips = await ScanMovieUnitsAsync(fileSystem, profileId, cancellationToken);

        return ([new Category { Id = CategoryId, ProfileId = profileId, Name = categoryName, SortOrder = 0 }], clips);
    }

    // Shared by ScanMoviesAsync/ScanClipsAsync - a movie/clip "unit" is either a top-level subfolder
    // (all its files - video/nfo/poster - group together) or a flat file directly under the root (see
    // GetUnitKey). Returns un-enriched Movie objects; enrichment (if any) is the caller's decision.
    private async Task<List<Movie>> ScanMovieUnitsAsync(IMediaFileSystem fileSystem, Guid profileId, CancellationToken cancellationToken)
    {
        var allFiles = await CollectAsync(fileSystem, cancellationToken);
        var videoFiles = allFiles.Where(f => IsVideoFile(f.RelativePath)).ToList();

        var movies = new List<Movie>();
        foreach (var unit in videoFiles.GroupBy(f => GetUnitKey(f.RelativePath)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Largest video file in the unit is treated as the movie itself - trailers/sample clips
            // that sometimes ship alongside a rip are reliably much smaller than the feature.
            var video = unit.OrderByDescending(f => f.Length).First();
            var siblings = allFiles.Where(f => GetUnitKey(f.RelativePath) == unit.Key).ToList();
            var nfoFile = siblings.FirstOrDefault(f => IsNfoFile(f.RelativePath));

            NfoParser.MovieNfo? nfo = null;
            if (nfoFile is not null)
            {
                using var stream = await fileSystem.OpenReadAsync(nfoFile.RelativePath, cancellationToken);
                nfo = NfoParser.TryParseMovie(stream);
            }

            var guess = MediaFilenameParser.ParseMovieName(FileNameWithoutExtension(video.RelativePath));
            var posterPath = FindPoster(siblings, FileNameWithoutExtension(video.RelativePath));

            movies.Add(new Movie
            {
                ProfileId = profileId,
                SourceMovieId = video.RelativePath,
                CategoryId = CategoryId,
                Name = nfo?.Title ?? FormatTitle(guess.Title, guess.Year),
                CoverUrl = posterPath is null ? null : fileSystem.BuildStreamUrl(posterPath),
                StreamUrl = fileSystem.BuildStreamUrl(video.RelativePath),
                Plot = nfo?.Plot,
                Genre = nfo?.Genre,
                ReleaseDate = nfo?.ReleaseDate,
                Rating = nfo?.Rating
            });
        }

        return movies;
    }

    // Fills gaps left by a missing/incomplete NFO from TMDb (see IMetadataEnrichmentService) - only for
    // movies still missing a plot or a poster after the local scan above, and only if a provider is
    // actually configured (IsAvailableAsync gates this before anything touches the cache - see its own
    // doc comment for why that matters). Runs with bounded concurrency since this can be dozens of
    // titles on a library with no local metadata at all.
    private async Task EnrichMoviesAsync(List<Movie> movies, CancellationToken cancellationToken)
    {
        if (_enrichmentService is null || !await _enrichmentService.IsAvailableAsync(cancellationToken))
        {
            return;
        }

        var candidates = movies.Where(m => m.Plot is null || m.CoverUrl is null).ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        await RunWithConcurrencyLimitAsync(candidates.Select(movie => (Func<Task>)(async () =>
        {
            EnrichedMetadata? enriched;
            try
            {
                var guess = MediaFilenameParser.ParseMovieName(FileNameWithoutExtension(movie.SourceMovieId));
                enriched = await _enrichmentService.SearchMovieAsync(guess.Title, guess.Year, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A provider-side failure (bad key, rate limited, network) for one title shouldn't
                // abort enrichment for the rest of the batch - this movie just keeps whatever local
                // data (possibly none) it already had, same as if no provider were configured at all.
                return;
            }

            if (enriched is null)
            {
                return;
            }

            movie.Plot ??= enriched.Plot;
            movie.Genre ??= enriched.Genre;
            movie.ReleaseDate ??= enriched.ReleaseDate;
            movie.CoverUrl ??= enriched.PosterUrl;
            movie.Rating ??= enriched.Rating;
        })), EnrichmentConcurrency, cancellationToken);
    }

    // EpisodesBySeriesId is built from the SAME file listing this method already collects to build the
    // show list - a naive "call GetEpisodesAsync once per show afterward" approach would instead
    // re-walk the ENTIRE series root from scratch for every single show (GetEpisodesAsync has no
    // pre-collected list to reuse when called on its own), which for a profile with dozens of shows
    // would mean dozens of redundant full SFTP tree walks. Doing it here means Refresh (see
    // FolderSeriesSource.ImportAsync) can eagerly cache every show's episodes for the cost of ONE walk
    // total, not one plus one-per-show.
    public async Task<(IReadOnlyList<Category> Categories, IReadOnlyList<Series> SeriesList, IReadOnlyDictionary<string, IReadOnlyList<Episode>> EpisodesBySeriesId)> ScanSeriesAsync(
        IMediaFileSystem fileSystem, Guid profileId, string categoryName, CancellationToken cancellationToken = default)
    {
        var allFiles = await CollectAsync(fileSystem, cancellationToken);
        var videoFiles = allFiles.Where(f => IsVideoFile(f.RelativePath) && HasTopLevelFolder(f.RelativePath)).ToList();

        // One top-level folder is a "release", not necessarily a whole show - see
        // MediaFilenameParser.ParseShowFolderName. Group releases whose resolved title matches back
        // into a single show, so e.g. "Fresh.Meat.S01...", "...S02...", "...S03...", "...S04..." become
        // one series with four seasons' worth of episodes, not four separate series.
        var releaseFolders = ResolveReleaseFolders(videoFiles);

        var seriesList = new List<Series>();
        var episodesBySeriesId = new Dictionary<string, IReadOnlyList<Episode>>();
        foreach (var showGroup in releaseFolders.GroupBy(x => x.Title, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folders = showGroup.ToList();
            var folderSet = new HashSet<string>(folders.Select(x => x.Folder), StringComparer.OrdinalIgnoreCase);
            var showFiles = allFiles.Where(f => folderSet.Contains(GetTopLevelSegment(f.RelativePath))).ToList();
            var nfoFile = showFiles.FirstOrDefault(f => IsNfoFile(f.RelativePath) && GetDepth(f.RelativePath) == 1);

            NfoParser.TvShowNfo? nfo = null;
            if (nfoFile is not null)
            {
                using var stream = await fileSystem.OpenReadAsync(nfoFile.RelativePath, cancellationToken);
                nfo = NfoParser.TryParseTvShow(stream);
            }

            var posterPath = FindPoster(showFiles.Where(f => GetDepth(f.RelativePath) == 1).ToList(), basenameToMatch: null);
            var sourceSeriesId = NormalizeShowKey(showGroup.Key);

            seriesList.Add(new Series
            {
                ProfileId = profileId,
                // A normalized title, not a folder path - a show can now span several release folders,
                // so there's no single one to key off. Re-derived the same way in GetEpisodesAsync.
                SourceSeriesId = sourceSeriesId,
                CategoryId = CategoryId,
                Name = nfo?.Title ?? showGroup.Key,
                CoverUrl = posterPath is null ? null : fileSystem.BuildStreamUrl(posterPath),
                Plot = nfo?.Plot,
                Genre = nfo?.Genre,
                Rating = nfo?.Rating
            });

            episodesBySeriesId[sourceSeriesId] = await BuildEpisodesAsync(fileSystem, showFiles, folders, cancellationToken);
        }

        await EnrichSeriesAsync(seriesList, cancellationToken);

        return ([new Category { Id = CategoryId, ProfileId = profileId, Name = categoryName, SortOrder = 0 }], seriesList, episodesBySeriesId);
    }

    // See EnrichMoviesAsync - same rationale, TV search instead of movie search.
    private async Task EnrichSeriesAsync(List<Series> seriesList, CancellationToken cancellationToken)
    {
        if (_enrichmentService is null || !await _enrichmentService.IsAvailableAsync(cancellationToken))
        {
            return;
        }

        var candidates = seriesList.Where(s => s.Plot is null || s.CoverUrl is null).ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        await RunWithConcurrencyLimitAsync(candidates.Select(series => (Func<Task>)(async () =>
        {
            EnrichedMetadata? enriched;
            try
            {
                enriched = await _enrichmentService.SearchTvShowAsync(series.Name, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // See EnrichMoviesAsync's equivalent catch for why this is swallowed per-item.
                return;
            }

            if (enriched is null)
            {
                return;
            }

            series.Plot ??= enriched.Plot;
            series.Genre ??= enriched.Genre;
            series.ReleaseDate ??= enriched.ReleaseDate;
            series.CoverUrl ??= enriched.PosterUrl;
            series.Rating ??= enriched.Rating;
        })), EnrichmentConcurrency, cancellationToken);
    }

    private static async Task RunWithConcurrencyLimitAsync(IEnumerable<Func<Task>> actions, int maxConcurrency, CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = actions.Select(async action =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await action();
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static string NormalizeShowKey(string title) => title.Trim().ToLowerInvariant();

    // A top-level "release" folder's name doesn't always carry the show's title - some libraries name
    // a season folder as a bare "Season NN" with nothing else, relying entirely on the episode
    // filenames inside it to carry the show name (e.g. "Season 01/Shooting Stars Season 01 Episode
    // 04.avi"). When that's detected, peek at one video file inside the folder and try to recover a
    // real title from IT instead - cheap (just string parsing, no extra I/O, the files are already
    // enumerated) and lets folders like "Season 01".."Season 07" for the same show still group
    // together under their real name rather than each showing up as a separate "Season NN" series.
    // Shared by ScanSeriesAsync and GetEpisodesAsync so both resolve a folder to the same show.
    private static List<(string Folder, string Title, int? Season)> ResolveReleaseFolders(List<MediaFileEntry> videoFiles)
    {
        var resolved = new List<(string Folder, string Title, int? Season)>();
        foreach (var folder in videoFiles.Select(f => GetTopLevelSegment(f.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var guess = MediaFilenameParser.ParseShowFolderName(folder);
            var title = guess.Title;

            if (MediaFilenameParser.IsBareSeasonLabel(title))
            {
                var sampleVideo = videoFiles.FirstOrDefault(f => GetTopLevelSegment(f.RelativePath).Equals(folder, StringComparison.OrdinalIgnoreCase));
                if (sampleVideo is not null)
                {
                    var fileGuess = MediaFilenameParser.ParseShowFolderName(FileNameWithoutExtension(sampleVideo.RelativePath));
                    if (!string.IsNullOrWhiteSpace(fileGuess.Title) && !MediaFilenameParser.IsBareSeasonLabel(fileGuess.Title))
                    {
                        title = fileGuess.Title;
                    }
                }
            }

            resolved.Add((folder, title, guess.Season));
        }

        return resolved;
    }

    // Fallback path for a cache miss between refreshes (see FolderSeriesSource.GetEpisodesAsync) -
    // ScanSeriesAsync eagerly builds and caches every show's episodes already, so this should only
    // run for a show that's somehow missing its cache entry. Has no pre-collected file list to reuse,
    // so - unlike ScanSeriesAsync's internal per-show calls - this does its own full walk.
    public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(
        IMediaFileSystem fileSystem, Series series, CancellationToken cancellationToken = default)
    {
        var allFiles = await CollectAsync(fileSystem, cancellationToken);
        var videoFiles = allFiles.Where(f => IsVideoFile(f.RelativePath) && HasTopLevelFolder(f.RelativePath)).ToList();

        // series.SourceSeriesId is the normalized show title (see ScanSeriesAsync), not a folder path -
        // re-derive which release folder(s) belong to this show the same way scanning grouped them,
        // since a show can be spread across several season-specific folders.
        var matchingFolders = ResolveReleaseFolders(videoFiles)
            .Where(x => NormalizeShowKey(x.Title) == series.SourceSeriesId)
            .ToList();

        var folderSet = new HashSet<string>(matchingFolders.Select(x => x.Folder), StringComparer.OrdinalIgnoreCase);
        var showFiles = allFiles.Where(f => folderSet.Contains(GetTopLevelSegment(f.RelativePath))).ToList();

        return await BuildEpisodesAsync(fileSystem, showFiles, matchingFolders, cancellationToken);
    }

    // Shared by ScanSeriesAsync (which already has this show's files on hand from its own walk) and
    // GetEpisodesAsync's fallback path above. showFiles is scoped to just this show's release
    // folder(s), not the whole library - keeps this cheap even when called once per show in a loop.
    private async Task<IReadOnlyList<Episode>> BuildEpisodesAsync(
        IMediaFileSystem fileSystem,
        List<MediaFileEntry> showFiles,
        List<(string Folder, string Title, int? Season)> matchingFolders,
        CancellationToken cancellationToken)
    {
        // FolderMatchesSeason tracks whether this episode's resolved season actually matches the
        // release folder it was found in - a source folder can be mislabeled (e.g. a stray file whose
        // own SxxEyy tag disagrees with the folder's own season, like a "1923.S01..." folder that
        // turned out to contain a misplaced S02E01 file) - used below to pick the genuinely-correct
        // copy when the same Season+EpisodeNumber turns up twice.
        var candidates = new List<(Episode Episode, bool FolderMatchesSeason)>();
        foreach (var (folder, _, folderSeason) in matchingFolders)
        {
            var folderVideos = showFiles.Where(f => IsVideoFile(f.RelativePath) && GetTopLevelSegment(f.RelativePath).Equals(folder, StringComparison.OrdinalIgnoreCase));
            foreach (var video in folderVideos)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nfoRelativePath = ChangeExtension(video.RelativePath, ".nfo");
                NfoParser.EpisodeNfo? nfo = null;
                if (showFiles.Any(f => string.Equals(f.RelativePath, nfoRelativePath, StringComparison.OrdinalIgnoreCase)))
                {
                    using var stream = await fileSystem.OpenReadAsync(nfoRelativePath, cancellationToken);
                    nfo = NfoParser.TryParseEpisode(stream);
                }

                // The release folder's own season (parsed from its name, e.g. "S04") is the fallback -
                // a further "Season NN" subfolder nested inside it (unusual but not unheard of) still
                // wins if present, and the episode filename's own SxxEyy tag (if any) wins over both.
                var seasonFolder = GetImmediateParentFolder(video.RelativePath, folder);
                var guess = MediaFilenameParser.ParseEpisodeName(FileNameWithoutExtension(video.RelativePath), seasonFolder);
                var season = guess.Season ?? folderSeason ?? 0;

                candidates.Add((new Episode
                {
                    SourceEpisodeId = video.RelativePath,
                    Title = nfo?.Title ?? guess.Title,
                    Season = season,
                    EpisodeNumber = guess.Episode ?? 0,
                    PlotSummary = nfo?.Plot,
                    StreamUrl = fileSystem.BuildStreamUrl(video.RelativePath)
                }, folderSeason == season));
            }
        }

        var episodes = new List<Episode>();
        foreach (var group in candidates.GroupBy(c => (c.Episode.Season, c.Episode.EpisodeNumber)))
        {
            if (group.Key.EpisodeNumber == 0)
            {
                // Episode number couldn't be determined at all - these aren't confirmed duplicates of
                // each other (e.g. several unlabeled "Special.mkv"-style files would otherwise
                // wrongly collapse into one), so every one of them is kept.
                episodes.AddRange(group.Select(c => c.Episode));
                continue;
            }

            episodes.Add(group.OrderByDescending(c => c.FolderMatchesSeason).First().Episode);
        }

        return episodes.OrderBy(e => e.Season).ThenBy(e => e.EpisodeNumber).ToList();
    }

    private static async Task<List<MediaFileEntry>> CollectAsync(IMediaFileSystem fileSystem, CancellationToken cancellationToken)
    {
        var files = new List<MediaFileEntry>();
        await foreach (var entry in fileSystem.EnumerateFilesAsync(cancellationToken))
        {
            files.Add(entry);
        }
        return files;
    }

    private static bool IsVideoFile(string relativePath) => VideoExtensions.Contains(Path.GetExtension(relativePath), StringComparer.OrdinalIgnoreCase);

    private static bool IsNfoFile(string relativePath) => string.Equals(Path.GetExtension(relativePath), ".nfo", StringComparison.OrdinalIgnoreCase);

    private static bool HasTopLevelFolder(string relativePath) => relativePath.Replace('\\', '/').Contains('/');

    // A movie "unit" is either a top-level subfolder (all its files - video/nfo/poster - group
    // together) or, for a flat file directly under the root, the file's own basename (so
    // "Movie (2020).mkv" groups with a sibling "Movie (2020).nfo"/"Movie (2020).jpg").
    private static string GetUnitKey(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var slash = normalized.IndexOf('/');
        return slash >= 0 ? normalized[..slash] : FileNameWithoutExtension(normalized);
    }

    private static string GetTopLevelSegment(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var slash = normalized.IndexOf('/');
        return slash >= 0 ? normalized[..slash] : normalized;
    }

    private static int GetDepth(string relativePath) => relativePath.Replace('\\', '/').Count(c => c == '/');

    private static string FileNameWithoutExtension(string relativePath) => Path.GetFileNameWithoutExtension(relativePath.Replace('\\', '/'));

    private static string ChangeExtension(string relativePath, string newExtension) =>
        relativePath[..^Path.GetExtension(relativePath).Length] + newExtension;

    // The folder directly containing an episode's video file, relative to the show root - "" if the
    // episode sits right under the show folder with no season subfolder. Used as the "Season NN"
    // hint when the episode filename itself doesn't carry an SxxEyy tag.
    private static string? GetImmediateParentFolder(string episodeRelativePath, string showFolder)
    {
        var normalized = episodeRelativePath.Replace('\\', '/');
        var dir = normalized[..normalized.LastIndexOf('/')];
        return string.Equals(dir, showFolder, StringComparison.OrdinalIgnoreCase) ? null : Path.GetFileName(dir);
    }

    private static readonly string[] PosterNames = ["poster.jpg", "poster.png", "folder.jpg", "folder.png"];

    // basenameToMatch enables the flat-file convention ("Movie (2020).jpg" sitting next to
    // "Movie (2020).mkv") - pass null when there's no single video file to match against (e.g. a
    // show folder, where only the well-known poster/folder names make sense).
    private static string? FindPoster(List<MediaFileEntry> siblings, string? basenameToMatch)
    {
        var byWellKnownName = siblings.FirstOrDefault(f => PosterNames.Contains(Path.GetFileName(f.RelativePath), StringComparer.OrdinalIgnoreCase));
        if (byWellKnownName is not null)
        {
            return byWellKnownName.RelativePath;
        }

        if (basenameToMatch is null)
        {
            return null;
        }

        var byBasename = siblings.FirstOrDefault(f =>
            string.Equals(FileNameWithoutExtension(f.RelativePath), basenameToMatch, StringComparison.OrdinalIgnoreCase) &&
            (Path.GetExtension(f.RelativePath).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(f.RelativePath).Equals(".png", StringComparison.OrdinalIgnoreCase)));

        return byBasename?.RelativePath;
    }

    private static string FormatTitle(string title, int? year) => year is null ? title : $"{title} ({year})";
}
