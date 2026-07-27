using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using BitMagic.BennyBox.Messages;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using Microsoft.Extensions.Logging;

namespace BitMagic.BennyBox.ViewModels;

public partial class MoviesViewModel : ViewModelBase
{
    private const string AllCategoriesLabel = "All Categories";

    private readonly IProfileRepository _profileRepository;
    private readonly IMovieRepository _movieRepository;
    private readonly MovieImportService _movieImportService;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IWatchedItemRepository _watchedItemRepository;
    private readonly ILogger<MoviesViewModel> _logger;

    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(300);

    private List<MovieCategoryGroupViewModel> _allGroups = [];
    private CancellationTokenSource? _searchCts;

    // See GuideViewModel._loadRequestId - LoadMoviesAsync now does a real network import on an
    // explicit Refresh (can take seconds), so two overlapping calls are a real possibility.
    private int _loadRequestId;

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

    // What the category ComboBox actually shows - narrowed by CategoryFilterText, but always a
    // subset of Categories. Kept separate so typing a filter query never touches SelectedCategory
    // (and so re-triggers a reload) until an item is actually picked from the (possibly narrowed)
    // list. Replaced wholesale (not mutated via Clear()+Add()) - clearing the ComboBox's bound
    // ItemsSource down to zero items, even briefly, resets its selection, and since SelectedCategory
    // is often unchanged across a reload (no PropertyChanged fires to re-push it) that selection
    // never came back. A one-shot swap to an already-complete list avoids that empty gap entirely.
    [ObservableProperty]
    private ObservableCollection<string> _filteredCategories = [AllCategoriesLabel];

    [ObservableProperty]
    private MovieListItemViewModel? _selectedMovie;

