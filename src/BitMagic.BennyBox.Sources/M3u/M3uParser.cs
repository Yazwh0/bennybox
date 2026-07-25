using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace BitMagic.BennyBox.Sources.M3u;

public static partial class M3uParser
{
    [GeneratedRegex(@"(?<key>[\w-]+)=""(?<value>[^""]*)""")]
    private static partial Regex AttributeRegex();

    public static async IAsyncEnumerable<M3uEntry> ParseAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream);
        string? pendingExtInfLine = null;

        while (await reader.ReadLineAsync(cancellationToken) is { } rawLine)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                pendingExtInfLine = line;
                continue;
            }

            if (line.StartsWith('#'))
            {
                // #EXTM3U, #EXTGRP, #EXTVLCOPT, etc - not needed for v1.
                continue;
            }

            if (pendingExtInfLine is null)
            {
                // A stream URL with no preceding #EXTINF is malformed - skip it.
                continue;
            }

            yield return BuildEntry(pendingExtInfLine, line);
            pendingExtInfLine = null;
        }
    }

    private static M3uEntry BuildEntry(string extInfLine, string url)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex().Matches(extInfLine))
        {
            attributes[match.Groups["key"].Value] = match.Groups["value"].Value;
        }

        // The title follows the last comma after any quoted attribute values, since a
        // group-title like "News, Sports" can itself contain commas.
        var searchStart = extInfLine.LastIndexOf('"');
        var commaIndex = extInfLine.IndexOf(',', searchStart < 0 ? 0 : searchStart);
        var title = commaIndex >= 0 && commaIndex < extInfLine.Length - 1
            ? extInfLine[(commaIndex + 1)..].Trim()
            : "Unknown";

        return new M3uEntry(url, title, attributes);
    }
}
