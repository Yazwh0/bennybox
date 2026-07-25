using System.Net;
using System.Runtime.CompilerServices;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Xmltv;

public class XmltvEpgSource : IEpgSource
{
    private readonly HttpClient _httpClient;

    public XmltvEpgSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public EpgSourceType SourceType => EpgSourceType.XmltvUrl;

    public Task<EpgFetchResult> GetProgrammesAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profile.EpgUrl))
        {
            throw new InvalidOperationException("Profile has no EPG URL configured.");
        }

        return FetchAsync(profile.EpgUrl, profile.Id, profile.EpgETag, profile.EpgLastModified, _httpClient, cancellationToken);
    }

    internal static async Task<EpgFetchResult> FetchAsync(
        string url, Guid profileId, string? etag, string? lastModified, HttpClient httpClient, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ConditionalRequestHelper.ApplyConditionalHeaders(request, etag, lastModified);

        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            response.Dispose();
            return new EpgFetchResult(true, null, null, null);
        }

        response.EnsureSuccessStatusCode();
        var newEtag = response.Headers.ETag?.ToString();
        var newLastModified = response.Content.Headers.LastModified?.ToString("R");

        return new EpgFetchResult(false, newEtag, newLastModified, StreamAsync(response, profileId, cancellationToken));
    }

    private static async IAsyncEnumerable<EpgProgramme> StreamAsync(
        HttpResponseMessage response, Guid profileId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using (response)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await foreach (var entry in XmltvParser.ParseAsync(stream, cancellationToken))
            {
                yield return Map(profileId, entry);
            }
        }
    }

    internal static EpgProgramme Map(Guid profileId, XmltvProgrammeEntry entry) => new()
    {
        ProfileId = profileId,
        ChannelTvgId = entry.ChannelId,
        Title = entry.Title,
        Description = entry.Description,
        StartUtc = entry.Start.UtcDateTime,
        EndUtc = entry.Stop.UtcDateTime
    };
}
