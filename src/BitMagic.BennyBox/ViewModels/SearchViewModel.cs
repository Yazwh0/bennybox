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

// Independent of LiveTvViewModel/SeriesViewModel/MoviesViewModel - same "own independent snapshot"
// rationale as FavoritesViewModel, so searching here never touches what's shown on those pages.
public partial class SearchViewModel : ViewModelBase
{
    private readonly IProfileRepository _profileRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IEpgRepository _epgRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly IMovieRepository _movieRepository;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IWatchedItemRepository _watchedItemRepository;
    private readonly ILogger<SearchViewModel> _logger;

    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(300);

    private List<ChannelListItemViewModel> _allChannels = [];
    private List<SeriesListItemViewModel> _allSeries = [];
    private List<MovieListItemViewModel> _allMovies = [];
    private CancellationTokenSource? _searchCts;
    private int _loadRequestId;

    public string Title => "Search";

    public PlayerViewModel Player { get; }

    // Flattened "Live TV"/"Series"/"Movies" header + result rows for a single virtualized list -
    // same rationale as every other page's Rows. Empty until a query is typed (see HasEmptyQuery).
    public ObservableCollection<object> Rows { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    [ObservableProperty]
    private string? _loadError;

    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);

    [ObservableProperty]
    private bool _hasNoMatches;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public bool HasEmptyQuery => string.IsNullOrWhiteSpace(SearchText);

    public SearchViewModel(
        PlayerViewModel player,
        IProfileRepository profileRepository,
        IChannelRepository channelRepository,
        IEpgRepository epgRepository,
        ISeriesRepository seriesRepository,
        IMovieRepository movieRepository,
        IFavoriteRepository favoriteRepository,
        IWatchedItemRepository watchedItemRepository,
        ILogger<SearchViewModel> logger)
    {
        Player = player;
        _profileRepository = profileRepository;
        _channelRepository = channelRepository;
        _epgRepository = epgRepository;
        _seriesRepository = seriesRepository;
        _movieRepository = movieRepository;
        _favoriteRepository = favoriteRepository;
        _watchedItemRepository = watchedItemRepository;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<ChannelsUpdatedMessage>(this, (_, _) => _ = LoadAsync());
        WeakReferenceMessenger.Default.Register<FavoritesUpdatedMessage>(this, (_, _) => _ = RefreshFavoriteFlagsAsync());
        WeakReferenceMessenger.Default.Register<WatchedStatusUpdatedMessage>(this, (_, _) => _ = RefreshWatchedFlagsAsync());

        _ = LoadAsync();
    }

    [RelayCommand]
    private void SelectChannel(Channel? channel)
    {
        if (channel is not null)
        {
            Player.PlayChannel(channel);
        }
    }

    // Opening a series/movie switches to its own page rather than showing details here - same
    // OpenSeriesMessage/OpenMovieMessage plumbing FavoritesViewModel already uses, which
    // MainWindowViewModel already listens for.
    [RelayCommand]
    private void SelectSeries(SeriesListItemViewModel? item)
    {
        if (item is not null)
        {
            WeakReferenceMessenger.Default.Send(new OpenSeriesMessage(item.Series));
        }
    }

    [RelayCommand]
    private void SelectMovie(MovieListItemViewModel? item)
    {
        if (item is not null)
        {
            WeakReferenceMessenger.Default.Send(new OpenMovieMessage(item.Movie));
        }
    }

    // Distinct per-type names (rather than one shared "ToggleFavoriteCommand") since this single
    // class hosts three different row types at once - same pattern FavoritesViewModel already uses
    // for its RemoveFavoriteCommand/RemoveSeriesFavoriteCommand/RemoveMovieFavoriteCommand.
    [RelayCommand]
    private async Task ToggleChannelFavoriteAsync(ChannelListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsFavorite)
        {
            await _favoriteRepository.RemoveAsync(item.Channel.Id);
            item.IsFavorite = false;
        }
        else
        {
            await _favoriteRepository.AddAsync(item.Channel.ProfileId, item.Channel.Id);
            item.IsFavorite = true;
        }

