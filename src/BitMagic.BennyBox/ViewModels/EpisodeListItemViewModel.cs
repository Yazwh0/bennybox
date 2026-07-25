using CommunityToolkit.Mvvm.ComponentModel;
using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.ViewModels;

public partial class EpisodeListItemViewModel : ObservableObject
{
    public Episode Episode { get; }
    public string Title => Episode.Title;
    public string SeasonEpisodeLabel => $"S{Episode.Season:00}E{Episode.EpisodeNumber:00}";
    public string? PlotSummary => Episode.PlotSummary;
    public bool HasPlotSummary => !string.IsNullOrEmpty(PlotSummary);

    // Watched is shown as a fade, not a second icon - only a checkmark appears once watched, nothing
    // is drawn for the unwatched state.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchedIcon))]
    [NotifyPropertyChangedFor(nameof(ContentOpacity))]
    private bool _isWatched;

    public string WatchedIcon => IsWatched ? "✓" : "";
    public double ContentOpacity => IsWatched ? 0.5 : 1.0;

    public EpisodeListItemViewModel(Episode episode, bool isWatched = false)
    {
        Episode = episode;
        _isWatched = isWatched;
    }
}
