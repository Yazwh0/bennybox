using System.Net;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.Sources.Xtream;

namespace BitMagic.BennyBox.Sources.Tests;

public class XtreamSeriesSourceTests
{
    private const string CategoriesJson = """
        [{"category_id":"5","category_name":"Drama"}]
        """;

    // Deliberately exercises the real-world Xtream quirks: series_id as a JSON number (not string),
    // rating as a string, and "releaseDate" in camelCase instead of the snake_case every other field uses.
    private const string SeriesJson = """
        [{"series_id":123,"name":"Breaking Bad","cover":"http://example.com/cover.jpg","plot":"A teacher turns to crime.","genre":"Drama","releaseDate":"2008-01-20","rating":"8.9","category_id":"5"}]
        """;

    // Episodes are keyed by season number (as a string) rather than being a flat array - and again,
    // "id" and "episode_num" show up as numbers here even though other panels send them as strings.
    private const string SeriesInfoJson = """
        {
            "info": {"name":"Breaking Bad","plot":"A teacher turns to crime."},
            "episodes": {
                "1": [
                    {"id":901,"episode_num":1,"title":"Pilot","container_extension":"mp4","info":{"plot":"The pilot episode."}},
                    {"id":902,"episode_num":2,"title":"Cat's in the Bag...","container_extension":"mp4"}
                ],
                "2": [
                    {"id":950,"episode_num":1,"title":"Seven Thirty-Seven","container_extension":"mkv"}
                ]
            }
        }
        """;

    private static ProfileSource CreateProfile() => new()
    {
        Name = "Test",
        SourceType = ProfileSourceType.XtreamCodes,
        XtreamServerUrl = "http://example.com",
        XtreamUsername = "user",
        XtreamPasswordEncrypted = CredentialProtector.Protect("pass")
    };

    [Fact]
    public async Task ImportAsync_MapsCategoriesAndSeries()
    {
        using var handler = new FakeHandler(request =>
        {
            var query = request.RequestUri!.Query;
            var json = query switch
            {
                _ when query.Contains("action=get_series_categories") => CategoriesJson,
                _ when query.Contains("action=get_series") => SeriesJson,
                _ => throw new InvalidOperationException($"Unexpected request: {query}")
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        });
        using var httpClient = new HttpClient(handler);
        var source = new XtreamSeriesSource(new XtreamClient(httpClient));

        var result = await source.ImportAsync(CreateProfile());

        var category = Assert.Single(result.Categories);
        Assert.Equal("5", category.Id);
        Assert.Equal("Drama", category.Name);

        var series = Assert.Single(result.SeriesList);
        Assert.Equal("123", series.SourceSeriesId);
        Assert.Equal("Breaking Bad", series.Name);
        Assert.Equal("http://example.com/cover.jpg", series.CoverUrl);
        Assert.Equal("2008-01-20", series.ReleaseDate);
        Assert.Equal(8.9, series.Rating);
        Assert.Equal("5", series.CategoryId);
    }

    [Fact]
    public async Task GetEpisodesAsync_FlattensSeasonsAndBuildsPlaybackUrls()
    {
        using var handler = new FakeHandler(request =>
        {
            Assert.Contains("get_series_info", request.RequestUri!.Query);
            Assert.Contains("series_id=123", request.RequestUri!.Query);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SeriesInfoJson) };
        });
        using var httpClient = new HttpClient(handler);
        var source = new XtreamSeriesSource(new XtreamClient(httpClient));
        var series = new Series { ProfileId = Guid.NewGuid(), SourceSeriesId = "123", Name = "Breaking Bad" };

        var episodes = await source.GetEpisodesAsync(CreateProfile(), series);

        Assert.Equal(3, episodes.Count);

        var first = episodes[0];
        Assert.Equal(1, first.Season);
        Assert.Equal(1, first.EpisodeNumber);
        Assert.Equal("Pilot", first.Title);
        Assert.Equal("The pilot episode.", first.PlotSummary);
        Assert.Equal("http://example.com/series/user/pass/901.mp4", first.StreamUrl);

        var last = episodes[2];
        Assert.Equal(2, last.Season);
        Assert.Equal("http://example.com/series/user/pass/950.mkv", last.StreamUrl);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
