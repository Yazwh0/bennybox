using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Folder;

// See FolderMovieSource - same pattern, one instance per folder-backed ProfileSourceType. Unlike
// FolderMovieSource/FolderSeriesSource, this is always constructed with no IMetadataEnrichmentService
// at all (see AppBootstrapper) - Clips never call TMDb, by design (see FolderMediaScanner.ScanClipsAsync).
public class FolderClipSource : IClipSource
{
    private readonly IMediaFileSystemFactory _fileSystemFactory;
    private readonly FolderMediaScanner _scanner;

    public FolderClipSource(ProfileSourceType sourceType, IMediaFileSystemFactory fileSystemFactory)
    {
        SourceType = sourceType;
        _fileSystemFactory = fileSystemFactory;
        _scanner = new FolderMediaScanner();
    }

    public ProfileSourceType SourceType { get; }

    public async Task<ClipImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        // Null means this profile hasn't set a clips path - nothing to import here, not an error.
        // `await using` is a safe no-op when fileSystem is null. Otherwise this owns the file system's
        // connection for the whole scan (see IMediaFileSystem).
        await using var fileSystem = _fileSystemFactory.Create(profile, MediaKind.Clips);
        if (fileSystem is null)
        {
            return new ClipImportResult([], []);
        }

        var (categories, clips) = await _scanner.ScanClipsAsync(fileSystem, profile.Id, profile.Name, cancellationToken);
        return new ClipImportResult(categories, clips);
    }

    // See FolderMovieSource.GetDetailsAsync - same rationale, the scan already read everything a clip
    // will ever have (no TMDb to fall back to for further detail).
    public Task<MovieDetails?> GetDetailsAsync(ProfileSource profile, Movie clip, CancellationToken cancellationToken = default) =>
        Task.FromResult<MovieDetails?>(new MovieDetails(clip.Plot, clip.Genre, clip.ReleaseDate, null, clip.Rating));
}
