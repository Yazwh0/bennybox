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

public partial class SeriesViewModel : ViewModelBase
{
    private readonly IProfileRepository _profileRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly SeriesImportService _seriesImportService;
    private readonly ILogger<SeriesViewModel> _logger;

    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(300);

    private List<SeriesCategoryGroupViewModel> _allGroups = [];
    private CancellationTokenSource? _searchCts;
    private ProfileSource? _selectedSeriesProfile;

    public string Title => "Series";

    public PlayerViewModel Player { get; }

    // Flattened header+series rows for a single virtualized list, shown while browsing (see
    // CategoryHeaderRow) - same rationale as LiveTvViewModel.Rows.
    public ObservableCollection<object> Rows { get; } = [];

    // Flattened season-header+episode rows for the selected series, shown instead of Rows once a
    // series is opened.
    public ObservableCollection<object> Episodes { get; } = [];

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
    private string _searchText = string.Empty;

    public SeriesViewModel(
        PlayerViewModel player,
        IProfileRepository profileRepository,
        ISeriesRepository seriesRepository,
        SeriesImportService seriesImportService,
        ILogger<SeriesViewModel> logger)
    {
        Player = player;
        _profileRepository = profileRepository;
        _seriesRepository = seriesRepository;
        _seriesImportService = seriesImportService;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<ChannelsUpdatedMessage>(this, (_, _) => _ = LoadSeriesAsync());

        _ = LoadSeriesAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadSeriesAsync();

    [RelayCommand]
    private async Task SelectSeriesAsync(SeriesListItemViewModel? series)
    {
        if (series is null || _selectedSeriesProfile is null)
        {
            return;
        }

        SelectedSeries = series;
        Episodes.Clear();
        IsLoadingEpisodes = true;
        try
        {
            var episodes = await _seriesImportService.GetEpisodesAsync(_selectedSeriesProfile, series.Series);

            var rows = new List<object>();
            foreach (var seasonGroup in episodes.GroupBy(e => e.Season).OrderBy(g => g.Key))
            {
                rows.Add(new CategoryHeaderRow($"Season {seasonGroup.Key}"));
                rows.AddRange(seasonGroup.Select(e => new EpisodeListItemViewModel(e)));
            }

            foreach (var row in rows)
            {
                Episodes.Add(row);
            }
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
        if (episode is null)
        {
            return;
        }

        Player.PlayEpisode(episode.Episode);
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = ApplyFilterDebouncedAsync(value, cts.Token);
    }

    // Same rationale as LiveTvViewModel: debounce so we only filter once the user pauses typing, and
    // do the scan on a background thread so a large series library never blocks the UI.
    private async Task ApplyFilterDebouncedAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDebounceDelay, cancellationToken);

            var snapshot = _allGroups;
            var filtered = await Task.Run(() => Flatten(Filter(snapshot, query.Trim())), cancellationToken);

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
                HasNoSeries = Rows.Count == 0;
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke - just drop this pass.
        }
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var row in Flatten(Filter(_allGroups, SearchText.Trim())))
        {
            Rows.Add(row);
        }
        HasNoSeries = Rows.Count == 0;
    }

    private static List<SeriesCategoryGroupViewModel> Filter(List<SeriesCategoryGroupViewModel> groups, string query)
    {
        var result = new List<SeriesCategoryGroupViewModel>();
        foreach (var group in groups)
        {
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
            var profiles = await _profileRepository.GetAllAsync();

            // Series is Xtream-specific (no equivalent structured API for M3U) - only the first
            // Xtream profile with any imported series is used, mirroring how profiles are otherwise
            // handled per-page in this app.
            var profileData = new List<(ProfileSource Profile, IReadOnlyList<Category> Categories, IReadOnlyList<Series> SeriesList)>();
            foreach (var profile in profiles)
            {
                var categories = await _seriesRepository.GetCategoriesAsync(profile.Id);
                var seriesList = await _seriesRepository.GetSeriesAsync(profile.Id);
                if (seriesList.Count == 0)
                {
                    continue;
                }

                profileData.Add((profile, categories, seriesList));
                _selectedSeriesProfile = profile;
            }

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
                            .Select(s => new SeriesListItemViewModel(s))
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
