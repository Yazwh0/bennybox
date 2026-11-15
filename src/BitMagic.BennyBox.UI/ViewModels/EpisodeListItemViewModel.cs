using CommunityToolkit.Mvvm.ComponentModel;
using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.ViewModels;

public partial class EpisodeListItemViewModel : ObservableObject
{
    public Episode Episode { get; }

    // True when this stream didn't play the last time it was tried (see StreamHealth) - shown as a
    // warning so dead streams can be skipped without tapping them. RefreshHealth re-reads it after
    // a StreamHealthChangedMessage.
    public bool DidNotWork => BitMagic.BennyBox.UI.Services.StreamHealth.Current?.DidNotWork(Episode.StreamUrl) ?? false;

    public void RefreshHealth() => OnPropertyChanged(nameof(DidNotWork));

    // The source entry this episode came from - the show itself, or one of its parts when a show was
    // merged from several entries (SeriesListItemViewModel.Parts). Playback, watched state and
    // downloads are all keyed on this one.
    public Series Series { get; }
    public string Title => Episode.Title;

    // Providers often name episodes "Show Name - S01E02 - Title". Where the show and number are
    // already on screen, just the title reads better ("Episode 2" when that's all there is).
    public string ShortTitle
    {
        get
        {
            var title = System.Text.RegularExpressions.Regex.Replace(Episode.Title, @"^.*?\bS\d+\s*E\d+\b\s*[-:–]?\s*", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            return title.Length > 0 ? title : $"Episode {Episode.EpisodeNumber}";
        }
    }
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

    public EpisodeListItemViewModel(Episode episode, Series series, bool isWatched = false, bool isFromDownloadsProfile = false)
    {
        Episode = episode;
        Series = series;
        _isWatched = isWatched;
        IsFromDownloadsProfile = isFromDownloadsProfile;
    }
}
