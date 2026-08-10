using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Folder;

// One instance of this class is registered per folder-backed ProfileSourceType (LocalFolder, Sftp -
// see AppBootstrapper), each fixed to its own SourceType via the constructor, matching the pattern
// IMovieSource consumers already rely on (looking up the one registered instance whose SourceType
// property matches a given profile's). Both share this same implementation and FolderMediaScanner -
// only the IMediaFileSystem the factory builds differs.
public class FolderMovieSource : IMovieSource
{
    private readonly IMediaFileSystemFactory _fileSystemFactory;
    private readonly FolderMediaScanner _scanner;

    public FolderMovieSource(ProfileSourceType sourceType, IMediaFileSystemFactory fileSystemFactory, IMetadataEnrichmentService enrichmentService)
    {
        SourceType = sourceType;
        _fileSystemFactory = fileSystemFactory;
        _scanner = new FolderMediaScanner(enrichmentService);
    }

    public ProfileSourceType SourceType { get; }

    public async Task<MovieImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        // Null means this profile hasn't set a movies path (it might still have a series path - the
        // two are independent, see MediaKind) - nothing to import here, not an error. `await using`
        // is a safe no-op when fileSystem is null. Otherwise this owns the file system's connection
        // for the whole scan (see IMediaFileSystem) - one login covers the listing walk and every
        // NFO/poster read the scan does along the way.
        await using var fileSystem = _fileSystemFactory.Create(profile, MediaKind.Movies);
        if (fileSystem is null)
        {
            return new MovieImportResult([], []);
        }

        var (categories, movies) = await _scanner.ScanMoviesAsync(fileSystem, profile.Id, profile.Name, cancellationToken);
        return new MovieImportResult(categories, movies);
    }

    // Unlike Xtream (whose bulk VOD listing genuinely lacks Plot/Genre/ReleaseDate, so a live per-title
    // API call can turn up new data), ScanMoviesAsync already reads each movie's NFO once during the
    // scan and SqliteMovieRepository persists the result - movie.Plot/Genre/ReleaseDate here are
    // already final. A live re-fetch could only ever re-derive the exact same answer, at the cost of a
    // full recursive directory walk over the whole profile's movies root - so there's nothing to fetch,
    // just hand back what the scan already found.
    public Task<MovieDetails?> GetDetailsAsync(ProfileSource profile, Movie movie, CancellationToken cancellationToken = default) =>
        Task.FromResult<MovieDetails?>(new MovieDetails(movie.Plot, movie.Genre, movie.ReleaseDate, null, movie.Rating));
}
