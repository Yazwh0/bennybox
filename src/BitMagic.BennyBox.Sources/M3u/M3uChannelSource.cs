using System.Net;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.M3u;

public class M3uChannelSource : IChannelSource
{
    private readonly HttpClient _httpClient;

    public M3uChannelSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public ProfileSourceType SourceType => ProfileSourceType.M3u;

    public async Task<ChannelImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profile.M3uUrl))
        {
            throw new InvalidOperationException("Profile has no M3U URL configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, profile.M3uUrl);
        ConditionalRequestHelper.ApplyConditionalHeaders(request, profile.PlaylistETag, profile.PlaylistLastModified);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new ChannelImportResult([], [], NotModified: true);
        }

        response.EnsureSuccessStatusCode();
        var etag = response.Headers.ETag?.ToString();
        var lastModified = response.Content.Headers.LastModified?.ToString("R");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        var categories = new Dictionary<string, Category>(StringComparer.OrdinalIgnoreCase);
        var channels = new List<Channel>();
        var number = 1;

        await foreach (var entry in M3uParser.ParseAsync(stream, cancellationToken))
        {
            var groupTitle = entry.Attributes.GetValueOrDefault("group-title", string.Empty).Trim();
            if (groupTitle.Length == 0)
            {
                groupTitle = "Uncategorized";
            }

            if (!categories.TryGetValue(groupTitle, out var category))
            {
                category = new Category
                {
                    Id = groupTitle,
                    ProfileId = profile.Id,
                    Name = groupTitle,
                    SortOrder = categories.Count
                };
                categories[groupTitle] = category;
            }

            var tvgId = entry.Attributes.GetValueOrDefault("tvg-id");

            // "catchup" names the scheme (default/shift/append/flussonic/...); its mere presence, or
            // catchup-days on its own, is what most players treat as "this channel has catch-up" -
            // days defaults to a week when the attribute is present but doesn't say otherwise.
            var catchupAttr = entry.Attributes.GetValueOrDefault("catchup");
            var catchupDaysAttr = entry.Attributes.GetValueOrDefault("catchup-days");
            var hasCatchup = !string.IsNullOrWhiteSpace(catchupAttr) || !string.IsNullOrWhiteSpace(catchupDaysAttr);
            var catchupDays = int.TryParse(catchupDaysAttr, out var days) ? days : (hasCatchup ? 7 : 0);

            channels.Add(new Channel
            {
                ProfileId = profile.Id,
                SourceChannelId = tvgId ?? entry.Url,
                CategoryId = category.Id,
                Name = entry.Title,
                LogoUrl = entry.Attributes.GetValueOrDefault("tvg-logo"),
                StreamUrl = entry.Url,
                TvgId = tvgId,
                Number = number++,
                HasCatchup = hasCatchup,
                CatchupDays = catchupDays
            });
        }

        return new ChannelImportResult(categories.Values.ToList(), channels, false, etag, lastModified);
    }

    // Best-effort only: unlike Xtream's single well-documented endpoint, M3U catch-up schemes vary a
    // lot by provider (default/shift/append/flussonic each build the URL differently, sometimes via a
    // separate catchup-source template attribute). This implements the "default"/"shift" convention -
    // appending ?utc={start}&lutc={now} unix timestamps to the normal stream URL - which is the most
    // common one, but hasn't been verified against a real catch-up-enabled M3U provider.
    public string? BuildTimeshiftUrl(ProfileSource profile, Channel channel, DateTime startUtc, TimeSpan duration)
    {
        if (!channel.HasCatchup)
        {
            return null;
        }

        var startUnix = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var separator = channel.StreamUrl.Contains('?') ? '&' : '?';
        return $"{channel.StreamUrl}{separator}utc={startUnix}&lutc={nowUnix}";
    }
}
