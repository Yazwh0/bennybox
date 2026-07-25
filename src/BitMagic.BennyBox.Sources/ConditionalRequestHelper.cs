using System.Net.Http.Headers;

namespace BitMagic.BennyBox.Sources;

internal static class ConditionalRequestHelper
{
    public static void ApplyConditionalHeaders(HttpRequestMessage request, string? etag, string? lastModified)
    {
        if (!string.IsNullOrEmpty(etag) && EntityTagHeaderValue.TryParse(etag, out var etagValue))
        {
            request.Headers.IfNoneMatch.Add(etagValue);
        }

        if (!string.IsNullOrEmpty(lastModified) && DateTimeOffset.TryParse(lastModified, out var lastModifiedValue))
        {
            request.Headers.IfModifiedSince = lastModifiedValue;
        }
    }
}
