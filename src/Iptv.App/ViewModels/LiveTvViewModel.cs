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

public partial class LiveTvViewModel : ViewModelBase
{
    private readonly IProfileRepository _profileRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IEpgRepository _epgRepository;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly ILogger<LiveTvViewModel> _logger;

    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(300);

    private List<CategoryGroupViewModel> _allGroups = [];
    private CancellationTokenSource? _searchCts;

    public string Title => "Live TV";

    public PlayerViewModel Player { get; }

    // Flattened header+channel rows for a single virtualized list - see CategoryHeaderRow.
    public ObservableCollection<object> Rows { get; } = [];

    [ObservableProperty]
    private Channel? _selectedChannel;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _loadError;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public LiveTvViewModel(
        PlayerViewModel player,
        IProfileRepository profileRepository,
        IChannelRepository channelRepository,
        IEpgRepository epgRepository,
        IFavoriteRepository favoriteRepository,
        ILogger<LiveTvViewModel> logger)
    {
        Player = player;
        _profileRepository = profileRepository;
        _channelRepository = channelRepository;
        _epgRepository = epgRepository;
        _favoriteRepository = favoriteRepository;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<ChannelsUpdatedMessage>(this, (_, _) => _ = LoadChannelsAsync());

        _ = LoadChannelsAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadChannelsAsync();

    [RelayCommand]
    private void SelectChannel(Channel? channel)
    {
        if (channel is null)
        {
            return;
        }

        SelectedChannel = channel;
        Player.PlayChannel(channel);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(ChannelListItemViewModel? item)
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

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = ApplyFilterDebouncedAsync(value, cts.Token);
    }

    // Every keystroke would otherwise re-scan 10k+ channels on the UI thread, which is what made
    // typing feel laggy. Debounce so we only filter once the user pauses, and do the scan itself on
    // a background thread so even a fast typist never blocks the UI.
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
    }

    private static List<CategoryGroupViewModel> Filter(List<CategoryGroupViewModel> groups, string query)
    {
        var result = new List<CategoryGroupViewModel>();
        foreach (var group in groups)
        {
            var matching = string.IsNullOrEmpty(query)
                ? group.Channels
                : group.Channels
                    .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                (c.NowTitle?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();

            if (matching.Count == 0)
            {
                continue;
            }

            result.Add(new CategoryGroupViewModel(group.Name, matching));
        }

        return result;
    }

    private static List<object> Flatten(List<CategoryGroupViewModel> groups)
    {
        var rows = new List<object>();
        foreach (var group in groups)
        {
            rows.Add(new CategoryHeaderRow(group.Name));
            rows.AddRange(group.Channels);
        }

        return rows;
    }

    private async Task LoadChannelsAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var favoriteIds = await _favoriteRepository.GetFavoriteChannelIdsAsync();
            var profiles = await _profileRepository.GetAllAsync();
            var nowUtc = DateTime.UtcNow;

            var profileData = new List<(IReadOnlyList<Category> Categories, IReadOnlyList<Channel> Channels, IReadOnlyDictionary<string, EpgNowNext> NowNext)>();
            foreach (var profile in profiles)
            {
                var categories = await _channelRepository.GetCategoriesAsync(profile.Id);
                var channels = await _channelRepository.GetChannelsAsync(profile.Id);
                var nowNext = await _epgRepository.GetNowNextAsync(profile.Id, nowUtc);
                profileData.Add((categories, channels, nowNext));
            }

            // Grouping/sorting/object-construction over tens of thousands of channels is real CPU
            // work - keep it off the UI thread so it can't hitch typing, scrolling, or playback.
            _allGroups = await Task.Run(() =>
            {
                var groups = new List<CategoryGroupViewModel>();
                foreach (var (categories, channels, nowNext) in profileData)
                {
                    var channelsByCategory = channels.ToLookup(c => c.CategoryId);
                    foreach (var category in categories)
                    {
                        var categoryChannels = channelsByCategory[category.Id]
                            .Select(c => new ChannelListItemViewModel(c, GetNowTitle(c, nowNext), favoriteIds.Contains(c.Id)))
                            .ToList();
                        if (categoryChannels.Count == 0)
                        {
                            continue;
                        }

                        groups.Add(new CategoryGroupViewModel(category.Name, categoryChannels));
                    }
                }

                return groups.OrderBy(g => g.Name).ToList();
            });

            ApplyFilter();
        }
        catch (Exception ex)
        {
            LoadError = "Failed to load channels.";
            _logger.LogError(ex, "Failed to load channels");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string? GetNowTitle(Channel channel, IReadOnlyDictionary<string, EpgNowNext> nowNext) =>
        !string.IsNullOrEmpty(channel.TvgId) && nowNext.TryGetValue(channel.TvgId, out var entry)
            ? entry.Now?.Title
            : null;
}
