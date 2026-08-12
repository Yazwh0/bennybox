using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using BitMagic.BennyBox.Core.Services;
using Microsoft.Extensions.Logging;

namespace BitMagic.BennyBox.Services;

// Backs channel logos shown in Live TV/Guide/Favorites with a disk cache under
// %APPDATA%\BennyBox\logos, so repeat launches don't re-download the same images. Registered as a
// singleton (see AppBootstrapper) so the in-memory de-dup below is shared app-wide, not per-page.
public class ChannelLogoCache : IChannelLogoCache
{
    // Decoded at roughly 2x the largest display size this is used at - the 110px-wide series cover
    // art shown on a series' detail page, which dwarfs the ~44px row icons elsewhere. The disk cache
    // stores the original downloaded bytes, not this decoded size, so raising this doesn't require
    // invalidating anything already cached - it just decodes sharper next time each file is read.
    private const int LogoDecodeWidth = 220;
    private const int MaxConcurrentDownloads = 6;

    // Same concurrency cap FolderMediaScanner.EnrichSeriesAsync uses for the identical TMDb TV
    // search call - a courteous rate against TMDb's API, not a technical limit of this cache.
    private const int MaxConcurrentTmdbSearches = 4;

    // Content hashes (SHA-256, over the raw downloaded bytes) of known "this isn't really a logo"
    // placeholder images, so a broken/blocked link doesn't get shown - or cached - as if it were the
    // real channel logo. Currently seeded with Imgur's generic "image removed/unavailable" graphic
    // (confirmed by fetching https://i.imgur.com/removed.png directly: 34641 bytes, image/png) -
    // Imgur serves this same file both for deleted images and, per user report, for images blocked in
    // their region (e.g. the UK), so this single signature already covers both cases we've seen.
    // Extend this set if another provider's placeholder shows up.
    private static readonly HashSet<string> KnownPlaceholderHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FAA24EC881E6040655C187A681D6DC496EB8AA41E1BD0652A180B3A40B457187",
    };

    private readonly HttpClient _httpClient;
    private readonly IMetadataEnrichmentService _enrichmentService;
    private readonly ILogger<ChannelLogoCache> _logger;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _downloadThrottle = new(MaxConcurrentDownloads);
    private readonly SemaphoreSlim _tmdbThrottle = new(MaxConcurrentTmdbSearches);

    // Keyed by URL so concurrent requests for the same logo (very common - the same channel can
    // appear in Live TV, Guide, and Favorites at once) share a single download+decode rather than
    // racing duplicate requests. GetOrAdd can rarely invoke the factory twice under contention; the
    // losing task's work is just wasted, not incorrect, so that's an acceptable simplification here.
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _memoryCache = new();

    public ChannelLogoCache(HttpClient httpClient, IMetadataEnrichmentService enrichmentService, ILogger<ChannelLogoCache> logger)
    {
        _httpClient = httpClient;
        _enrichmentService = enrichmentService;
        _logger = logger;
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BennyBox", "logos");
    }

    public Task<Bitmap?> GetLogoAsync(string? logoUrl, string? fallbackLogoUrl = null, string? tmdbFallbackTitle = null)
    {
        var primary = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl;
        var fallback = string.IsNullOrWhiteSpace(fallbackLogoUrl) ? null : fallbackLogoUrl;
        var tmdbTitle = string.IsNullOrWhiteSpace(tmdbFallbackTitle) ? null : tmdbFallbackTitle;

        return primary is null && fallback is null && tmdbTitle is null
            ? Task.FromResult<Bitmap?>(null)
            : GetWithFallbacksAsync(primary, fallback, tmdbTitle);
    }

    // Each URL is still memoized/disk-cached independently under its own key (see _memoryCache/
    // GetCachePath) - chaining fallbacks here just tries a second/third already-deduplicated lookup,
    // none of them need their own caching layer. The TMDb tier is deliberately last and lazy - it
    // only ever runs for a row that's actually on screen with no working logo of its own AND no
    // local Series match, same "only what's visible, virtualization stays cheap" reasoning as the
    // image loads themselves, rather than a batch pass over the whole (tens-of-thousands-strong)
    // channel list up front. GetTmdbPosterUrlAsync's result is never itself cached by URL here since
    // it isn't a URL - CachingMetadataEnrichmentService (see IMetadataEnrichmentService) already
    // persists the resolved poster URL (or a permanent "TMDb had nothing") per title, so a repeat
    // lookup for the same channel name - this launch or a future one - never re-hits the network.
    private async Task<Bitmap?> GetWithFallbacksAsync(string? logoUrl, string? fallbackLogoUrl, string? tmdbFallbackTitle)
    {
        if (logoUrl is not null && await GetSingleAsync(logoUrl) is { } primaryBitmap)
        {
            return primaryBitmap;
        }

        if (fallbackLogoUrl is not null && await GetSingleAsync(fallbackLogoUrl) is { } fallbackBitmap)
        {
            return fallbackBitmap;
        }

        if (tmdbFallbackTitle is null)
        {
            return null;
        }

        var posterUrl = await GetTmdbPosterUrlAsync(tmdbFallbackTitle);
        return posterUrl is null ? null : await GetSingleAsync(posterUrl);
    }

    private async Task<string?> GetTmdbPosterUrlAsync(string title)
    {
        if (!await _enrichmentService.IsAvailableAsync())
        {
            // No TMDb key configured (see IMetadataEnrichmentService.IsAvailableAsync) - skip
            // entirely rather than calling SearchTvShowAsync, which would otherwise permanently
            // cache a "not found" for every title while no key is set (see
            // CachingMetadataEnrichmentService's own warning about this).
            return null;
        }

        await _tmdbThrottle.WaitAsync();
        try
        {
            var enriched = await _enrichmentService.SearchTvShowAsync(title);
            return enriched?.PosterUrl;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "TMDb TV search failed for {Title}", title);
            return null;
        }
        finally
        {
            _tmdbThrottle.Release();
        }
    }

    private Task<Bitmap?> GetSingleAsync(string url) => _memoryCache.GetOrAdd(url, LoadAsync);

    private Task<Bitmap?> LoadAsync(string url) =>
        TryGetLocalPath(url, out var localPath) ? LoadLocalAsync(localPath) : LoadHttpAsync(url);

    // LocalFolder/Sftp-sourced posters (see FolderMediaScanner) come through as file:// URIs, not
    // http(s) - straight disk read, no download/placeholder-detection/disk-cache machinery needed
    // since the source is already local.
    private async Task<Bitmap?> LoadLocalAsync(string localPath)
    {
        try
        {
            if (!File.Exists(localPath))
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(localPath);
            using var stream = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(stream, LogoDecodeWidth);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load local channel logo from {Path}", localPath);
            return null;
        }
    }

    private static bool TryGetLocalPath(string url, out string localPath)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            localPath = uri.LocalPath;
            return true;
        }

        localPath = string.Empty;
        return false;
    }

    private async Task<Bitmap?> LoadHttpAsync(string url)
    {
        var cachePath = GetCachePath(url);

        try
        {
            if (File.Exists(cachePath))
            {
                var cachedBytes = await File.ReadAllBytesAsync(cachePath);
                if (IsKnownPlaceholder(cachedBytes))
                {
                    // Was cached before this placeholder's hash was known (or before this check
                    // existed) - clean it up so a future retry has a chance to pull the real logo.
                    File.Delete(cachePath);
                    return null;
                }

                using var cached = new MemoryStream(cachedBytes);
                return Bitmap.DecodeToWidth(cached, LogoDecodeWidth);
            }

            await _downloadThrottle.WaitAsync();
            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);

                if (IsKnownPlaceholder(bytes))
                {
                    _logger.LogDebug("Logo at {Url} is a known placeholder/unavailable image - not caching", url);
                    return null;
                }

                Directory.CreateDirectory(_cacheDirectory);
                await File.WriteAllBytesAsync(cachePath, bytes);

                using var downloaded = new MemoryStream(bytes);
                return Bitmap.DecodeToWidth(downloaded, LogoDecodeWidth);
            }
            finally
            {
                _downloadThrottle.Release();
            }
        }
        catch (Exception ex)
        {
            // Broken/expired logo URLs are common in IPTV playlists - log quietly and just show no
            // logo rather than surfacing an error for what's a purely cosmetic feature.
            _logger.LogDebug(ex, "Failed to load channel logo from {Url}", url);
            return null;
        }
    }

    private static bool IsKnownPlaceholder(byte[] bytes) =>
        KnownPlaceholderHashes.Contains(Convert.ToHexString(SHA256.HashData(bytes)));

    private string GetCachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(_cacheDirectory, hash);
    }
}
