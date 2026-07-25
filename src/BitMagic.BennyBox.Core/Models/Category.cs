namespace BitMagic.BennyBox.Core.Models;

public class Category
{
    public required string Id { get; set; }
    public required Guid ProfileId { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
}
