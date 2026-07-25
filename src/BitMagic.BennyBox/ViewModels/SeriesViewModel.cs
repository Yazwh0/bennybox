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

public partial class SeriesViewModel : ViewModelBase
{
    private const string AllCategoriesLabel = "All Categories";

    private readonly IProfileRepository _profileRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly SeriesImportService _seriesImportService;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IWatchedItemRepository _watchedItemRepository;
    private readonly ILogger<SeriesViewModel> _logger;

    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(300);

    private List<SeriesCategoryGroupViewModel> _allGroups = [];
    private CancellationTokenSource? _searchCts;

    // Keyed by ProfileSource.Id so episodes are always fetched using the credentials of whichever
    // profile actually owns the clicked series - a single shared "current profile" field would silently
    // use the wrong (e.g. last-loaded) profile's credentials whenever more than one Xtream profile has
    // series, since series across profiles are merged into one browsing list.
    private Dictionary<Guid, ProfileSource> _seriesProfilesById = [];

    public string Title => "Series";

    public PlayerViewModel Player { get; }

    // Flattened header+series rows for a single virtualized list, shown while browsing (see
    // CategoryHeaderRow) - same rationale as LiveTvViewModel.Rows.
    public ObservableCollection<object> Rows { get; } = [];

    // Flattened season-header+episode rows for the selected series, shown instead of Rows once a
    // series is opened.
    public ObservableCollection<object> Episodes { get; } = [];

    public ObservableCollection<string> Categories { get; } = [AllCategoriesLabel];

    [ObservableProperty]
    private SeriesListItemViewModel? _selectedSeries;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingEpisodes;

    [ObservableProperty]
    private string? _loadError;

    [ObservableProperty]
    private bool _hasNoSeries;

    [ObservableProperty]
    private bool _hasNoMatches;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = AllCategoriesLabel;

    public SeriesViewModel(
        PlayerViewModel player,
        IProfileRepository profileRepository,
        ISeriesRepository seriesRepository,
        SeriesImportService seriesImportService,
        IFavoriteRepository favoriteRepository,
        IWatchedItemRepository watchedItemRepository,
        ILogger<SeriesViewModel> logger)
    {
        Player = player;
        _profileRepository = profileRepository;
        _seriesRepository = seriesRepository;
        _seriesImportService = seriesImportService;
        _favoriteRepository = favoriteRepository;
        _watchedItemRepository = watchedItemRepository;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<ChannelsUpdatedMessage>(this, (_, _) => _ = LoadSeriesAsync());
        WeakReferenceMessenger.Default.Register<FavoritesUpdatedMessage>(this, (_, _) => _ = RefreshFavoriteFlagsAsync());
        WeakReferenceMessenger.Default.Register<WatchedStatusUpdatedMessage>(this, (_, _) => _ = RefreshWatchedFlagsAsync());

        _ = LoadSeriesAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadSeriesAsync();

    [RelayCommand]
    private async Task ToggleFavoriteAsync(SeriesListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsFavorite)
        {
            await _favoriteRepository.RemoveSeriesAsync(item.Series.Id);
            item.IsFavorite = false;
        }
        else
        {
            await _favoriteRepository.AddSeriesAsync(item.Series.ProfileId, item.Series.Id);
            item.IsFavorite = true;
        }

        WeakReferenceMessenger.Default.Send(new FavoritesUpdatedMessage());
    }

    // A favorite can be toggled from this page, Favorites, or nowhere else (Guide/Live TV don't show
    // series) - but Favorites removing one should still update this page's stars without a full
    // reload (which would re-fetch every series from the database just to flip some booleans).
    private async Task RefreshFavoriteFlagsAsync()
    {
        var favoriteIds = await _favoriteRepository.GetFavoriteSeriesIdsAsync();
        foreach (var group in _allGroups)
        {
            foreach (var series in group.Series)
            {
                series.IsFavorite = favoriteIds.Contains(series.Series.Id);
            }
        }
    }

