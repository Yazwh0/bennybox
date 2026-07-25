namespace BitMagic.BennyBox.Core.Models;

public class Movie
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid ProfileId { get; set; }
    public required string SourceMovieId { get; set; }
    public string? CategoryId { get; set; }
    public required string Name { get; set; }
    public string? CoverUrl { get; set; }
    public required string StreamUrl { get; set; }
    public double? Rating { get; set; }

    // Not available from the bulk VOD listing on Xtream (only per-title get_vod_info) - null until
    // the user opens this movie's detail page, see IMovieSource.GetDetailsAsync.
    public string? Plot { get; set; }
    public string? Genre { get; set; }
    public string? ReleaseDate { get; set; }
    public string? Duration { get; set; }
}
