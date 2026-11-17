using CommunityToolkit.Mvvm.ComponentModel;
using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.ViewModels;

// Mirrors MovieListItemViewModel, but simpler: a clip's Plot/Genre/ReleaseDate/Rating are already
// final the moment the scan produces it (an NFO sidecar if one exists, nothing else - never TMDb),
// so there's no "IsLoadingDetails"/ApplyDetails on-demand fetch branch to mirror.
public partial class ClipListItemViewModel : ObservableObject
{
    public Movie Clip { get; }

    // True when this stream didn't play the last time it was tried (see StreamHealth) - shown as a
    // warning so dead streams can be skipped without tapping them. RefreshHealth re-reads it after
    // a StreamHealthChangedMessage.
    public bool DidNotWork => BitMagic.BennyBox.UI.Services.StreamHealth.Current?.DidNotWork(Clip.StreamUrl) ?? false;

    // This device can't decode it (4K/UHD on a phone, say) - see StreamHealth.CannotPlayHere.
    public bool CannotPlayHere => BitMagic.BennyBox.UI.Services.StreamHealth.Current?.CannotPlayHere(Clip.StreamUrl) ?? false;

    public void RefreshHealth()
    {
        OnPropertyChanged(nameof(DidNotWork));
        OnPropertyChanged(nameof(CannotPlayHere));
    }

    public string Name => Clip.Name;
    public string? CoverUrl => Clip.CoverUrl;

    // See MovieListItemViewModel.SourceName - same badge rationale.
    public string SourceName { get; }

    public string? Plot => Clip.Plot;
    public bool HasPlot => !string.IsNullOrWhiteSpace(Plot);

    public string? MetaLine { get; }
    public bool HasMetaLine => !string.IsNullOrEmpty(MetaLine);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteIcon))]
    private bool _isFavorite;

    public string FavoriteIcon => IsFavorite ? "★" : "☆";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchedIcon))]
    [NotifyPropertyChangedFor(nameof(ContentOpacity))]
    private bool _isWatched;

    public string WatchedIcon => IsWatched ? "✓" : "👁";
    public double ContentOpacity => IsWatched ? 0.5 : 1.0;

    // See MovieListItemViewModel.IsFromDownloadsProfile - same rationale.
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

    public ClipListItemViewModel(Movie clip, string sourceName, bool isFavorite = false, bool isWatched = false, bool isFromDownloadsProfile = false)
    {
        Clip = clip;
        SourceName = sourceName;
        _isFavorite = isFavorite;
        _isWatched = isWatched;
        MetaLine = BuildMetaLine(clip.Genre, clip.ReleaseDate, clip.Rating);
        IsFromDownloadsProfile = isFromDownloadsProfile;
    }

    private static string? BuildMetaLine(string? genre, string? releaseDate, double? rating)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(genre))
        {
            parts.Add(genre);
        }
        if (!string.IsNullOrWhiteSpace(releaseDate))
        {
            parts.Add(releaseDate);
        }
        if (rating is > 0)
        {
            parts.Add($"★ {rating:0.0}");
        }

        return parts.Count > 0 ? string.Join("   •   ", parts) : null;
    }
}
