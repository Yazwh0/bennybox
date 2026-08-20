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

    // Absolute path to the downloaded copy on disk, if one exists - either this episode IS itself a
    // Downloads-profile item, or a matching one was found in the Downloads profile (see
    // SeriesViewModel.GetDownloadedEpisodeKeysAsync). Null while NotDownloaded/Queued/Downloading.
    // Whenever this is set the content is by definition already local (SourceName is always the
    // Downloads profile's name in that case) - shown via ToolTip only (see SeriesView.axaml), not
    // spelled out on the tile itself, since it'd be showing on effectively every downloaded row.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocalFilePath))]
    private string? _localFilePath;

    public bool HasLocalFilePath => !string.IsNullOrEmpty(LocalFilePath);

    // See MovieListItemViewModel.SourceName for the general badge rationale. Here it's the profile
    // actually providing THIS row's content right now, paired with LocalFilePath - the Downloads
    // profile's name when a local copy exists, otherwise the series' own streaming profile (the two
    // differ once an episode has been downloaded, which is what makes this worth showing per-row
    // rather than once at the series header, unlike Movies/Clips where every row is always the same
    // profile the parent list is scoped to).
    public string SourceName { get; set; } = string.Empty;

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
