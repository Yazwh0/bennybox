namespace Iptv.Core.Models;

// Not persisted - fetched live per-series (see ISeriesSource.GetEpisodesAsync) rather than cached in
// the DB, since a library can have thousands of series and most episode lists are only ever looked at
// once, right before playing something from them.
public class Episode
{
    public required string SourceEpisodeId { get; set; }
    public required string Title { get; set; }
    public int Season { get; set; }
    public int EpisodeNumber { get; set; }
    public string? PlotSummary { get; set; }
    public required string StreamUrl { get; set; }
}
