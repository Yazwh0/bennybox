using Iptv.Core.Models;

namespace Iptv.Core.Services;

// NotModified/ETag/LastModified support HTTP conditional requests (If-None-Match/If-Modified-Since):
// when a source's server confirms nothing changed since the last fetch, callers skip re-parsing and
// re-writing the DB entirely. Sources that can't support this (e.g. a dynamic API with no caching
// headers) simply always return NotModified: false with null ETag/LastModified.
public record ChannelImportResult(
    IReadOnlyList<Category> Categories,
    IReadOnlyList<Channel> Channels,
    bool NotModified = false,
    string? ETag = null,
    string? LastModified = null);

public interface IChannelSource
{
    ProfileSourceType SourceType { get; }

    Task<ChannelImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default);

    // Pure string-building (credentials are already known, no network round trip) - null if this
    // source type doesn't support catch-up, or the channel itself doesn't (see Channel.HasCatchup).
    string? BuildTimeshiftUrl(ProfileSource profile, Channel channel, DateTime startUtc, TimeSpan duration);
}
