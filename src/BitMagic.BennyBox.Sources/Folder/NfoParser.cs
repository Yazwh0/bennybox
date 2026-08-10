using System.Xml.Linq;

namespace BitMagic.BennyBox.Sources.Folder;

// Parses Kodi/Jellyfin/Plex-style .nfo sidecar XML files - the actual, near-universal convention for
// structured local media metadata (see the plan discussion: video containers themselves almost never
// carry a real synopsis/poster, but any library that's been scanned by one of those tools has these).
// Deliberately tolerant: any field can be missing, unexpected elements are ignored, and a malformed
// file returns null rather than throwing, so one bad .nfo never fails an entire folder scan.
public static class NfoParser
{
    public record MovieNfo(string? Title, string? Plot, string? Genre, string? ReleaseDate, double? Rating);
    public record TvShowNfo(string? Title, string? Plot, string? Genre, double? Rating);
    public record EpisodeNfo(string? Title, string? Plot);

    public static MovieNfo? TryParseMovie(Stream xmlStream)
    {
        var root = TryLoad(xmlStream);
        if (root is null)
        {
            return null;
        }

        return new MovieNfo(
            Element(root, "title"),
            Element(root, "plot"),
            Element(root, "genre"),
            Element(root, "premiered") ?? Element(root, "releasedate") ?? Element(root, "year"),
            RatingElement(root));
    }

    public static TvShowNfo? TryParseTvShow(Stream xmlStream)
    {
        var root = TryLoad(xmlStream);
        if (root is null)
        {
            return null;
        }

        return new TvShowNfo(
            Element(root, "title"),
            Element(root, "plot"),
            Element(root, "genre"),
            RatingElement(root));
    }

    public static EpisodeNfo? TryParseEpisode(Stream xmlStream)
    {
        var root = TryLoad(xmlStream);
        if (root is null)
        {
            return null;
        }

        return new EpisodeNfo(Element(root, "title"), Element(root, "plot"));
    }

    private static XElement? TryLoad(Stream xmlStream)
    {
        try
        {
            return XDocument.Load(xmlStream).Root;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? Element(XElement root, string name)
    {
        var value = root.Element(name)?.Value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // Kodi NFOs usually nest <rating><value>7.5</value></rating> under <ratings>, but older/simpler
    // ones just have a bare <rating>7.5</rating> - try both rather than picking one and silently
    // dropping the other's rating.
    private static double? RatingElement(XElement root)
    {
        var nested = root.Element("ratings")?.Element("rating")?.Element("value")?.Value;
        var flat = root.Element("rating")?.Value;
        return double.TryParse(nested ?? flat, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
