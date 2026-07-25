using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;

namespace Iptv.Sources.Xmltv;

public record XmltvProgrammeEntry(string ChannelId, string Title, string? Description, DateTimeOffset Start, DateTimeOffset Stop);

public static class XmltvParser
{
    // Uses a forward-only XmlReader rather than XDocument.Load - real-world XMLTV feeds
    // run tens/hundreds of MB with 50k-100k+ <programme> elements, too large to load whole.
    public static async IAsyncEnumerable<XmltvProgrammeEntry> ParseAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var settings = new XmlReaderSettings { Async = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore };
        using var reader = XmlReader.Create(stream, settings);

        // XNode.ReadFrom and XmlReader.Skip both already leave the reader positioned at the
        // *next* node, same as ReadAsync would. So each loop iteration below reads at most once -
        // via ReadAsync in the "skip this node" paths, or implicitly via ReadFrom/Skip - never both,
        // or every other sibling gets silently skipped.
        await reader.ReadAsync();
        while (!reader.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element || reader.Name != "programme")
            {
                await reader.ReadAsync();
                continue;
            }

            var channelId = reader.GetAttribute("channel") ?? "";
            var startRaw = reader.GetAttribute("start");
            var stopRaw = reader.GetAttribute("stop");
            var start = default(DateTimeOffset);
            var stop = default(DateTimeOffset);
            var isValid = !string.IsNullOrEmpty(channelId) && startRaw is not null && stopRaw is not null &&
                TryParseXmltvDate(startRaw, out start) && TryParseXmltvDate(stopRaw, out stop);

            if (!isValid)
            {
                reader.Skip();
                continue;
            }

            // A <programme> only ever holds a handful of small child elements (title/desc/etc),
            // so materializing just this one element as an XElement is cheap and far less error-prone
            // than hand-rolling forward-only reads of its children - the outer loop is what keeps the
            // whole parse streaming across tens of thousands of <programme> siblings.
            var programmeElement = (XElement)XNode.ReadFrom(reader);
            var title = programmeElement.Element("title")?.Value ?? "";
            var description = programmeElement.Element("desc")?.Value;

            yield return new XmltvProgrammeEntry(channelId, title, description, start, stop);
        }
    }

    // XMLTV dates look like "20260724120000 +0100" - .NET's "zzz" format specifier requires a
    // colon in the offset ("+01:00"), which real-world feeds don't use, so parse manually instead.
    private static bool TryParseXmltvDate(string raw, out DateTimeOffset result)
    {
        result = default;
        raw = raw.Trim();
        if (raw.Length < 14)
        {
            return false;
        }

        if (!DateTime.TryParseExact(raw[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimePart))
        {
            return false;
        }

        var offsetPart = raw.Length > 14 ? raw[14..].Trim() : "";
        if (offsetPart.Length < 5)
        {
            result = new DateTimeOffset(dateTimePart, TimeSpan.Zero);
            return true;
        }

        var sign = offsetPart[0] == '-' ? -1 : 1;
        var digits = offsetPart.TrimStart('+', '-');
        if (digits.Length < 4 || !int.TryParse(digits[..2], out var hours) || !int.TryParse(digits[2..4], out var minutes))
        {
            result = new DateTimeOffset(dateTimePart, TimeSpan.Zero);
            return true;
        }

        result = new DateTimeOffset(dateTimePart, new TimeSpan(sign * hours, sign * minutes, 0));
        return true;
    }
}
