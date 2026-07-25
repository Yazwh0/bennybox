using System.Net;
using Iptv.Core.Models;
using Iptv.Core.Services;

namespace Iptv.Sources.M3u;

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

            channels.Add(new Channel
            {
                ProfileId = profile.Id,
                SourceChannelId = tvgId ?? entry.Url,
                CategoryId = category.Id,
                Name = entry.Title,
                LogoUrl = entry.Attributes.GetValueOrDefault("tvg-logo"),
                StreamUrl = entry.Url,
                TvgId = tvgId,
                Number = number++
            });
        }

        return new ChannelImportResult(categories.Values.ToList(), channels, false, etag, lastModified);
    }
}
