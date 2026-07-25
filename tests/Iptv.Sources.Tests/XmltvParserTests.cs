using System.Text;
using Iptv.Sources.Xmltv;

namespace Iptv.Sources.Tests;

public class XmltvParserTests
{
    [Fact]
    public async Task ParseAsync_ExtractsProgrammeFields()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <tv>
              <channel id="bbc1.uk"><display-name>BBC One</display-name></channel>
              <programme start="20260724120000 +0100" stop="20260724130000 +0100" channel="bbc1.uk">
                <title>The News</title>
                <desc>Today's headlines.</desc>
              </programme>
            </tv>
            """;

        var entries = await ParseAllAsync(xml);

        var entry = Assert.Single(entries);
        Assert.Equal("bbc1.uk", entry.ChannelId);
        Assert.Equal("The News", entry.Title);
        Assert.Equal("Today's headlines.", entry.Description);
        Assert.Equal(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.FromHours(1)), entry.Start);
        Assert.Equal(new DateTimeOffset(2026, 7, 24, 13, 0, 0, TimeSpan.FromHours(1)), entry.Stop);
    }

    [Fact]
    public async Task ParseAsync_NegativeOffset_ParsesCorrectly()
    {
        const string xml = """
            <tv>
              <programme start="20260724120000 -0500" stop="20260724130000 -0500" channel="ch1">
                <title>Show</title>
              </programme>
            </tv>
            """;

        var entries = await ParseAllAsync(xml);

        var entry = Assert.Single(entries);
        Assert.Equal(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.FromHours(-5)), entry.Start);
    }

    [Fact]
    public async Task ParseAsync_MultipleProgrammes_YieldsAll()
    {
        const string xml = """
            <tv>
              <programme start="20260724120000 +0000" stop="20260724130000 +0000" channel="ch1"><title>A</title></programme>
              <programme start="20260724130000 +0000" stop="20260724140000 +0000" channel="ch1"><title>B</title></programme>
            </tv>
            """;

        var entries = await ParseAllAsync(xml);

        Assert.Equal(2, entries.Count);
        Assert.Equal("A", entries[0].Title);
        Assert.Equal("B", entries[1].Title);
    }

    [Fact]
    public async Task ParseAsync_MissingRequiredAttributes_IsSkipped()
    {
        const string xml = """
            <tv>
              <programme stop="20260724130000 +0000" channel="ch1"><title>No start</title></programme>
              <programme start="20260724120000 +0000" stop="20260724130000 +0000" channel="ch1"><title>Valid</title></programme>
            </tv>
            """;

        var entries = await ParseAllAsync(xml);

        var entry = Assert.Single(entries);
        Assert.Equal("Valid", entry.Title);
    }

    private static async Task<List<XmltvProgrammeEntry>> ParseAllAsync(string xml)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var entries = new List<XmltvProgrammeEntry>();
        await foreach (var entry in XmltvParser.ParseAsync(stream))
        {
            entries.Add(entry);
        }
        return entries;
    }
}
