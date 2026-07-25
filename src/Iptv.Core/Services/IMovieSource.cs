using Iptv.Core.Models;

namespace Iptv.Core.Services;

// Mirrors SeriesImportResult/ISeriesSource - see those for the NotModified/ETag caching rationale.
public record MovieImportResult(
    IReadOnlyList<Category> Categories,
    IReadOnlyList<Movie> Movies,
    bool NotModified = false,
    string? ETag = null,
    string? LastModified = null);

// Extended per-title details (plot, genre, etc.) - not part of the bulk import above since Xtream's
// bulk VOD listing doesn't include them (only per-title get_vod_info does, and fetching that for
// every movie in a large library up front isn't worth it) - fetched on demand, one title at a time,
// right before the user views it. Mirrors ISeriesSource.GetEpisodesAsync's on-demand rationale.
public record MovieDetails(string? Plot, string? Genre, string? ReleaseDate, string? Duration, double? Rating);

// Not every ProfileSourceType supports VOD movies (M3U playlists have no equivalent structured API) -
// a profile whose source type has no registered IMovieSource is treated as "no movies", not an error;
// see MovieImportService.
public interface IMovieSource
{
    ProfileSourceType SourceType { get; }

    Task<MovieImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default);

    Task<MovieDetails?> GetDetailsAsync(ProfileSource profile, Movie movie, CancellationToken cancellationToken = default);
}
