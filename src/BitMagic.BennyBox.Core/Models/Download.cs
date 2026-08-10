namespace BitMagic.BennyBox.Core.Models;

public enum DownloadStatus
{
    Queued,
    Downloading,
    Completed,
    Failed,
    Canceled
}

// A download's progress/lifecycle, tracked independently of the item it's downloading (Movie/Episode/
// Clip aren't otherwise persisted with a stable cross-session identity - see WatchProgress for the
// same rationale). Keyed by the ORIGINAL item's (ProfileId, ContentType, SourceId) - not the eventual
// downloaded copy's, since the download is still in progress (or failed) when this row is created and
// there's no downloaded copy yet.
public class Download
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid OriginalProfileId { get; set; }
    public required WatchProgressContentType ContentType { get; set; }
    public required string OriginalSourceId { get; set; }
    public required string Title { get; set; }
    public string? CoverUrl { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public long BytesDownloaded { get; set; }
    public long? TotalBytes { get; set; }

    // Relative to the Downloads profile's Movies/Series/Clips root (whichever applies) - set once
    // Status reaches Completed, see DownloadManager's filename conventions.
    public string? LocalRelativePath { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}
