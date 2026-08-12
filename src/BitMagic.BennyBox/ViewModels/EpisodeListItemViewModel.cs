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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchedIcon))]
    [NotifyPropertyChangedFor(nameof(ContentOpacity))]
    private bool _isWatched;

    public string WatchedIcon => IsWatched ? "✓" : "👁";
    public double ContentOpacity => IsWatched ? 0.5 : 1.0;

    // See MovieListItemViewModel.IsFromDownloadsProfile - same rationale (this series is itself a
    // Downloads-profile item).
    public bool IsFromDownloadsProfile { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadIcon))]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    private DownloadUiState _downloadState = DownloadUiState.NotDownloaded;

    [ObservableProperty]
    private double _downloadProgress;

    public bool CanDownload => !IsFromDownloadsProfile && DownloadState is DownloadUiState.NotDownloaded;

    public string DownloadIcon => DownloadState switch
    {
        DownloadUiState.Queued => "⏳",
        DownloadUiState.Downloading => "↓",
        DownloadUiState.Completed => "✓",
        _ => "⬇"
    };

    public EpisodeListItemViewModel(Episode episode, bool isWatched = false, bool isFromDownloadsProfile = false)
    {
        Episode = episode;
        _isWatched = isWatched;
        IsFromDownloadsProfile = isFromDownloadsProfile;
    }
}
