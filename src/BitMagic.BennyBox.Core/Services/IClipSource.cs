using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

// Mirrors MovieImportResult/IMovieSource - see those for the NotModified/ETag caching rationale.
// Reuses the Movie model as the payload shape (a clip is structurally identical - title, cover,
// stream, optional plot/genre/rating from an NFO if one happens to exist), but needs its own
// interface: content-kind dispatch picks one registered instance per ProfileSourceType (see
// MovieImportService), so a single LocalFolder/Sftp profile with both a Movies path and a Clips
// path couldn't be disambiguated through IMovieSource alone.
public record ClipImportResult(
    IReadOnlyList<Category> Categories,
    IReadOnlyList<Movie> Clips,
    bool NotModified = false,
    string? ETag = null,
    string? LastModified = null);

// Not every ProfileSourceType supports Clips (only LocalFolder/Sftp do) - a profile whose source
// type has no registered IClipSource is treated as "no clips", not an error; see ClipImportService.
public interface IClipSource
{
    ProfileSourceType SourceType { get; }

    Task<ClipImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default);

    Task<MovieDetails?> GetDetailsAsync(ProfileSource profile, Movie clip, CancellationToken cancellationToken = default);
}
