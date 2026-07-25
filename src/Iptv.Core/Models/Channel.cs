namespace Iptv.Core.Models;

public class Channel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid ProfileId { get; set; }
    public required string SourceChannelId { get; set; }
    public string? CategoryId { get; set; }
    public required string Name { get; set; }
    public string? LogoUrl { get; set; }
    public required string StreamUrl { get; set; }
    public string? TvgId { get; set; }
    public int Number { get; set; }

    // Catch-up/timeshift support - Xtream's tv_archive/tv_archive_duration or M3U's
    // catchup/catchup-days attributes. CatchupDays is how far back the provider retains programmes;
    // 0 means unknown/unsupported.
    public bool HasCatchup { get; set; }
    public int CatchupDays { get; set; }
}
