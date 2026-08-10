namespace BitMagic.BennyBox.Core.Models;

public enum ProfileSourceType
{
    M3u,
    XtreamCodes,
    LocalFolder,
    Sftp
}

public enum EpgSourceType
{
    None,
    XmltvUrl,
    XtreamEmbedded
}

// Which kind of content a given LocalFolder/Sftp root holds - a single profile can set a path for
// either or both (see ProfileSource.Local*Path/Sftp*RemotePath), since one site/folder tree commonly
// hosts both movies and TV shows under separate subtrees. Used as a parameter (which root to resolve)
// rather than a ProfileSource property, since a profile isn't tied to just one anymore.
public enum MediaKind
{
    Movies,
    Series,

    // One-off/uncategorized video with no useful season/episode number and no movie-database match
    // worth attempting - sports broadcasts, TV specials. See IClipSource/FolderClipSource.
    Clips
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

    // Snapshot of the provider's account info as of the last successful authenticate call (Add or
    // Refresh) - not re-fetched on every app launch, so this can lag reality between refreshes.
    public string? XtreamStatus { get; set; }
    public DateTime? XtreamExpiryUtc { get; set; }
    public int? XtreamMaxConnections { get; set; }

    // LocalFolder: any subset may be set - a folder tree with a movies root, a shows root, and/or a
    // clips root is common, so this isn't an either/or choice the way SourceType is.
    public string? LocalMoviesPath { get; set; }
    public string? LocalSeriesPath { get; set; }
    public string? LocalClipsPath { get; set; }

    // Sftp: one connection (host/credentials), with movies/series/clips each optionally pointing at
    // their own remote path under that same site - same "any subset" reasoning as LocalFolder.
    public string? SftpHost { get; set; }
    public int? SftpPort { get; set; }
    public string? SftpUsername { get; set; }
    public string? SftpPasswordEncrypted { get; set; }
    public string? SftpMoviesRemotePath { get; set; }
    public string? SftpSeriesRemotePath { get; set; }
    public string? SftpClipsRemotePath { get; set; }
}
