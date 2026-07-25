namespace BitMagic.BennyBox.Core.Models;

// Keyed by (ProfileId, ChannelTvgId, StartUtc) rather than a foreign key to EpgProgramme's own Guid
// Id, for the same reason as WatchProgress - EPG data is fully replaced (delete+insert, fresh Ids)
// on every refresh, so a stable natural key survives that where an internal Guid FK would not.
// ChannelName/ProgrammeTitle are denormalized so the reminder banner and reminders list don't need
// to re-fetch/re-match against the (possibly since-changed) EPG data to render.
public class Reminder
{
    public required Guid ProfileId { get; set; }
    public required string ChannelTvgId { get; set; }
    public required DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public required string ChannelName { get; set; }
    public required string ProgrammeTitle { get; set; }
}
