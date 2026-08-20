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

// Mirrors MoviesViewModel - see that for the rationale behind most of this (debounced search,
// Sources filter, flattened header+item Rows). Simpler in one respect: a clip's detail is already
// final from the scan (see ClipListItemViewModel), so there's no on-demand "load details on select"
// branch to mirror from MoviesViewModel.SelectMovieAsync.
public partial class ClipsViewModel : ViewModelBase
{
    private const string AllCategoriesLabel = "All Categories";
    private const string AllSourcesLabel = "All Sources";
    private const string LastRefreshSecondsKey = "Clips.LastRefreshSeconds";

    private readonly IProfileRepository _profileRepository;
    private readonly IClipRepository _clipRepository;
    private readonly ClipImportService _clipImportService;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IWatchedItemRepository _watchedItemRepository;
    private readonly ISettingsStore _settingsStore;
    private readonly DownloadManager _downloadManager;
    private readonly IDownloadRepository _downloadRepository;
    private readonly ILogger<ClipsViewModel> _logger;

    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(300);

    private List<ClipCategoryGroupViewModel> _allGroups = [];
    private CancellationTokenSource? _searchCts;
    private int? _lastRefreshSeconds;
    private int _loadRequestId;

    // See MoviesViewModel._movieProfilesById - same rationale (a clip's download button needs the
    // right profile's credentials to actually fetch the file).
    private Dictionary<Guid, ProfileSource> _clipProfilesById = [];

    public string Title => "Clips";

    public PlayerViewModel Player { get; }

    public ObservableCollection<object> Rows { get; } = [];

    public ObservableCollection<string> Categories { get; } = [AllCategoriesLabel];

    public ObservableCollection<string> Sources { get; } = [AllSourcesLabel];

    [ObservableProperty]
    private string _selectedSource = AllSourcesLabel;

    [ObservableProperty]
    private ObservableCollection<string> _filteredCategories = [AllCategoriesLabel];

    [ObservableProperty]
    private ClipListItemViewModel? _selectedClip;

