using System.Text;
using BitMagic.BennyBox.Sources.M3u;

namespace BitMagic.BennyBox.Sources.Tests;

public class M3uParserTests
{
    [Fact]
    public async Task ParseAsync_ExtractsAttributesAndTitle()
    {
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 tvg-id="bbc1.uk" tvg-logo="http://logo/bbc1.png" group-title="News",BBC One
            http://example.com/bbc1.m3u8
            """;

        var entries = await ParseAllAsync(playlist);

        var entry = Assert.Single(entries);
        Assert.Equal("BBC One", entry.Title);
        Assert.Equal("http://example.com/bbc1.m3u8", entry.Url);
        Assert.Equal("bbc1.uk", entry.Attributes["tvg-id"]);
        Assert.Equal("http://logo/bbc1.png", entry.Attributes["tvg-logo"]);
        Assert.Equal("News", entry.Attributes["group-title"]);
    }

    [Fact]
    public async Task ParseAsync_TitleWithCommaInGroupTitle_SplitsOnLastAttributeComma()
    {
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 group-title="News, Sports",Channel, With Comma
            http://example.com/stream.m3u8
            """;

        var entries = await ParseAllAsync(playlist);

        var entry = Assert.Single(entries);
        Assert.Equal("Channel, With Comma", entry.Title);
        Assert.Equal("News, Sports", entry.Attributes["group-title"]);
    }

    [Fact]
    public async Task ParseAsync_MultipleEntries_YieldsEachInOrder()
    {
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 tvg-id="ch1",Channel 1
            http://example.com/1.m3u8
            #EXTINF:-1 tvg-id="ch2",Channel 2
            http://example.com/2.m3u8
            """;

        var entries = await ParseAllAsync(playlist);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Channel 1", entries[0].Title);
        Assert.Equal("Channel 2", entries[1].Title);
    }

    [Fact]
    public async Task ParseAsync_UrlWithoutPrecedingExtInf_IsSkipped()
    {
        const string playlist = """
            #EXTM3U
            http://example.com/orphan.m3u8
            #EXTINF:-1 tvg-id="ch1",Channel 1
            http://example.com/1.m3u8
            """;

        var entries = await ParseAllAsync(playlist);

        var entry = Assert.Single(entries);
        Assert.Equal("Channel 1", entry.Title);
    }

    [Fact]
    public async Task ParseAsync_NoAttributesOrTitle_DefaultsToUnknown()
    {
        const string playlist = """
            #EXTM3U
            #EXTINF:-1
            http://example.com/stream.m3u8
            """;

        var entries = await ParseAllAsync(playlist);

        var entry = Assert.Single(entries);
        Assert.Equal("Unknown", entry.Title);
        Assert.Empty(entry.Attributes);
    }

    private static async Task<List<M3uEntry>> ParseAllAsync(string playlist)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(playlist));
        var entries = new List<M3uEntry>();
        await foreach (var entry in M3uParser.ParseAsync(stream))
        {
            entries.Add(entry);
        }
        return entries;
    }
}
