using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Iptv.App.Messages;
using Iptv.Core.Models;
using Iptv.Core.Services;
using Microsoft.Extensions.Logging;

namespace Iptv.App.ViewModels;

public partial class MoviesViewModel : ViewModelBase
{
    private const string AllCategoriesLabel = "All Categories";

    private readonly IProfileRepository _profileRepository;
    private readonly IMovieRepository _movieRepository;
    private readonly MovieImportService _movieImportService;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly ILogger<MoviesViewModel> _logger;

    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(300);

    private List<MovieCategoryGroupViewModel> _allGroups = [];
    private CancellationTokenSource? _searchCts;

    // Keyed by ProfileSource.Id so a movie's details/playback always use the credentials of whichever
    // profile actually owns it - see the equivalent field in SeriesViewModel for why a single shared
    // "current profile" field is wrong once more than one Xtream profile has content.
    private Dictionary<Guid, ProfileSource> _movieProfilesById = [];

    public string Title => "Movies";

    public PlayerViewModel Player { get; }

    // Flattened header+movie rows for a single virtualized list, shown while browsing (see
    // CategoryHeaderRow) - same rationale as LiveTvViewModel.Rows.
    public ObservableCollection<object> Rows { get; } = [];

    public ObservableCollection<string> Categories { get; } = [AllCategoriesLabel];

    [ObservableProperty]
    private MovieListItemViewModel? _selectedMovie;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _loadError;

    [ObservableProperty]
    private bool _hasNoMovies;

    [ObservableProperty]
    private bool _hasNoMatches;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = AllCategoriesLabel;

    public MoviesViewModel(
        PlayerViewModel player,
        IProfileRepository profileRepository,
        IMovieRepository movieRepository,
        MovieImportService movieImportService,
        IFavoriteRepository favoriteRepository,
        ILogger<MoviesViewModel> logger)
    {
        Player = player;
        _profileRepository = profileRepository;
        _movieRepository = movieRepository;
        _movieImportService = movieImportService;
        _favoriteRepository = favoriteRepository;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<ChannelsUpdatedMessage>(this, (_, _) => _ = LoadMoviesAsync());
        WeakReferenceMessenger.Default.Register<FavoritesUpdatedMessage>(this, (_, _) => _ = RefreshFavoriteFlagsAsync());

        _ = LoadMoviesAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadMoviesAsync();

    [RelayCommand]
    private async Task ToggleFavoriteAsync(MovieListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsFavorite)
        {
            await _favoriteRepository.RemoveMovieAsync(item.Movie.Id);
            item.IsFavorite = false;
        }
        else
        {
            await _favoriteRepository.AddMovieAsync(item.Movie.ProfileId, item.Movie.Id);
            item.IsFavorite = true;
        }

        WeakReferenceMessenger.Default.Send(new FavoritesUpdatedMessage());
    }

    // Same rationale as SeriesViewModel.RefreshFavoriteFlagsAsync - keep this page's stars in sync
    // without a full reload when a favorite is toggled elsewhere (e.g. Favorites' remove button).
    private async Task RefreshFavoriteFlagsAsync()
    {
        var favoriteIds = await _favoriteRepository.GetFavoriteMovieIdsAsync();
        foreach (var group in _allGroups)
        {
            foreach (var movie in group.Movies)
            {
                movie.IsFavorite = favoriteIds.Contains(movie.Movie.Id);
            }
        }
    }

    [RelayCommand]
    private async Task SelectMovieAsync(MovieListItemViewModel? movie)
    {
        if (movie is null)
        {
            return;
        }

        SelectedMovie = movie;

        if (movie.HasPlot || !_movieProfilesById.TryGetValue(movie.Movie.ProfileId, out var profile))
        {
            return;
        }

        movie.IsLoadingDetails = true;
        try
        {
            var details = await _movieImportService.GetDetailsAsync(profile, movie.Movie);
            if (details is not null)
            {
                movie.ApplyDetails(details);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load details for movie {MovieName}", movie.Name);
        }
        finally
        {
            movie.IsLoadingDetails = false;
        }
    }

    [RelayCommand]
    private void BackToList() => SelectedMovie = null;

    [RelayCommand]
    private void PlayMovie(MovieListItemViewModel? movie)
    {
        if (movie is null)
        {
            return;
        }

        Player.PlayMovie(movie.Movie);
    }

    partial void OnSearchTextChanged(string value) => DebounceApplyFilter();

    partial void OnSelectedCategoryChanged(string value) => DebounceApplyFilter();

    private void DebounceApplyFilter()
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = ApplyFilterDebouncedAsync(cts.Token);
    }

