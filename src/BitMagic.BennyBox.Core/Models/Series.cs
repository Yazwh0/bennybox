namespace BitMagic.BennyBox.Core.Models;

public class Series
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid ProfileId { get; set; }
    public required string SourceSeriesId { get; set; }
    public string? CategoryId { get; set; }
    public required string Name { get; set; }
    public string? CoverUrl { get; set; }
    public string? Plot { get; set; }
    public string? Genre { get; set; }
    public string? ReleaseDate { get; set; }
    public double? Rating { get; set; }
}
