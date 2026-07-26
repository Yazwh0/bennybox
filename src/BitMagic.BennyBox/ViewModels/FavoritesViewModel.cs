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

public partial class FavoritesViewModel : ViewModelBase
{
    private readonly IProfileRepository _profileRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IEpgRepository _epgRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly IMovieRepository _movieRepository;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly IWatchProgressRepository _watchProgressRepository;
    private readonly ILogger<FavoritesViewModel> _logger;

    public string Title => "Favorites";

    public PlayerViewModel Player { get; }

    // Flattened "Channels"/"Series"/"Movies" headers + favorited items - either section is omitted
    // entirely if empty. Same flattened-list rationale as LiveTv/Guide/Series (see CategoryHeaderRow),
    // even though favorites are usually few enough not to strictly need virtualization - it keeps
    // this page's structure consistent with the rest of the app.
    public ObservableCollection<object> Rows { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _loadError;

    [ObservableProperty]
    private bool _hasNoFavorites;

    public FavoritesViewModel(
        PlayerViewModel player,
        IProfileRepository profileRepository,
        IChannelRepository channelRepository,
        IEpgRepository epgRepository,
        ISeriesRepository seriesRepository,
        IMovieRepository movieRepository,
        IFavoriteRepository favoriteRepository,
        IWatchProgressRepository watchProgressRepository,
        ILogger<FavoritesViewModel> logger)
    {
        Player = player;
        _profileRepository = profileRepository;
        _channelRepository = channelRepository;
        _epgRepository = epgRepository;
        _seriesRepository = seriesRepository;
        _movieRepository = movieRepository;
        _favoriteRepository = favoriteRepository;
        _watchProgressRepository = watchProgressRepository;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<FavoritesUpdatedMessage>(this, (_, _) => _ = LoadAsync());
        WeakReferenceMessenger.Default.Register<ChannelsUpdatedMessage>(this, (_, _) => _ = LoadAsync());
        // Playback progress changes (a stop/switch, or completion) fire far more often than actual
        // favorite add/remove - a full LoadAsync() reload on every one flickers the whole page (see
        // Rows.Clear() below) for a change that's scoped to the "Continue Watching" section, so this
        // gets a narrower handler that only splices that section back in.
        WeakReferenceMessenger.Default.Register<WatchProgressUpdatedMessage>(this, (_, _) => _ = RefreshContinueWatchingAsync());

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

    [RelayCommand]
    private async Task RemoveFavoriteAsync(ChannelListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await _favoriteRepository.RemoveAsync(item.Channel.Id);
        WeakReferenceMessenger.Default.Send(new FavoritesUpdatedMessage());
    }

    // Opening a favorited series/movie switches to its own page rather than showing details here -
    // Favorites doesn't duplicate that browsing UI, it just gets you to it quickly.
    [RelayCommand]
    private void SelectSeries(SeriesListItemViewModel? item)
    {
        if (item is not null)
        {
            WeakReferenceMessenger.Default.Send(new OpenSeriesMessage(item.Series));
        }
    }

    [RelayCommand]
    private async Task RemoveSeriesFavoriteAsync(SeriesListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await _favoriteRepository.RemoveSeriesAsync(item.Series.Id);
        WeakReferenceMessenger.Default.Send(new FavoritesUpdatedMessage());
    }

    [RelayCommand]
    private void SelectMovie(MovieListItemViewModel? item)
    {
        if (item is not null)
        {
            WeakReferenceMessenger.Default.Send(new OpenMovieMessage(item.Movie));
        }
    }

    [RelayCommand]
    private async Task RemoveMovieFavoriteAsync(MovieListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await _favoriteRepository.RemoveMovieAsync(item.Movie.Id);
        WeakReferenceMessenger.Default.Send(new FavoritesUpdatedMessage());
    }

    [RelayCommand]
    private void PlayWatchProgress(WatchProgressItemViewModel? item)
    {
        if (item is not null)
        {
            Player.ResumeFromProgress(item.Progress);
        }
    }

    [RelayCommand]
    private async Task RemoveWatchProgressAsync(WatchProgressItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await _watchProgressRepository.RemoveAsync(item.Progress.ProfileId, item.Progress.ContentType, item.Progress.ContentKey);

        // Targeted removal instead of a full reload - removing one row doesn't need every other
        // favorite/channel/series/movie row on the page to be torn down and rebuilt.
        var index = Rows.IndexOf(item);
        if (index >= 0)
        {
            Rows.RemoveAt(index);
            var wasLastInSection = index >= Rows.Count || Rows[index] is not WatchProgressItemViewModel;
            if (index > 0 && wasLastInSection && Rows[index - 1] is CategoryHeaderRow { Name: "Continue Watching" })
            {
                Rows.RemoveAt(index - 1);
            }
        }

        HasNoFavorites = Rows.Count == 0;
    }

    // Playback progress changes far more often than favorites do, so this only re-fetches and
    // splices the "Continue Watching" section - which is always first, per LoadAsync's ordering -
    // leaving the Channels/Series/Movies rows below it untouched.
    private async Task RefreshContinueWatchingAsync()
    {
        IReadOnlyList<WatchProgress> recentProgress;
        try
        {
            recentProgress = await _watchProgressRepository.GetRecentAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh continue watching");
            return;
        }

        var oldCount = 0;
        if (Rows.Count > 0 && Rows[0] is CategoryHeaderRow { Name: "Continue Watching" })
        {
            oldCount = 1;
            while (oldCount < Rows.Count && Rows[oldCount] is WatchProgressItemViewModel)
            {
                oldCount++;
            }
        }

        for (var i = oldCount - 1; i >= 0; i--)
        {
            Rows.RemoveAt(i);
        }

        if (recentProgress.Count > 0)
        {
            Rows.Insert(0, new CategoryHeaderRow("Continue Watching"));
            for (var i = 0; i < recentProgress.Count; i++)
            {
                Rows.Insert(1 + i, new WatchProgressItemViewModel(recentProgress[i]));
            }
        }

        HasNoFavorites = Rows.Count == 0;
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var favoriteChannelIds = await _favoriteRepository.GetFavoriteChannelIdsAsync();
            var favoriteSeriesIds = await _favoriteRepository.GetFavoriteSeriesIdsAsync();
            var favoriteMovieIds = await _favoriteRepository.GetFavoriteMovieIdsAsync();
            var recentProgress = await _watchProgressRepository.GetRecentAsync();
            var profiles = await _profileRepository.GetAllAsync();

            var channelItems = new List<ChannelListItemViewModel>();
            var seriesItems = new List<SeriesListItemViewModel>();
            var movieItems = new List<MovieListItemViewModel>();

            foreach (var profile in profiles)
            {
                var channels = await _channelRepository.GetChannelsAsync(profile.Id);
                var favoriteChannels = channels.Where(c => favoriteChannelIds.Contains(c.Id)).ToList();
                if (favoriteChannels.Count > 0)
                {
                    var nowNext = await _epgRepository.GetNowNextAsync(profile.Id, DateTime.UtcNow);
                    foreach (var channel in favoriteChannels)
                    {
                        var nowTitle = !string.IsNullOrEmpty(channel.TvgId) && nowNext.TryGetValue(channel.TvgId, out var entry)
                            ? entry.Now?.Title
                            : null;
                        channelItems.Add(new ChannelListItemViewModel(channel, nowTitle, isFavorite: true));
                    }
                }

                var series = await _seriesRepository.GetSeriesAsync(profile.Id);
                seriesItems.AddRange(series
                    .Where(s => favoriteSeriesIds.Contains(s.Id))
                    .Select(s => new SeriesListItemViewModel(s, isFavorite: true)));

                var movies = await _movieRepository.GetMoviesAsync(profile.Id);
                movieItems.AddRange(movies
                    .Where(m => favoriteMovieIds.Contains(m.Id))
                    .Select(m => new MovieListItemViewModel(m, isFavorite: true)));
            }

            Rows.Clear();
            if (recentProgress.Count > 0)
            {
                Rows.Add(new CategoryHeaderRow("Continue Watching"));
                foreach (var progress in recentProgress)
                {
                    Rows.Add(new WatchProgressItemViewModel(progress));
                }
            }
            if (channelItems.Count > 0)
            {
                Rows.Add(new CategoryHeaderRow("Channels"));
                foreach (var item in channelItems.OrderBy(i => i.Name))
                {
                    Rows.Add(item);
                }
            }
            if (seriesItems.Count > 0)
            {
                Rows.Add(new CategoryHeaderRow("Series"));
                foreach (var item in seriesItems.OrderBy(i => i.Name))
                {
                    Rows.Add(item);
                }
            }
            if (movieItems.Count > 0)
            {
                Rows.Add(new CategoryHeaderRow("Movies"));
                foreach (var item in movieItems.OrderBy(i => i.Name))
                {
                    Rows.Add(item);
                }
            }

            HasNoFavorites = channelItems.Count == 0 && seriesItems.Count == 0 && movieItems.Count == 0 && recentProgress.Count == 0;

            // The Rows.Add() calls above don't finish re-realizing/rendering synchronously - that
            // layout pass is queued, not immediate. Without this, IsLoading flips back to false -
            // hiding the spinner - a frame or two before the list has actually caught up, which reads
            // as "it says it's done but the list is still updating" (same fix as GuideViewModel.LoadAsync).
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            LoadError = "Failed to load favorites.";
            _logger.LogError(ex, "Failed to load favorites");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
