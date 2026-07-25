namespace BitMagic.BennyBox.Core.Models;

// A manual "seen this" marker, independent of WatchProgress - WatchProgress rows are transient
// (removed once something is finished, see PlayerViewModel.MarkCurrentContentFinished) and only
// exist for partially-watched titles, so they can't also carry a persistent watched/unwatched flag.
// Keyed the same way as WatchProgress/Reminder for the same reason: stable provider-assigned keys
// survive a profile refresh, the app's own Guid Ids do not.
public class WatchedItem
{
    public required Guid ProfileId { get; set; }
    public required WatchProgressContentType ContentType { get; set; }
    public required string ContentKey { get; set; }
    public DateTime WatchedUtc { get; set; }
}
