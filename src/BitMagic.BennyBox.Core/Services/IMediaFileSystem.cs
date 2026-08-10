namespace BitMagic.BennyBox.Core.Services;

// A file found while walking a media root - RelativePath is relative to the root that was scanned
// (e.g. "Show Name/Season 01/Show Name - S01E02 - Title.mkv"), used both as the display path for
// filename parsing and as the stable identifier persisted as SourceMovieId/part of an episode's
// SourceEpisodeId, so a later rescan or on-demand episode fetch can relocate the same file.
public record MediaFileEntry(string RelativePath, long Length);

// Abstracts "enumerate files under a root, open one for reading, and build a playable URL for it" -
// implemented once for the local filesystem and once for SFTP (via SSH.NET), so FolderMediaScanner's
// scanning/parsing logic is written exactly once and works identically for both. An instance is
// constructed per-profile (holding whatever connection info that profile needs - a local root path,
// or an SFTP host/credentials), not shared across profiles.
//
// IAsyncDisposable: the SFTP implementation connects lazily on first use and reuses that ONE
// connection for every call made on the same instance (SFTP multiplexes multiple requests over a
// single SSH connection fine, so there's no need to log in again per file) - the connection is only
// actually closed when the whole instance is disposed, which the caller should do once it's done
// with an entire scan (not per file/operation). LocalFileSystem's implementation is a no-op, since
// plain file I/O has no connection to hold open.
public interface IMediaFileSystem : IAsyncDisposable
{
    // Recursive - every file under rootPath, at any depth, as a flat sequence. Ordering is not
    // guaranteed; callers group by folder structure themselves from RelativePath.
    IAsyncEnumerable<MediaFileEntry> EnumerateFilesAsync(CancellationToken cancellationToken = default);

    // Opens a file for reading - used for NFO sidecar parsing and poster image bytes, not for video
    // playback (playback goes straight to libVLC via BuildStreamUrl's MRL, never through this).
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    // Builds the MRL handed to PlayerViewModel/libVLC for a given file - file:// for local paths,
    // sftp://user:pass@host:port/path for SFTP (libVLC's bundled sftp access module plays this
    // directly, confirmed present in the referenced VideoLAN.LibVLC.Windows package).
    string BuildStreamUrl(string relativePath);
}
