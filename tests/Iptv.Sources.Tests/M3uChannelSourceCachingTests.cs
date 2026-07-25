using System.Net;
using System.Net.Http.Headers;
using Iptv.Core.Models;
using Iptv.Sources.M3u;

namespace Iptv.Sources.Tests;

public class M3uChannelSourceCachingTests
{
    private const string Playlist = """
        #EXTM3U
        #EXTINF:-1 tvg-id="ch1" group-title="News",Channel 1
        http://example.com/1.m3u8
        """;

    [Fact]
    public async Task ImportAsync_FirstFetch_ReturnsChannelsAndCapturesValidators()
    {
        using var handler = new FakeHandler(request =>
        {
            Assert.Null(request.Headers.IfNoneMatch.FirstOrDefault());
            Assert.Null(request.Headers.IfModifiedSince);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Playlist)
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"abc123\"");
            return response;
        });
        using var httpClient = new HttpClient(handler);
        var source = new M3uChannelSource(httpClient);
        var profile = new ProfileSource { Name = "Test", SourceType = ProfileSourceType.M3u, M3uUrl = "http://example.com/playlist.m3u" };

        var result = await source.ImportAsync(profile);

        Assert.False(result.NotModified);
        Assert.Single(result.Channels);
        Assert.Equal("\"abc123\"", result.ETag);
    }

    [Fact]
    public async Task ImportAsync_SendsStoredValidators_AndServerReturns304_ResultIsNotModifiedWithNoChannels()
    {
        using var handler = new FakeHandler(request =>
        {
            // This is the core of the caching behavior: previously-stored validators must be sent
            // back on the next refresh so the server can short-circuit with a 304.
            Assert.Equal("\"abc123\"", request.Headers.IfNoneMatch.FirstOrDefault()?.ToString());
            Assert.Equal(DateTimeOffset.Parse("Mon, 01 Jan 2024 00:00:00 GMT"), request.Headers.IfModifiedSince);
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });
        using var httpClient = new HttpClient(handler);
        var source = new M3uChannelSource(httpClient);
        var profile = new ProfileSource
        {
            Name = "Test",
            SourceType = ProfileSourceType.M3u,
            M3uUrl = "http://example.com/playlist.m3u",
            PlaylistETag = "\"abc123\"",
            PlaylistLastModified = "Mon, 01 Jan 2024 00:00:00 GMT"
        };

        var result = await source.ImportAsync(profile);

        Assert.True(result.NotModified);
        Assert.Empty(result.Channels);
        Assert.Empty(result.Categories);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
