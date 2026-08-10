namespace BitMagic.BennyBox.Core.Models;

public enum WatchProgressContentType
{
    Movie,
    Episode,

    // Only ever used by WatchedItem, never WatchProgress (a series itself isn't "played" - there's
    // nothing to resume) - marks a whole series as fully watched once every one of its episodes is.
    // See SeriesViewModel for where that gets computed and kept up to date.
    Series,

    // One-off/uncategorized media (sports broadcasts, specials) - see Clip/IClipSource. Tracked the
    // same way as Movie (resumable, markable watched), just a separate content type.
    Clip
}

// A "continue watching" bookmark. Keyed by (ProfileId, ContentType, ContentKey) rather than a
// foreign key to Movies/Series' own Guid Id, because a profile refresh fully replaces those rows
// with freshly-generated Ids (see MovieImportService/SeriesImportService) - a stable
// provider-assigned key survives that, an internal Guid FK would silently orphan on every refresh.
// Display fields are denormalized for the same reason, and because Episodes aren't persisted
// anywhere else to join back against (see Episode.cs).
public class WatchProgress
{
    public required Guid ProfileId { get; set; }
    public required WatchProgressContentType ContentType { get; set; }
    public required string ContentKey { get; set; }
    public required string Title { get; set; }
    public string? CoverUrl { get; set; }
    public required string StreamUrl { get; set; }
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