        WeakReferenceMessenger.Default.Send(new FavoritesUpdatedMessage());
    }

    [RelayCommand]
    private async Task ToggleSeriesFavoriteAsync(SeriesListItemViewModel? item)
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

    [RelayCommand]
    private async Task ToggleMovieFavoriteAsync(MovieListItemViewModel? item)
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
    private async Task ToggleMovieWatchedAsync(MovieListItemViewModel? item)
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

    private async Task RefreshFavoriteFlagsAsync()
    {
        var favoriteChannelIds = await _favoriteRepository.GetFavoriteChannelIdsAsync();
        var favoriteSeriesIds = await _favoriteRepository.GetFavoriteSeriesIdsAsync();
        var favoriteMovieIds = await _favoriteRepository.GetFavoriteMovieIdsAsync();

        foreach (var channel in _allChannels)
        {
            channel.IsFavorite = favoriteChannelIds.Contains(channel.Channel.Id);
        }
        foreach (var series in _allSeries)
        {
            series.IsFavorite = favoriteSeriesIds.Contains(series.Series.Id);
        }
        foreach (var movie in _allMovies)
        {
            movie.IsFavorite = favoriteMovieIds.Contains(movie.Movie.Id);
        }
    }

    private async Task RefreshWatchedFlagsAsync()
    {
        var watchedItems = await _watchedItemRepository.GetAllAsync();

        var watchedSeriesKeys = watchedItems
            .Where(w => w.ContentType == WatchProgressContentType.Series)
            .Select(w => (w.ProfileId, w.ContentKey))
            .ToHashSet();
        foreach (var series in _allSeries)
        {
            series.IsWatched = watchedSeriesKeys.Contains((series.Series.ProfileId, series.Series.SourceSeriesId));
        }

        var watchedMovieKeys = watchedItems
            .Where(w => w.ContentType == WatchProgressContentType.Movie)
            .Select(w => (w.ProfileId, w.ContentKey))
            .ToHashSet();
        foreach (var movie in _allMovies)
        {
            movie.IsWatched = watchedMovieKeys.Contains((movie.Movie.ProfileId, movie.Movie.SourceMovieId));
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasEmptyQuery));
        DebounceApplyFilter();
    }

    private void DebounceApplyFilter()
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = ApplyFilterDebouncedAsync(cts.Token);
    }

    // Same rationale as LiveTvViewModel/SeriesViewModel/MoviesViewModel: debounce so a large combined
    // library isn't re-scanned on every keystroke, and do the scan off the UI thread.
    private async Task ApplyFilterDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDebounceDelay, cancellationToken);

            var channels = _allChannels;
            var series = _allSeries;
            var movies = _allMovies;
            var query = SearchText.Trim();
            var filtered = await Task.Run(() => Flatten(channels, series, movies, query), cancellationToken);

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
                HasNoMatches = Rows.Count == 0 && !string.IsNullOrEmpty(query);
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke - just drop this pass.
        }
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        Rows.Clear();
        foreach (var row in Flatten(_allChannels, _allSeries, _allMovies, query))
        {
            Rows.Add(row);
        }
        HasNoMatches = Rows.Count == 0 && !string.IsNullOrEmpty(query);
    }

    // An empty query intentionally produces no rows at all - searching implies you've typed
    // something, rather than dumping the entire combined catalog the moment the page opens.
    private static List<object> Flatten(
        List<ChannelListItemViewModel> channels, List<SeriesListItemViewModel> series, List<MovieListItemViewModel> movies, string query)
    {
        var rows = new List<object>();
        if (string.IsNullOrEmpty(query))
        {
            return rows;
        }

        var matchingChannels = channels.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matchingChannels.Count > 0)
        {
            rows.Add(new CategoryHeaderRow("Live TV"));
            rows.AddRange(matchingChannels);
        }

        var matchingSeries = series.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matchingSeries.Count > 0)
        {
            rows.Add(new CategoryHeaderRow("Series"));
            rows.AddRange(matchingSeries);
        }

        var matchingMovies = movies.Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matchingMovies.Count > 0)
        {
            rows.Add(new CategoryHeaderRow("Movies"));
            rows.AddRange(matchingMovies);
        }

        return rows;
    }

    private async Task LoadAsync()
    {
        var requestId = ++_loadRequestId;
        IsLoading = true;
        LoadError = null;
        try
        {
            var favoriteChannelIds = await _favoriteRepository.GetFavoriteChannelIdsAsync();
            var favoriteSeriesIds = await _favoriteRepository.GetFavoriteSeriesIdsAsync();
            var favoriteMovieIds = await _favoriteRepository.GetFavoriteMovieIdsAsync();
            var watchedItems = await _watchedItemRepository.GetAllAsync();
            var watchedSeriesKeys = watchedItems
                .Where(w => w.ContentType == WatchProgressContentType.Series)
                .Select(w => (w.ProfileId, w.ContentKey))
                .ToHashSet();
            var watchedMovieKeys = watchedItems
                .Where(w => w.ContentType == WatchProgressContentType.Movie)
                .Select(w => (w.ProfileId, w.ContentKey))
                .ToHashSet();
            var profiles = await _profileRepository.GetAllAsync();
            var nowUtc = DateTime.UtcNow;

            var channels = new List<ChannelListItemViewModel>();
            var series = new List<SeriesListItemViewModel>();
            var movies = new List<MovieListItemViewModel>();

            foreach (var profile in profiles)
            {
                var profileChannels = await _channelRepository.GetChannelsAsync(profile.Id);
                if (profileChannels.Count > 0)
                {
                    var nowNext = await _epgRepository.GetNowNextAsync(profile.Id, nowUtc);
                    foreach (var channel in profileChannels)
                    {
                        var nowTitle = !string.IsNullOrEmpty(channel.TvgId) && nowNext.TryGetValue(channel.TvgId, out var entry)
                            ? entry.Now?.Title
                            : null;
                        channels.Add(new ChannelListItemViewModel(channel, nowTitle, favoriteChannelIds.Contains(channel.Id)));
                    }
                }

                var profileSeries = await _seriesRepository.GetSeriesAsync(profile.Id);
                series.AddRange(profileSeries.Select(s => new SeriesListItemViewModel(
                    s, favoriteSeriesIds.Contains(s.Id), watchedSeriesKeys.Contains((s.ProfileId, s.SourceSeriesId)))));

                var profileMovies = await _movieRepository.GetMoviesAsync(profile.Id);
                movies.AddRange(profileMovies.Select(m => new MovieListItemViewModel(
                    m, favoriteMovieIds.Contains(m.Id), watchedMovieKeys.Contains((m.ProfileId, m.SourceMovieId)))));
            }

            if (requestId != _loadRequestId)
            {
                // Superseded by a newer LoadAsync call while these DB reads were in flight - drop this
                // stale pass instead of overwriting whatever the latest call already produced.
                return;
            }

            _allChannels = channels;
            _allSeries = series;
            _allMovies = movies;

            ApplyFilter();

            // ApplyFilter mutates Rows (Clear + per-item Add), which the virtualized ItemsControl
            // doesn't finish re-realizing/rendering synchronously - see LiveTvViewModel.LoadChannelsAsync
            // for why this matters for IsLoading's timing.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            LoadError = "Failed to load search data.";
            _logger.LogError(ex, "Failed to load search data");
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