    // PlayerViewModel's auto-mark-on-completion only has a content key to persist against - it can't
    // reach into whichever EpisodeListItemViewModel is currently on screen the way the manual toggle
    // command does, so it broadcasts WatchedStatusUpdatedMessage instead and this re-derives both the
    // open episode list's ticks and the whole browse list's series-level dimming from the database.
    private async Task RefreshWatchedFlagsAsync()
    {
        var watchedItems = await _watchedItemRepository.GetAllAsync();

        var watchedSeriesKeys = watchedItems
            .Where(w => w.ContentType == WatchProgressContentType.Series)
            .Select(w => (w.ProfileId, w.ContentKey))
            .ToHashSet();

        foreach (var group in _allGroups)
        {
            foreach (var series in group.Series)
            {
                series.IsWatched = watchedSeriesKeys.Contains((series.Series.ProfileId, series.Series.SourceSeriesId));
            }
        }

        if (SelectedSeries is not null)
        {
            var episodeWatchedKeys = watchedItems
                .Where(w => w.ContentType == WatchProgressContentType.Episode && w.ProfileId == SelectedSeries.Series.ProfileId)
                .Select(w => w.ContentKey)
                .ToHashSet();

            foreach (var episode in Episodes.OfType<EpisodeListItemViewModel>())
            {
                episode.IsWatched = episodeWatchedKeys.Contains(ContentKeys.ForEpisode(SelectedSeries.Series.SourceSeriesId, episode.Episode.SourceEpisodeId));
            }

            // Recomputed last so it's authoritative for the currently-open series specifically - it
            // may persist a change (e.g. the episode that just finished was the last unwatched one)
            // that the bulk refresh above, using an older snapshot, wouldn't yet reflect.
            await RecomputeSeriesWatchedStatusAsync(SelectedSeries);
        }
    }

    [RelayCommand]
    private async Task SelectSeriesAsync(SeriesListItemViewModel? series)
    {
        if (series is null || !_seriesProfilesById.TryGetValue(series.Series.ProfileId, out var profile))
        {
            return;
        }

        SelectedSeries = series;
        Episodes.Clear();
        IsLoadingEpisodes = true;
        try
        {
            var episodes = await _seriesImportService.GetEpisodesAsync(profile, series.Series);
            var watchedKeys = (await _watchedItemRepository.GetAllAsync())
                .Where(w => w.ContentType == WatchProgressContentType.Episode && w.ProfileId == profile.Id)
                .Select(w => w.ContentKey)
                .ToHashSet();

            var rows = new List<object>();
            foreach (var seasonGroup in episodes.GroupBy(e => e.Season).OrderBy(g => g.Key))
            {
                rows.Add(new CategoryHeaderRow($"Season {seasonGroup.Key}"));
                rows.AddRange(seasonGroup.Select(e => new EpisodeListItemViewModel(
                    e, watchedKeys.Contains(ContentKeys.ForEpisode(series.Series.SourceSeriesId, e.SourceEpisodeId)))));
            }

            foreach (var row in rows)
            {
                Episodes.Add(row);
            }

            await RecomputeSeriesWatchedStatusAsync(series);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load episodes for series {SeriesName}", series.Name);
        }
        finally
        {
            IsLoadingEpisodes = false;
        }
    }

    [RelayCommand]
    private void BackToList()
    {
        SelectedSeries = null;
        Episodes.Clear();
    }

    [RelayCommand]
    private void PlayEpisode(EpisodeListItemViewModel? episode)
    {
        if (episode is null || SelectedSeries is null)
        {
            return;
        }

        Player.PlayEpisode(episode.Episode, SelectedSeries.Series);
    }

    [RelayCommand]
    private async Task ToggleEpisodeWatchedAsync(EpisodeListItemViewModel? episode)
    {
        if (episode is null || SelectedSeries is null)
        {
            return;
        }

        var profileId = SelectedSeries.Series.ProfileId;
        var contentKey = ContentKeys.ForEpisode(SelectedSeries.Series.SourceSeriesId, episode.Episode.SourceEpisodeId);

        if (episode.IsWatched)
        {
            await _watchedItemRepository.MarkUnwatchedAsync(profileId, WatchProgressContentType.Episode, contentKey);
            episode.IsWatched = false;
        }
        else
        {
            await _watchedItemRepository.MarkWatchedAsync(profileId, WatchProgressContentType.Episode, contentKey);
            episode.IsWatched = true;
        }

        await RecomputeSeriesWatchedStatusAsync(SelectedSeries);
    }

