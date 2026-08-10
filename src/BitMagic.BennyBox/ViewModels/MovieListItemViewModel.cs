using CommunityToolkit.Mvvm.ComponentModel;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.ViewModels;

public partial class MovieListItemViewModel : ObservableObject
{
    public Movie Movie { get; }
    public string Name => Movie.Name;
    public string? CoverUrl => Movie.CoverUrl;

    // The owning profile's name (e.g. "Seedhost", "My IPTV Playlist") - shown as a small badge so a
    // library merged from more than one profile (Xtream + a local/SFTP folder, or two Xtream accounts)
    // still shows which one any given movie actually came from.
    public string SourceName { get; }

    // Xtream's bulk VOD listing only gives us Rating up front - Plot/Genre/ReleaseDate/Duration stay
    // null until LoadDetailsAsync fetches them (see MoviesViewModel.SelectMovieAsync). LocalFolder/Sftp
    // movies are different: their NFO is read once during the scan and persisted (see
    // SqliteMovieRepository.ReplaceMoviesAsync), so Movie.Plot/Genre/ReleaseDate already have real
    // values here and HasPlot is already true - SelectMovieAsync's live-fetch is skipped entirely.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlot))]
    private string? _plot;

    public bool HasPlot => !string.IsNullOrWhiteSpace(Plot);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetaLine))]
    private string? _metaLine;

    public bool HasMetaLine => !string.IsNullOrEmpty(MetaLine);

    [ObservableProperty]
    private bool _isLoadingDetails;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteIcon))]
    private bool _isFavorite;

    public string FavoriteIcon => IsFavorite ? "★" : "☆";

    // Watched is shown as a fade rather than a second icon competing with the favorite star - only a
    // checkmark appears once watched, nothing is drawn for the unwatched state.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchedIcon))]
    [NotifyPropertyChangedFor(nameof(ContentOpacity))]
    private bool _isWatched;

    public string WatchedIcon => IsWatched ? "✓" : "";
    public double ContentOpacity => IsWatched ? 0.5 : 1.0;

    public MovieListItemViewModel(Movie movie, string sourceName, bool isFavorite = false, bool isWatched = false)
    {
        Movie = movie;
        SourceName = sourceName;
        _isFavorite = isFavorite;
        _isWatched = isWatched;
        _plot = movie.Plot;
        _metaLine = BuildMetaLine(movie.Genre, movie.ReleaseDate, movie.Rating);
    }

    public void ApplyDetails(MovieDetails details)
    {
        Plot = details.Plot;
        MetaLine = BuildMetaLine(details.Genre, details.ReleaseDate, details.Rating ?? Movie.Rating);
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
