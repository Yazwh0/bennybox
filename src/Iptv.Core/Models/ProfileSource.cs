namespace Iptv.Core.Models;

public enum ProfileSourceType
{
    M3u,
    XtreamCodes
}

public enum EpgSourceType
{
    None,
    XmltvUrl,
    XtreamEmbedded
}

public class ProfileSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public ProfileSourceType SourceType { get; set; }
    public string? M3uUrl { get; set; }
    public string? XtreamServerUrl { get; set; }
    public string? XtreamUsername { get; set; }
    public string? XtreamPasswordEncrypted { get; set; }
    public EpgSourceType EpgSourceType { get; set; } = EpgSourceType.None;
    public string? EpgUrl { get; set; }
    public DateTime? LastRefreshedUtc { get; set; }
    public int SortOrder { get; set; }

    // HTTP conditional-request caching (ETag / Last-Modified) so a refresh against a server that
    // supports it - the M3U/EPG payloads, not the Xtream API itself - can skip re-downloading,
    // re-parsing, and re-writing the DB entirely when nothing has changed.
    public string? PlaylistETag { get; set; }
    public string? PlaylistLastModified { get; set; }
    public string? EpgETag { get; set; }
    public string? EpgLastModified { get; set; }
}
