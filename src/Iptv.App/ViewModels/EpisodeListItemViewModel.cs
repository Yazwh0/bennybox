using Iptv.Core.Models;

namespace Iptv.App.ViewModels;

public class EpisodeListItemViewModel
{
    public Episode Episode { get; }
    public string Title => Episode.Title;
    public string SeasonEpisodeLabel => $"S{Episode.Season:00}E{Episode.EpisodeNumber:00}";
    public string? PlotSummary => Episode.PlotSummary;
    public bool HasPlotSummary => !string.IsNullOrEmpty(PlotSummary);

    public EpisodeListItemViewModel(Episode episode)
    {
        Episode = episode;
    }
}
