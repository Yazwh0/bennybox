using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

// Mirrors ChannelImportResult/IChannelSource - see those for the NotModified/ETag caching rationale.
// Xtream's get_series endpoint sends no caching headers (same as get_live_streams), so in practice
// NotModified is always false here; the shape is kept consistent in case that ever changes.
public record SeriesImportResult(
    IReadOnlyList<Category> Categories,
    IReadOnlyList<Series> SeriesList,
    bool NotModified = false,
    string? ETag = null,
    string? LastModified = null);

// Not every ProfileSourceType supports series (M3U playlists have no equivalent structured API) - a
// profile whose source type has no registered ISeriesSource is treated as "no series", not an error;
// see SeriesImportService.
public interface ISeriesSource
{
    ProfileSourceType SourceType { get; }

    Task<SeriesImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default);

    // Episode lists aren't part of the bulk import above - fetched on demand for one series at a
    // time, right before the user browses into it.
    Task<IReadOnlyList<Episode>> GetEpisodesAsync(ProfileSource profile, Series series, CancellationToken cancellationToken = default);
}
