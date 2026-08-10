using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private const string AllSourcesLabel = "All Sources";
    private const string LastRefreshSecondsKey = "Series.LastRefreshSeconds";

    private readonly IProfileRepository _profileRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly SeriesImportService _seriesImportService;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IWatchedItemRepository _watchedItemRepository;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<SeriesViewModel> _logger;

    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(300);

    private List<SeriesCategoryGroupViewModel> _allGroups = [];
    private CancellationTokenSource? _searchCts;

    // See GuideViewModel's equivalent field for why this exists.
    private int? _lastRefreshSeconds;

    // See GuideViewModel._loadRequestId - LoadSeriesAsync now does a real network import on an
    // explicit Refresh (can take seconds), so two overlapping calls are a real possibility.
    private int _loadRequestId;

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

    // See MoviesViewModel.Sources for why this exists.
    public ObservableCollection<string> Sources { get; } = [AllSourcesLabel];

    [ObservableProperty]
    private string _selectedSource = AllSourcesLabel;

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
    private SeriesListItemViewModel? _selectedSeries;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingEpisodes;

    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    [ObservableProperty]
    private string? _loadError;

    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);

    // See GuideViewModel's equivalent property for why this exists and how it's bound.
    [ObservableProperty]
    private string? _refreshElapsedLabel;

    [ObservableProperty]
    private bool _hasNoSeries;

    [ObservableProperty]
    private bool _hasNoMatches;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = AllCategoriesLabel;

    [ObservableProperty]
    private string _categoryFilterText = string.Empty;

    public SeriesViewModel(
        PlayerViewModel player,
        IProfileRepository profileRepository,
        ISeriesRepository seriesRepository,
        SeriesImportService seriesImportService,
        IFavoriteRepository favoriteRepository,
        IWatchedItemRepository watchedItemRepository,
        ISettingsStore settingsStore,
        ILogger<SeriesViewModel> logger)
    {
        Player = player;
        _profileRepository = profileRepository;
        _seriesRepository = seriesRepository;
        _seriesImportService = seriesImportService;
        _favoriteRepository = favoriteRepository;
        _watchedItemRepository = watchedItemRepository;
        _settingsStore = settingsStore;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<ChannelsUpdatedMessage>(this, (_, _) => _ = LoadSeriesAsync());
        WeakReferenceMessenger.Default.Register<FavoritesUpdatedMessage>(this, (_, _) => _ = RefreshFavoriteFlagsAsync());
        WeakReferenceMessenger.Default.Register<WatchedStatusUpdatedMessage>(this, (_, _) => _ = RefreshWatchedFlagsAsync());

        _ = LoadSeriesAsync();
        _ = LoadLastRefreshSecondsAsync();
    }

    private async Task LoadLastRefreshSecondsAsync()
    {
        var stored = await _settingsStore.GetAsync(LastRefreshSecondsKey);
        if (int.TryParse(stored, out var seconds))
        {
            _lastRefreshSeconds = seconds;
        }
    }

    private string FormatRefreshElapsedLabel(TimeSpan elapsed)
    {
        var seconds = (int)elapsed.TotalSeconds;
        return _lastRefreshSeconds is { } last
            ? $"Refreshing... {seconds}s (last time: ~{last}s)"
            : $"Refreshing... {seconds}s";
    }

    // See LiveTvViewModel.RefreshAsync / GuideViewModel.RefreshAsync - an explicit Refresh press is the
    // one moment this page should actually go re-fetch series from the server, rather than just
    // re-reading whatever's already in the local cache.
    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        var stopwatch = Stopwatch.StartNew();
        // DispatcherTimer.Stop() only prevents *future* ticks from being scheduled - a tick already
        // queued on the UI thread at the moment Stop() is called can still fire afterward, which would
        // otherwise clobber the RefreshElapsedLabel = null reset below with stale "Refreshing... Ns"
        // text that then never clears. This flag makes that late tick a no-op.
        var isRefreshing = true;
        var elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        elapsedTimer.Tick += (_, _) =>
        {
            if (isRefreshing)
            {
                RefreshElapsedLabel = FormatRefreshElapsedLabel(stopwatch.Elapsed);
            }
        };
        RefreshElapsedLabel = FormatRefreshElapsedLabel(TimeSpan.Zero);
        elapsedTimer.Start();

        var failedProfiles = new List<string>();
        try
        {
            var profiles = await _profileRepository.GetAllAsync();
            foreach (var profile in profiles)
            {
                try
                {
                    await _seriesImportService.ImportAsync(profile);
                }
                catch (Exception ex)
                {
                    failedProfiles.Add(profile.Name);
                    _logger.LogWarning(ex, "Failed to refresh series for profile {ProfileName}", profile.Name);
                }
            }
        }
        finally
        {
            isRefreshing = false;
            elapsedTimer.Stop();
            RefreshElapsedLabel = null;
            _lastRefreshSeconds = (int)Math.Round(stopwatch.Elapsed.TotalSeconds);
            _ = _settingsStore.SetAsync(LastRefreshSecondsKey, _lastRefreshSeconds.Value.ToString());

            // LoadSeriesAsync resets LoadError to null on entry, so any failure message has to be
            // applied after it returns rather than before.
            await LoadSeriesAsync();
            if (failedProfiles.Count > 0)
            {
                LoadError = $"Couldn't refresh: {string.Join(", ", failedProfiles)}. Showing cached data.";
            }
        }
    }

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

    // Same rationale as OnSelectedCategoryChanged - a plain filter re-apply, not a full reload.
    partial void OnSelectedSourceChanged(string value)
    {
        if (value is null)
        {
            return;
        }

        DebounceApplyFilter();
    }

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
            var source = SelectedSource;
            var filtered = await Task.Run(() => Flatten(Filter(snapshot, query, category, source)), cancellationToken);

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
        foreach (var row in Flatten(Filter(_allGroups, SearchText.Trim(), SelectedCategory, SelectedSource)))
        {
            Rows.Add(row);
        }
        HasNoSeries = _allGroups.Count == 0;
        HasNoMatches = Rows.Count == 0 && _allGroups.Count > 0;
    }

    private static List<SeriesCategoryGroupViewModel> Filter(List<SeriesCategoryGroupViewModel> groups, string query, string category, string source)
    {
        var result = new List<SeriesCategoryGroupViewModel>();
        foreach (var group in groups)
        {
            if (category != AllCategoriesLabel && group.Name != category)
            {
                continue;
            }

            var matching = group.Series.Where(s =>
                (source == AllSourcesLabel || s.SourceName == source) &&
                (string.IsNullOrEmpty(query) || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

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
        var requestId = ++_loadRequestId;
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
                foreach (var (profile, categories, seriesList) in profileData)
                {
                    var seriesByCategory = seriesList.ToLookup(s => s.CategoryId);
                    foreach (var category in categories)
                    {
                        var categorySeries = seriesByCategory[category.Id]
                            .Select(s => new SeriesListItemViewModel(s, profile.Name, favoriteIds.Contains(s.Id), watchedSeriesKeys.Contains((s.ProfileId, s.SourceSeriesId))))
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

            if (requestId != _loadRequestId)
            {
                // Superseded by a newer LoadSeriesAsync call while these DB reads were in flight - see
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

            var selectedSource = SelectedSource;
            Sources.Clear();
            Sources.Add(AllSourcesLabel);
            foreach (var profileName in profileData.Select(p => p.Profile.Name).Distinct().OrderBy(n => n))
            {
                Sources.Add(profileName);
            }

            if (!Sources.Contains(selectedSource))
            {
                selectedSource = AllSourcesLabel;
            }
            SelectedSource = selectedSource;

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
            LoadError = "Failed to load series.";
            _logger.LogError(ex, "Failed to load series");
        }
        finally
        {
            // Only the latest call gets to clear the spinner - if a newer LoadSeriesAsync started
            // while this one was still running, it's already the one driving IsLoading now.
            if (requestId == _loadRequestId)
            {
                IsLoading = false;
            }
        }
    }
}
