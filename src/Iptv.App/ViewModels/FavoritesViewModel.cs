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
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly ILogger<FavoritesViewModel> _logger;

    public string Title => "Favorites";

    public PlayerViewModel Player { get; }

    public ObservableCollection<ChannelListItemViewModel> Channels { get; } = [];

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
        IFavoriteRepository favoriteRepository,
        ILogger<FavoritesViewModel> logger)
    {
        Player = player;
        _profileRepository = profileRepository;
        _channelRepository = channelRepository;
        _epgRepository = epgRepository;
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

    private async Task LoadAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var favoriteIds = await _favoriteRepository.GetFavoriteChannelIdsAsync();
            var profiles = await _profileRepository.GetAllAsync();
            var items = new List<ChannelListItemViewModel>();

            foreach (var profile in profiles)
            {
                var channels = await _channelRepository.GetChannelsAsync(profile.Id);
                var favoriteChannels = channels.Where(c => favoriteIds.Contains(c.Id)).ToList();
                if (favoriteChannels.Count == 0)
                {
                    continue;
                }

                var nowNext = await _epgRepository.GetNowNextAsync(profile.Id, DateTime.UtcNow);
                foreach (var channel in favoriteChannels)
                {
                    var nowTitle = !string.IsNullOrEmpty(channel.TvgId) && nowNext.TryGetValue(channel.TvgId, out var entry)
                        ? entry.Now?.Title
                        : null;
                    items.Add(new ChannelListItemViewModel(channel, nowTitle, isFavorite: true));
                }
            }

            Channels.Clear();
            foreach (var item in items.OrderBy(i => i.Name))
            {
                Channels.Add(item);
            }
            HasNoFavorites = Channels.Count == 0;
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
