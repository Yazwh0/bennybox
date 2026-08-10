using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Folder;

// See FolderMovieSource - same pattern, one instance per folder-backed ProfileSourceType.
public class FolderSeriesSource : ISeriesSource
{
    private readonly IMediaFileSystemFactory _fileSystemFactory;
    private readonly IEpisodeCacheRepository _episodeCache;
    private readonly FolderMediaScanner _scanner;

    public FolderSeriesSource(
        ProfileSourceType sourceType,
        IMediaFileSystemFactory fileSystemFactory,
        IEpisodeCacheRepository episodeCache,
        IMetadataEnrichmentService enrichmentService)
    {
        SourceType = sourceType;
        _fileSystemFactory = fileSystemFactory;
        _episodeCache = episodeCache;
        _scanner = new FolderMediaScanner(enrichmentService);
    }

    public ProfileSourceType SourceType { get; }

    public async Task<SeriesImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        // This is the one place a rescan of this profile happens (Settings' per-profile Refresh, or
        // Movies/Series' own Refresh - both funnel through here) - see IEpisodeCacheRepository for why
        // this is a full clear, not just a prune of shows that disappeared.
        await _episodeCache.ClearProfileAsync(profile.Id, cancellationToken);

        // Null means this profile hasn't set a series path (it might still have a movies path - the
        // two are independent, see MediaKind) - nothing to import here, not an error. `await using`
        // is a safe no-op when fileSystem is null. Otherwise this owns the file system's connection
        // for the whole scan (see IMediaFileSystem) - one login covers the listing walk and every
        // NFO/poster read across every show, not one login per show or per file.
        await using var fileSystem = _fileSystemFactory.Create(profile, MediaKind.Series);
        if (fileSystem is null)
        {
            return new SeriesImportResult([], []);
        }

        var (categories, seriesList, episodesBySeriesId) = await _scanner.ScanSeriesAsync(fileSystem, profile.Id, profile.Name, cancellationToken);

        // Persist every show's episodes now, from the same walk that already found them (see
        // FolderMediaScanner.ScanSeriesAsync) - no further SFTP access, so Refresh genuinely means
        // "everything is up to date" rather than "the show list is current but episodes still need
        // fetching the first time each show is opened."
        foreach (var (sourceSeriesId, episodes) in episodesBySeriesId)
        {
            await _episodeCache.ReplaceCachedEpisodesAsync(profile.Id, sourceSeriesId, episodes, cancellationToken);
        }

        return new SeriesImportResult(categories, seriesList);
    }

    public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(ProfileSource profile, Series series, CancellationToken cancellationToken = default)
    {
        var cached = await _episodeCache.GetCachedEpisodesAsync(profile.Id, series.SourceSeriesId, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        await using var fileSystem = _fileSystemFactory.Create(profile, MediaKind.Series);
        if (fileSystem is null)
        {
            return [];
        }

        var episodes = await _scanner.GetEpisodesAsync(fileSystem, series, cancellationToken);
        await _episodeCache.ReplaceCachedEpisodesAsync(profile.Id, series.SourceSeriesId, episodes, cancellationToken);
        return episodes;
    }
}
