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

public partial class LiveTvViewModel : ViewModelBase
{
    private const string AllCategoriesLabel = "All Categories";

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
    private Channel? _selectedChannel;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _loadError;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = AllCategoriesLabel;

    [ObservableProperty]
    private string _categoryFilterText = string.Empty;

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
        WeakReferenceMessenger.Default.Register<FavoritesUpdatedMessage>(this, (_, _) => _ = RefreshFavoriteFlagsAsync());

        _ = LoadChannelsAsync();
    }

    // A favorite toggled from Guide or Favorites should show up here too without a full reload
    // (which would re-fetch every channel and now/next EPG entry just to flip some booleans).
    private async Task RefreshFavoriteFlagsAsync()
    {
        var favoriteIds = await _favoriteRepository.GetFavoriteChannelIdsAsync();
        foreach (var group in _allGroups)
        {
            foreach (var channel in group.Channels)
            {
                channel.IsFavorite = favoriteIds.Contains(channel.Channel.Id);
            }
        }
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

    // Every keystroke would otherwise re-scan 10k+ channels on the UI thread, which is what made
    // typing feel laggy. Debounce so we only filter once the user pauses, and do the scan itself on
    // a background thread so even a fast typist never blocks the UI.
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
    }

    private static List<CategoryGroupViewModel> Filter(List<CategoryGroupViewModel> groups, string query, string category)
    {
        var result = new List<CategoryGroupViewModel>();
        foreach (var group in groups)
        {
            if (category != AllCategoriesLabel && group.Name != category)
            {
                continue;
            }

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
