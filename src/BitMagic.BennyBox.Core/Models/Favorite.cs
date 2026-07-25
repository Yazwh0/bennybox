namespace BitMagic.BennyBox.Core.Models;

public class Favorite
{
    public required Guid ProfileId { get; set; }
    public required Guid ChannelId { get; set; }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
}