    [ObservableProperty]
    private bool _isLoading;

    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    [ObservableProperty]
    private string? _loadError;

    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);

    [ObservableProperty]
    private bool _hasNoMovies;

    [ObservableProperty]
    private bool _hasNoMatches;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = AllCategoriesLabel;

    [ObservableProperty]
    private string _categoryFilterText = string.Empty;

    public MoviesViewModel(
        PlayerViewModel player,
        IProfileRepository profileRepository,
        IMovieRepository movieRepository,
        MovieImportService movieImportService,
        IFavoriteRepository favoriteRepository,
        IWatchedItemRepository watchedItemRepository,
        ILogger<MoviesViewModel> logger)
    {
        Player = player;
        _profileRepository = profileRepository;
        _movieRepository = movieRepository;
        _movieImportService = movieImportService;
        _favoriteRepository = favoriteRepository;
        _watchedItemRepository = watchedItemRepository;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<ChannelsUpdatedMessage>(this, (_, _) => _ = LoadMoviesAsync());
        WeakReferenceMessenger.Default.Register<FavoritesUpdatedMessage>(this, (_, _) => _ = RefreshFavoriteFlagsAsync());
        WeakReferenceMessenger.Default.Register<WatchedStatusUpdatedMessage>(this, (_, _) => _ = RefreshWatchedFlagsAsync());

        _ = LoadMoviesAsync();
    }

    // See LiveTvViewModel.RefreshAsync / GuideViewModel.RefreshAsync - an explicit Refresh press is the
    // one moment this page should actually go re-fetch movies from the server, rather than just
    // re-reading whatever's already in the local cache.
    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        var failedProfiles = new List<string>();
        try
        {
            var profiles = await _profileRepository.GetAllAsync();
            foreach (var profile in profiles)
            {
                try
                {
                    await _movieImportService.ImportAsync(profile);
                }
                catch (Exception ex)
                {
                    failedProfiles.Add(profile.Name);
                    _logger.LogWarning(ex, "Failed to refresh movies for profile {ProfileName}", profile.Name);
                }
            }
        }
        finally
        {
            // LoadMoviesAsync resets LoadError to null on entry, so any failure message has to be
            // applied after it returns rather than before.
            await LoadMoviesAsync();
            if (failedProfiles.Count > 0)
            {
                LoadError = $"Couldn't refresh: {string.Join(", ", failedProfiles)}. Showing cached data.";
            }
        }
    }

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

    [RelayCommand]
    private async Task ToggleWatchedAsync(MovieListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsWatched)
        {
            await _watchedItemRepository.MarkUnwatchedAsync(item.Movie.ProfileId, WatchProgressContentType.Movie, item.Movie.SourceMovieId);
            item.IsWatched = false;
        }
        else
        {
            await _watchedItemRepository.MarkWatchedAsync(item.Movie.ProfileId, WatchProgressContentType.Movie, item.Movie.SourceMovieId);
            item.IsWatched = true;
        }
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

    // PlayerViewModel's auto-mark-on-completion only has a content key to persist against, not a
    // reference back to whichever MovieListItemViewModel is on screen for it - see
    // SeriesViewModel.RefreshWatchedFlagsAsync for the equivalent on the episodes side.
    private async Task RefreshWatchedFlagsAsync()
    {
        var watchedKeys = (await _watchedItemRepository.GetAllAsync())
            .Where(w => w.ContentType == WatchProgressContentType.Movie)
            .Select(w => (w.ProfileId, w.ContentKey))
            .ToHashSet();

        foreach (var group in _allGroups)
        {
            foreach (var movie in group.Movies)
            {
                movie.IsWatched = watchedKeys.Contains((movie.Movie.ProfileId, movie.Movie.SourceMovieId));
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

    // The ComboBox transiently nulls its own selection whenever FilteredCategories is swapped to a
    // new collection (see ApplyCategoryFilter), even though the new list still contains a matching
    // entry. Reacting to that null here is harmless on this page (DebounceApplyFilter is cheap and
    // debounced), but it's still not a real category change, so it's ignored for correctness -
    // GuideViewModel's equivalent guard is load-bearing (its handler triggers a full reload).
    partial void OnSelectedCategoryChanged(string value)
    {
        if (value is null)
        {
            return;
        }

        DebounceApplyFilter();
    }

    partial void OnCategoryFilterTextChanged(string value) => ApplyCategoryFilter();

    // Narrows the ComboBox's visible options as you type, without touching Categories (the source of
    // truth) or SelectedCategory itself. The current selection is always kept in the list even if it
    // doesn't match the filter, so typing to browse for something else never silently clears - and
    // never has the ComboBox null out - what's already selected.
    private void ApplyCategoryFilter()
    {
        var query = CategoryFilterText.Trim();
        var matching = Categories.Where(category =>
            string.IsNullOrEmpty(query) ||
            category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            category == SelectedCategory).ToList();

        // Only actually replace the collection if its contents changed - reassigning on every single
        // reload (even when nothing about the category list or filter actually changed) makes the
        // ComboBox reset and transiently null its own selection every time, which - on pages that
        // react to SelectedCategory by reloading - turns into a self-sustaining loop (see
        // GuideViewModel.OnSelectedCategoryChanged).
        if (!matching.SequenceEqual(FilteredCategories))
        {
            FilteredCategories = new ObservableCollection<string>(matching);
        }
    }

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
        var requestId = ++_loadRequestId;
        IsLoading = true;
        LoadError = null;
        try
        {
            var favoriteIds = await _favoriteRepository.GetFavoriteMovieIdsAsync();
            var watchedKeys = (await _watchedItemRepository.GetAllAsync())
                .Where(w => w.ContentType == WatchProgressContentType.Movie)
                .Select(w => (w.ProfileId, w.ContentKey))
                .ToHashSet();
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
                            .Select(m => new MovieListItemViewModel(m, favoriteIds.Contains(m.Id), watchedKeys.Contains((m.ProfileId, m.SourceMovieId))))
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

            if (requestId != _loadRequestId)
            {
                // Superseded by a newer LoadMoviesAsync call while these DB reads were in flight - see
                // _loadRequestId. Drop this stale pass instead of overwriting whatever the latest call
                // already produced (or is still producing).
                return;
            }

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
            ApplyCategoryFilter();

            ApplyFilter();

            // ApplyFilter mutates Rows (Clear + per-item Add), which the virtualized ItemsControl
            // doesn't finish re-realizing/rendering synchronously - that layout pass is queued, not
            // immediate. Without this, IsLoading flips back to false - hiding the spinner - a frame or
            // two before the list has actually caught up, which reads as "it says it's done but the
            // list is still updating" (same fix as GuideViewModel.LoadAsync).
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            LoadError = "Failed to load movies.";
            _logger.LogError(ex, "Failed to load movies");
        }
        finally
        {
            // Only the latest call gets to clear the spinner - if a newer LoadMoviesAsync started
            // while this one was still running, it's already the one driving IsLoading now.
            if (requestId == _loadRequestId)
            {
                IsLoading = false;
            }
        }
    }
}
