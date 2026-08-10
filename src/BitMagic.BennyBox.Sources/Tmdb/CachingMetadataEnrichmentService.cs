using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Tmdb;

// Wraps the real provider (TmdbMetadataEnrichmentService) with a cache lookup, so a title is only ever
// sent to the provider once - including "provider had nothing" results, which matter just as much as
// hits (see IMetadataEnrichmentCacheRepository). IsAvailableAsync delegates straight through and is
// checked by callers BEFORE reaching here for a given title - that's what keeps an unconfigured API
// key from ever writing "not found" into the cache in the first place.
public class CachingMetadataEnrichmentService : IMetadataEnrichmentService
{
    private readonly IMetadataEnrichmentService _inner;
    private readonly IMetadataEnrichmentCacheRepository _cache;

    public CachingMetadataEnrichmentService(IMetadataEnrichmentService inner, IMetadataEnrichmentCacheRepository cache)
    {
        _inner = inner;
        _cache = cache;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => _inner.IsAvailableAsync(cancellationToken);

    public bool HasEmbeddedFallback => _inner.HasEmbeddedFallback;

    public async Task<EnrichedMetadata?> SearchMovieAsync(string title, int? year, CancellationToken cancellationToken = default)
    {
        var key = NormalizeKey(title, year);
        var cached = await _cache.GetAsync("movie", key, cancellationToken);
        if (cached is not null)
        {
            return cached.Metadata;
        }

        var result = await _inner.SearchMovieAsync(title, year, cancellationToken);
        await _cache.SetAsync("movie", key, result, cancellationToken);
        return result;
    }

    public async Task<EnrichedMetadata?> SearchTvShowAsync(string title, CancellationToken cancellationToken = default)
    {
        var key = NormalizeKey(title, null);
        var cached = await _cache.GetAsync("tv", key, cancellationToken);
        if (cached is not null)
        {
            return cached.Metadata;
        }

        var result = await _inner.SearchTvShowAsync(title, cancellationToken);
        await _cache.SetAsync("tv", key, result, cancellationToken);
        return result;
    }

    private static string NormalizeKey(string title, int? year) =>
        year is null ? title.Trim().ToLowerInvariant() : $"{title.Trim().ToLowerInvariant()}|{year}";
}
