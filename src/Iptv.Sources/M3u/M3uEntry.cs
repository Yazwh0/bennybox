namespace Iptv.Sources.M3u;

public record M3uEntry(string Url, string Title, IReadOnlyDictionary<string, string> Attributes);