    // A series counts as watched once every one of its episodes does - only knowable once the full
    // episode list has actually been fetched (see SelectSeriesAsync), so this recomputes and persists
    // the series-level flag from whatever's currently loaded in Episodes rather than trying to derive
    // it for series the user hasn't opened yet.
    private async Task RecomputeSeriesWatchedStatusAsync(SeriesListItemViewModel series)
    {
        var episodeVms = Episodes.OfType<EpisodeListItemViewModel>().ToList();
        if (episodeVms.Count == 0)
        {
            return;
        }

        var allWatched = episodeVms.All(e => e.IsWatched);
        if (allWatched == series.IsWatched)
        {
            return;
        }

        if (allWatched)
        {
            await _watchedItemRepository.MarkWatchedAsync(series.Series.ProfileId, WatchProgressContentType.Series, series.Series.SourceSeriesId);
        }
        else
        {
            await _watchedItemRepository.MarkUnwatchedAsync(series.Series.ProfileId, WatchProgressContentType.Series, series.Series.SourceSeriesId);
        }

        series.IsWatched = allWatched;
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

    // Same rationale as LiveTvViewModel: debounce so we only filter once the user pauses typing, and
    // do the scan on a background thread so a large series library never blocks the UI.
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
        HasNoSeries = _allGroups.Count == 0;
        HasNoMatches = Rows.Count == 0 && _allGroups.Count > 0;
    }

    private static List<SeriesCategoryGroupViewModel> Filter(List<SeriesCategoryGroupViewModel> groups, string query, string category)
    {
        var result = new List<SeriesCategoryGroupViewModel>();
        foreach (var group in groups)
        {
            if (category != AllCategoriesLabel && group.Name != category)
            {
                continue;
            }

            var matching = string.IsNullOrEmpty(query)
                ? group.Series
                : group.Series.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matching.Count == 0)
            {
                continue;
            }

            result.Add(new SeriesCategoryGroupViewModel(group.Name, matching));
        }

        return result;
    }

    private static List<object> Flatten(List<SeriesCategoryGroupViewModel> groups)
    {
        var rows = new List<object>();
        foreach (var group in groups)
        {
            rows.Add(new CategoryHeaderRow(group.Name));
            rows.AddRange(group.Series);
        }

        return rows;
    }

    private async Task LoadSeriesAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var favoriteIds = await _favoriteRepository.GetFavoriteSeriesIdsAsync();
            var watchedSeriesKeys = (await _watchedItemRepository.GetAllAsync())
                .Where(w => w.ContentType == WatchProgressContentType.Series)
                .Select(w => (w.ProfileId, w.ContentKey))
                .ToHashSet();
            var profiles = await _profileRepository.GetAllAsync();

            // Series is Xtream-specific (no equivalent structured API for M3U) - every Xtream profile
            // with any imported series contributes to the browsing list, same as Live TV merges
            // channels across all profiles.
            var profileData = new List<(ProfileSource Profile, IReadOnlyList<Category> Categories, IReadOnlyList<Series> SeriesList)>();
            var seriesProfilesById = new Dictionary<Guid, ProfileSource>();
            foreach (var profile in profiles)
            {
                var categories = await _seriesRepository.GetCategoriesAsync(profile.Id);
                var seriesList = await _seriesRepository.GetSeriesAsync(profile.Id);
                if (seriesList.Count == 0)
                {
                    continue;
                }

                profileData.Add((profile, categories, seriesList));
                seriesProfilesById[profile.Id] = profile;
            }
            _seriesProfilesById = seriesProfilesById;

            // Grouping/sorting/object-construction can be real CPU work for a large library - keep it
            // off the UI thread, same rationale as LiveTvViewModel.
            _allGroups = await Task.Run(() =>
            {
                var groups = new List<SeriesCategoryGroupViewModel>();
                foreach (var (_, categories, seriesList) in profileData)
                {
                    var seriesByCategory = seriesList.ToLookup(s => s.CategoryId);
                    foreach (var category in categories)
                    {
                        var categorySeries = seriesByCategory[category.Id]
                            .Select(s => new SeriesListItemViewModel(s, favoriteIds.Contains(s.Id), watchedSeriesKeys.Contains((s.ProfileId, s.SourceSeriesId))))
                            .ToList();
                        if (categorySeries.Count == 0)
                        {
                            continue;
                        }

                        groups.Add(new SeriesCategoryGroupViewModel(category.Name, categorySeries));
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
            LoadError = "Failed to load series.";
            _logger.LogError(ex, "Failed to load series");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
