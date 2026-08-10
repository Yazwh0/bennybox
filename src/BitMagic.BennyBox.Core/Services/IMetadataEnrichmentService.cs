namespace BitMagic.BennyBox.Core.Services;

// Fills in Plot/Genre/ReleaseDate/poster for a movie or show when the local source (LocalFolder/Sftp -
// no equivalent structured API for Xtream/M3U, which already come with their own metadata pipeline)
// has none - e.g. no NFO sidecar and no local poster image. Null return means no confident match was
// found (not an error) - the caller should treat that as "nothing more to show", not retry.
public interface IMetadataEnrichmentService
{
    // False when no API key is configured - callers should skip enrichment entirely in that case
    // (including skipping the cache), not just get null matches back for every title. Without this
    // gate, scanning before an API key is ever set would poison the cache with "not found" for every
    // title, and adding a key afterward would silently never query the provider for any of them.
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    // True on an official release build (see EmbeddedTmdbApiKey), false on a local dev build or a
    // fork with no TMDB_API_KEY_ENCODED secret configured. Independent of whatever the user has typed
    // into Settings - lets the UI say "leave blank to use the bundled key" accurately instead of
    // assuming a blank Settings field means enrichment is off.
    bool HasEmbeddedFallback { get; }

    Task<EnrichedMetadata?> SearchMovieAsync(string title, int? year, CancellationToken cancellationToken = default);
    Task<EnrichedMetadata?> SearchTvShowAsync(string title, CancellationToken cancellationToken = default);
}

public record EnrichedMetadata(string? Plot, string? Genre, string? ReleaseDate, string? PosterUrl, double? Rating);
