using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Iptv.App.Messages;
using Iptv.Core.Models;
using Iptv.Core.Services;
using Microsoft.Extensions.Logging;

namespace Iptv.App.ViewModels;

public partial class FavoritesViewModel : ViewModelBase
{
    private readonly IProfileRepository _profileRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IEpgRepository _epgRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly ILogger<FavoritesViewModel> _logger;

    public string Title => "Favorites";

    public PlayerViewModel Player { get; }

    // Flattened "Channels" header + favorited channels, then "Series" header + favorited series -
    // either section is omitted entirely if empty. Same flattened-list rationale as LiveTv/Guide/
    // Series (see CategoryHeaderRow), even though favorites are usually few enough not to strictly
    // need virtualization - it keeps this page's structure consistent with the rest of the app.
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
        IFavoriteRepository favoriteRepository,
        ILogger<FavoritesViewModel> logger)
    {
        Player = player;
        _profileRepository = profileRepository;
        _channelRepository = channelRepository;
        _epgRepository = epgRepository;
        _seriesRepository = seriesRepository;
        _favoriteRepository = favoriteRepository;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<FavoritesUpdatedMessage>(this, (_, _) => _ = LoadAsync());
        WeakReferenceMessenger.Default.Register<ChannelsUpdatedMessage>(this, (_, _) => _ = LoadAsync());

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

    // Opening a favorited series switches to the Series page rather than showing episodes here -
    // Favorites doesn't duplicate the season/episode browsing UI, it just gets you to it quickly.
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

    private async Task LoadAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var favoriteChannelIds = await _favoriteRepository.GetFavoriteChannelIdsAsync();
            var favoriteSeriesIds = await _favoriteRepository.GetFavoriteSeriesIdsAsync();
            var profiles = await _profileRepository.GetAllAsync();

            var channelItems = new List<ChannelListItemViewModel>();
            var seriesItems = new List<SeriesListItemViewModel>();

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
            }

            Rows.Clear();
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

            HasNoFavorites = channelItems.Count == 0 && seriesItems.Count == 0;
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