    [ObservableProperty]
    private bool _isLoading;

    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    [ObservableProperty]
    private string? _loadError;

    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);

    [ObservableProperty]
    private string? _refreshElapsedLabel;

    [ObservableProperty]
    private bool _hasNoClips;

    [ObservableProperty]
    private bool _hasNoMatches;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = AllCategoriesLabel;

    [ObservableProperty]
    private string _categoryFilterText = string.Empty;

    public ClipsViewModel(
        PlayerViewModel player,
        IProfileRepository profileRepository,
        IClipRepository clipRepository,
        ClipImportService clipImportService,
        IFavoriteRepository favoriteRepository,
        IWatchedItemRepository watchedItemRepository,
        ISettingsStore settingsStore,
        DownloadManager downloadManager,
        IDownloadRepository downloadRepository,
        ILogger<ClipsViewModel> logger)
    {
        Player = player;
        _profileRepository = profileRepository;
        _clipRepository = clipRepository;
        _clipImportService = clipImportService;
        _favoriteRepository = favoriteRepository;
        _watchedItemRepository = watchedItemRepository;
        _settingsStore = settingsStore;
        _downloadManager = downloadManager;
        _downloadRepository = downloadRepository;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<ChannelsUpdatedMessage>(this, (_, _) => _ = LoadClipsAsync());
        WeakReferenceMessenger.Default.Register<FavoritesUpdatedMessage>(this, (_, _) => _ = RefreshFavoriteFlagsAsync());
        WeakReferenceMessenger.Default.Register<WatchedStatusUpdatedMessage>(this, (_, _) => _ = RefreshWatchedFlagsAsync());
        _downloadManager.DownloadChanged += OnDownloadChanged;

        _ = LoadClipsAsync();
        _ = LoadLastRefreshSecondsAsync();
    }

    // See MoviesViewModel.OnDownloadChanged - same pattern.
    private void OnDownloadChanged(object? sender, Download download) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (download.ContentType != WatchProgressContentType.Clip)
            {
                return;
            }

            var match = _allGroups.SelectMany(g => g.Clips).FirstOrDefault(c =>
                c.Clip.ProfileId == download.OriginalProfileId && c.Clip.SourceMovieId == download.OriginalSourceId);
            if (match is null)
            {
                return;
            }

            match.DownloadState = download.Status switch
            {
                DownloadStatus.Queued => DownloadUiState.Queued,
                DownloadStatus.Downloading => DownloadUiState.Downloading,
                _ => DownloadUiState.NotDownloaded
            };
            match.DownloadProgress = download.TotalBytes is { } total && total > 0 ? (double)download.BytesDownloaded / total : 0;
        });

    [RelayCommand]
    private async Task DownloadAsync(ClipListItemViewModel? item)
    {
        if (item is null || !item.CanDownload || !_clipProfilesById.TryGetValue(item.Clip.ProfileId, out var profile))
        {
            return;
        }

        item.DownloadState = DownloadUiState.Queued;
        try
        {
            await _downloadManager.QueueClipDownloadAsync(profile, item.Clip);
        }
        catch (Exception ex)
        {
            item.DownloadState = DownloadUiState.NotDownloaded;
            _logger.LogError(ex, "Failed to queue download for clip {ClipName}", item.Name);
        }
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

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
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
                    await _clipImportService.ImportAsync(profile);
                }
                catch (Exception ex)
                {
                    failedProfiles.Add(profile.Name);
                    _logger.LogWarning(ex, "Failed to refresh clips for profile {ProfileName}", profile.Name);
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

            await LoadClipsAsync();
            if (failedProfiles.Count > 0)
            {
                LoadError = $"Couldn't refresh: {string.Join(", ", failedProfiles)}. Showing cached data.";
            }
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(ClipListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsFavorite)
        {
            await _favoriteRepository.RemoveClipAsync(item.Clip.Id);
            item.IsFavorite = false;
        }
        else
        {
            await _favoriteRepository.AddClipAsync(item.Clip.ProfileId, item.Clip.Id);
            item.IsFavorite = true;
        }

        WeakReferenceMessenger.Default.Send(new FavoritesUpdatedMessage());
    }

    [RelayCommand]
    private async Task ToggleWatchedAsync(ClipListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsWatched)
        {
            await _watchedItemRepository.MarkUnwatchedAsync(item.Clip.ProfileId, WatchProgressContentType.Clip, item.Clip.SourceMovieId);
            item.IsWatched = false;
        }
        else
        {
            await _watchedItemRepository.MarkWatchedAsync(item.Clip.ProfileId, WatchProgressContentType.Clip, item.Clip.SourceMovieId);
            item.IsWatched = true;
        }
    }

    private async Task RefreshFavoriteFlagsAsync()
    {
        var favoriteIds = await _favoriteRepository.GetFavoriteClipIdsAsync();
        foreach (var group in _allGroups)
        {
            foreach (var clip in group.Clips)
            {
                clip.IsFavorite = favoriteIds.Contains(clip.Clip.Id);
            }
        }
    }

    private async Task RefreshWatchedFlagsAsync()
    {
        var watchedKeys = (await _watchedItemRepository.GetAllAsync())
            .Where(w => w.ContentType == WatchProgressContentType.Clip)
            .Select(w => (w.ProfileId, w.ContentKey))
            .ToHashSet();

        foreach (var group in _allGroups)
        {
            foreach (var clip in group.Clips)
            {
                clip.IsWatched = watchedKeys.Contains((clip.Clip.ProfileId, clip.Clip.SourceMovieId));
            }
        }
    }

    [RelayCommand]
    private void SelectClip(ClipListItemViewModel? clip) => SelectedClip = clip;

    [RelayCommand]
    private void BackToList() => SelectedClip = null;

    [RelayCommand]
    private void PlayClip(ClipListItemViewModel? clip)
    {
        if (clip is null)
        {
            return;
        }

        Player.PlayClip(clip.Clip);
    }

    partial void OnSearchTextChanged(string value) => DebounceApplyFilter();

    partial void OnSelectedCategoryChanged(string value)
    {
        if (value is null)
        {
            return;
        }

        DebounceApplyFilter();
    }

    partial void OnCategoryFilterTextChanged(string value) => ApplyCategoryFilter();

    partial void OnSelectedSourceChanged(string value)
    {
        if (value is null)
        {
            return;
        }

        DebounceApplyFilter();
    }

    private void ApplyCategoryFilter()
    {
        var query = CategoryFilterText.Trim();
        var matching = Categories.Where(category =>
            string.IsNullOrEmpty(query) ||
            category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            category == SelectedCategory).ToList();

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
        }
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var row in Flatten(Filter(_allGroups, SearchText.Trim(), SelectedCategory, SelectedSource)))
        {
            Rows.Add(row);
        }
        HasNoClips = _allGroups.Count == 0;
        HasNoMatches = Rows.Count == 0 && _allGroups.Count > 0;
    }

    private static List<ClipCategoryGroupViewModel> Filter(List<ClipCategoryGroupViewModel> groups, string query, string category, string source)
    {
        var result = new List<ClipCategoryGroupViewModel>();
        foreach (var group in groups)
        {
            if (category != AllCategoriesLabel && group.Name != category)
            {
                continue;
            }

            var matching = group.Clips.Where(c =>
                (source == AllSourcesLabel || c.SourceName == source) &&
                (string.IsNullOrEmpty(query) || c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

            if (matching.Count == 0)
            {
                continue;
            }

            result.Add(new ClipCategoryGroupViewModel(group.Name, matching));
        }

        return result;
    }

    private static List<object> Flatten(List<ClipCategoryGroupViewModel> groups)
    {
        var rows = new List<object>();
        foreach (var group in groups)
        {
            rows.Add(new CategoryHeaderRow(group.Name));
            rows.AddRange(group.Clips);
        }

        return rows;
    }

    private async Task LoadClipsAsync()
    {
        var requestId = ++_loadRequestId;
        IsLoading = true;
        LoadError = null;
        try
        {
            var favoriteIds = await _favoriteRepository.GetFavoriteClipIdsAsync();
            var watchedKeys = (await _watchedItemRepository.GetAllAsync())
                .Where(w => w.ContentType == WatchProgressContentType.Clip)
                .Select(w => (w.ProfileId, w.ContentKey))
                .ToHashSet();
            var profiles = await _profileRepository.GetAllAsync();

            // See MoviesViewModel.LoadMoviesAsync's equivalent block - same rationale.
            var downloadsProfile = await _downloadManager.GetDownloadsProfileIfExistsAsync();
            var activeDownloads = (await _downloadRepository.GetAllAsync())
                .Where(d => d.Status is DownloadStatus.Queued or DownloadStatus.Downloading)
                .ToList();
            var downloadedTitles = downloadsProfile is null
                ? []
                : (await _clipRepository.GetClipsAsync(downloadsProfile.Id)).Select(c => DownloadRowSupport.Normalize(c.Name)).ToHashSet();

            var profileData = new List<(ProfileSource Profile, IReadOnlyList<Category> Categories, IReadOnlyList<Movie> ClipList)>();
            var clipProfilesById = new Dictionary<Guid, ProfileSource>();
            foreach (var profile in profiles)
            {
                var categories = await _clipRepository.GetCategoriesAsync(profile.Id);
                var clipList = await _clipRepository.GetClipsAsync(profile.Id);
                if (clipList.Count == 0)
                {
                    continue;
                }

                profileData.Add((profile, categories, clipList));
                clipProfilesById[profile.Id] = profile;
            }
            _clipProfilesById = clipProfilesById;

            _allGroups = await Task.Run(() =>
            {
                var groups = new List<ClipCategoryGroupViewModel>();
                foreach (var (profile, categories, clipList) in profileData)
                {
                    var clipsByCategory = clipList.ToLookup(c => c.CategoryId);
                    foreach (var category in categories)
                    {
                        var categoryClips = clipsByCategory[category.Id]
                            .Select(c =>
                            {
                                var isFromDownloadsProfile = downloadsProfile is not null && profile.Id == downloadsProfile.Id;
                                var item = new ClipListItemViewModel(c, profile.Name, favoriteIds.Contains(c.Id), watchedKeys.Contains((c.ProfileId, c.SourceMovieId)), isFromDownloadsProfile);
                                var (state, progress) = DownloadRowSupport.ComputeState(activeDownloads, downloadedTitles.Contains(DownloadRowSupport.Normalize(c.Name)), c.ProfileId, WatchProgressContentType.Clip, c.SourceMovieId);
                                item.DownloadState = state;
                                item.DownloadProgress = progress;
                                return item;
                            })
                            .ToList();
                        if (categoryClips.Count == 0)
                        {
                            continue;
                        }

                        groups.Add(new ClipCategoryGroupViewModel(category.Name, categoryClips));
                    }
                }

                return groups.OrderBy(g => g.Name).ToList();
            });

            if (requestId != _loadRequestId)
            {
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

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            LoadError = "Failed to load clips.";
            _logger.LogError(ex, "Failed to load clips");
        }
        finally
        {
            if (requestId == _loadRequestId)
            {
                IsLoading = false;
            }
        }
    }
}
