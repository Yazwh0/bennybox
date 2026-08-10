namespace BitMagic.BennyBox.Core.Services;

// Persists IMetadataEnrichmentService results (both real matches AND confirmed "nothing found" misses)
// keyed by a normalized title(+year) lookup key - not by profile or item id, since the same title
// looked up from two different profiles/libraries should share one TMDb lookup rather than paying for
// it twice. Caching misses too matters just as much as caching hits: without it, a title TMDb
// genuinely has no match for would be re-queried on every single scan forever.
public interface IMetadataEnrichmentCacheRepository
{
    // Null return means "never looked up" (go query the provider) - distinct from a cached result whose
    // Found flag is false (looked up before, provider had nothing - don't query again).
    Task<CachedEnrichment?> GetAsync(string mediaType, string lookupKey, CancellationToken cancellationToken = default);
    Task SetAsync(string mediaType, string lookupKey, EnrichedMetadata? metadata, CancellationToken cancellationToken = default);
}

public record CachedEnrichment(bool Found, EnrichedMetadata? Metadata);
