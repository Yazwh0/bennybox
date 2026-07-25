using System.Net;
using System.Net.Http.Headers;
using Iptv.Core.Models;
using Iptv.Sources.Xmltv;

namespace Iptv.Sources.Tests;

public class XmltvEpgSourceCachingTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <tv>
          <channel id="bbc1.uk"><display-name>BBC One</display-name></channel>
          <programme start="20260724120000 +0100" stop="20260724130000 +0100" channel="bbc1.uk">
            <title>The News</title>
          </programme>
        </tv>
        """;

    [Fact]
    public async Task GetProgrammesAsync_FirstFetch_StreamsProgrammesAndCapturesValidators()
    {
        using var handler = new FakeHandler(request =>
        {
            Assert.Null(request.Headers.IfNoneMatch.FirstOrDefault());

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Xml) };
            response.Headers.ETag = new EntityTagHeaderValue("\"epg-v1\"");
            return response;
        });
        using var httpClient = new HttpClient(handler);
        var source = new XmltvEpgSource(httpClient);
        var profile = new ProfileSource { Name = "Test", EpgSourceType = EpgSourceType.XmltvUrl, EpgUrl = "http://example.com/epg.xml" };

        var result = await source.GetProgrammesAsync(profile);

        Assert.False(result.NotModified);
        Assert.Equal("\"epg-v1\"", result.ETag);
        var programmes = await ToListAsync(result.Programmes!);
        Assert.Single(programmes);
        Assert.Equal("The News", programmes[0].Title);
    }

    [Fact]
    public async Task GetProgrammesAsync_SendsStoredEtag_AndServerReturns304_ResultIsNotModified()
    {
        using var handler = new FakeHandler(request =>
        {
            Assert.Equal("\"epg-v1\"", request.Headers.IfNoneMatch.FirstOrDefault()?.ToString());
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });
        using var httpClient = new HttpClient(handler);
        var source = new XmltvEpgSource(httpClient);
        var profile = new ProfileSource
        {
            Name = "Test",
            EpgSourceType = EpgSourceType.XmltvUrl,
            EpgUrl = "http://example.com/epg.xml",
            EpgETag = "\"epg-v1\""
        };

        var result = await source.GetProgrammesAsync(profile);

        Assert.True(result.NotModified);
        Assert.Null(result.Programmes);
    }

    private static async Task<List<EpgProgramme>> ToListAsync(IAsyncEnumerable<EpgProgramme> source)
    {
        var list = new List<EpgProgramme>();
        await foreach (var item in source)
        {
            list.Add(item);
        }
        return list;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