    // Same rationale as LiveTvViewModel/SeriesViewModel: debounce so we only filter once the user
    // pauses typing, and do the scan on a background thread so a large library never blocks the UI.
    private async Task ApplyFilterDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDebounceDelay, cancellationToken);

            var snapshot = _allGroups;
            var query = SearchText.Trim();
            var category = SelectedCategory;
            var filtered = await Task.Run(() => Flatten(Filter(snapshot, query, category)), cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            Dispatcher.UIThread.Post(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                Rows.Clear();
                foreach (var row in filtered)
                {
                    Rows.Add(row);
                }
                HasNoMatches = Rows.Count == 0 && _allGroups.Count > 0;
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke/category change - just drop this pass.
        }
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var row in Flatten(Filter(_allGroups, SearchText.Trim(), SelectedCategory)))
        {
            Rows.Add(row);
        }
        HasNoMovies = _allGroups.Count == 0;
        HasNoMatches = Rows.Count == 0 && _allGroups.Count > 0;
    }

    private static List<MovieCategoryGroupViewModel> Filter(List<MovieCategoryGroupViewModel> groups, string query, string category)
    {
        var result = new List<MovieCategoryGroupViewModel>();
        foreach (var group in groups)
        {
            if (category != AllCategoriesLabel && group.Name != category)
            {
                continue;
            }

            var matching = string.IsNullOrEmpty(query)
                ? group.Movies
                : group.Movies.Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matching.Count == 0)
            {
                continue;
            }

            result.Add(new MovieCategoryGroupViewModel(group.Name, matching));
        }

        return result;
    }

    private static List<object> Flatten(List<MovieCategoryGroupViewModel> groups)
    {
        var rows = new List<object>();
        foreach (var group in groups)
        {
            rows.Add(new CategoryHeaderRow(group.Name));
            rows.AddRange(group.Movies);
        }

        return rows;
    }

    private async Task LoadMoviesAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var favoriteIds = await _favoriteRepository.GetFavoriteMovieIdsAsync();
            var profiles = await _profileRepository.GetAllAsync();

            // Movies is Xtream-specific (no equivalent structured API for M3U) - every Xtream profile
            // with any imported movies contributes to the browsing list, same as Live TV merges
            // channels across all profiles.
            var profileData = new List<(IReadOnlyList<Category> Categories, IReadOnlyList<Movie> MovieList)>();
            var movieProfilesById = new Dictionary<Guid, ProfileSource>();
            foreach (var profile in profiles)
            {
                var categories = await _movieRepository.GetCategoriesAsync(profile.Id);
                var movieList = await _movieRepository.GetMoviesAsync(profile.Id);
                if (movieList.Count == 0)
                {
                    continue;
                }

                profileData.Add((categories, movieList));
                movieProfilesById[profile.Id] = profile;
            }
            _movieProfilesById = movieProfilesById;

            // Grouping/sorting/object-construction can be real CPU work for a large library - keep it
            // off the UI thread, same rationale as LiveTvViewModel.
            _allGroups = await Task.Run(() =>
            {
                var groups = new List<MovieCategoryGroupViewModel>();
                foreach (var (categories, movieList) in profileData)
                {
                    var moviesByCategory = movieList.ToLookup(m => m.CategoryId);
                    foreach (var category in categories)
                    {
                        var categoryMovies = moviesByCategory[category.Id]
                            .Select(m => new MovieListItemViewModel(m, favoriteIds.Contains(m.Id)))
                            .ToList();
                        if (categoryMovies.Count == 0)
                        {
                            continue;
                        }

                        groups.Add(new MovieCategoryGroupViewModel(category.Name, categoryMovies));
                    }
                }

                return groups.OrderBy(g => g.Name).ToList();
            });

            var selectedCategory = SelectedCategory;
            Categories.Clear();
            Categories.Add(AllCategoriesLabel);
            foreach (var group in _allGroups)
            {
                Categories.Add(group.Name);
            }

            if (!Categories.Contains(selectedCategory))
            {
                selectedCategory = AllCategoriesLabel;
            }
            SelectedCategory = selectedCategory;

            ApplyFilter();
        }
        catch (Exception ex)
        {
            LoadError = "Failed to load movies.";
            _logger.LogError(ex, "Failed to load movies");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
