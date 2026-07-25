namespace BitMagic.BennyBox.Core.Models;

public class EpgProgramme
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid ProfileId { get; set; }
    public required string ChannelTvgId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
}
