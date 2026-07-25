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

public partial class GuideViewModel : ViewModelBase
{
    private const double PixelsPerMinuteValue = 4;
    private const string AllCategoriesLabel = "All Categories";
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(300);

    private readonly IProfileRepository _profileRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IEpgRepository _epgRepository;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly TimeshiftUrlService _timeshiftUrlService;
    private readonly IReminderRepository _reminderRepository;
    private readonly ILogger<GuideViewModel> _logger;

    // The full (category-filtered, sorted) row set for the current window - Rows is re-derived from
    // this on every HideEmptyChannels/SearchText change so that doesn't require a reload from the
    // database.
    private List<GuideRowViewModel> _allRows = [];
    private CancellationTokenSource? _filterCts;

    public string Title => "Guide";

    public PlayerViewModel Player { get; }

    // Replaced wholesale (not mutated via Clear()+Add() per row) on every filter/reload - with
    // thousands of rows, Clear() alone is a Reset that tears down every realized row container, and
    // each subsequent Add() is its own change notification on top of that. Swapping the ItemsSource
    // reference in one go instead gives Avalonia a single, one-shot refresh rather than a storm of
    // incremental ones - this is what was making the Guide stutter while scrolling/filtering.
    [ObservableProperty]
    private ObservableCollection<GuideRowViewModel> _rows = [];

    public ObservableCollection<string> Categories { get; } = [AllCategoriesLabel];

    public double PixelsPerMinute => PixelsPerMinuteValue;

    [ObservableProperty]
    private string _selectedCategory = AllCategoriesLabel;

    [ObservableProperty]
    private bool _hideEmptyChannels = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentDayLabel))]
    private int _dayOffset;

    [ObservableProperty]
    private DateTime _windowStartUtc;

    [ObservableProperty]
    private DateTime _windowEndUtc;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _loadError;

    public string CurrentDayLabel => DayOffset switch
    {
        0 => "Today",
        1 => "Tomorrow",
        _ => DateTime.Now.Date.AddDays(DayOffset).ToString("dddd, MMM d")
    };

    public GuideViewModel(
        IProfileRepository profileRepository,
        IChannelRepository channelRepository,
        IEpgRepository epgRepository,
        IFavoriteRepository favoriteRepository,
        TimeshiftUrlService timeshiftUrlService,
        IReminderRepository reminderRepository,
        PlayerViewModel player,
        ILogger<GuideViewModel> logger)
    {
        _profileRepository = profileRepository;
        _channelRepository = channelRepository;
        _epgRepository = epgRepository;
        _favoriteRepository = favoriteRepository;
        _timeshiftUrlService = timeshiftUrlService;
        _reminderRepository = reminderRepository;
        Player = player;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<ChannelsUpdatedMessage>(this, (_, _) => _ = LoadAsync());
        WeakReferenceMessenger.Default.Register<FavoritesUpdatedMessage>(this, (_, _) => _ = RefreshFavoriteFlagsAsync());

        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(GuideRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.IsFavorite)
        {
            await _favoriteRepository.RemoveAsync(row.Channel.Id);
            row.IsFavorite = false;
        }
        else
        {
            await _favoriteRepository.AddAsync(row.Channel.ProfileId, row.Channel.Id);
            row.IsFavorite = true;
        }

        WeakReferenceMessenger.Default.Send(new FavoritesUpdatedMessage());
    }

    // Same rationale as SeriesViewModel: a favorite toggled on Live TV or Favorites should be
    // reflected here too, without re-running the whole (EPG-fetching) LoadAsync just to flip stars.
    private async Task RefreshFavoriteFlagsAsync()
    {
        var favoriteIds = await _favoriteRepository.GetFavoriteChannelIdsAsync();
        foreach (var row in _allRows)
        {
            row.IsFavorite = favoriteIds.Contains(row.Channel.Id);
        }
    }

    [RelayCommand]
    private async Task GoToTodayAsync()
    {
        DayOffset = 0;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task GoToNextDayAsync()
    {
        DayOffset++;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task GoToPreviousDayAsync()
    {
        if (DayOffset > 0)
        {
            DayOffset--;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    partial void OnSelectedCategoryChanged(string value) => _ = LoadAsync();

    partial void OnHideEmptyChannelsChanged(bool value) => DebounceApplyRowFilter();

    partial void OnSearchTextChanged(string value) => DebounceApplyRowFilter();

    private void DebounceApplyRowFilter()
    {
        _filterCts?.Cancel();
        var cts = new CancellationTokenSource();
        _filterCts = cts;
        _ = ApplyRowFilterDebouncedAsync(cts.Token);
    }

    // Same reasoning as LiveTvViewModel's search: scanning every row (and every row's programmes) on
    // each keystroke would make typing feel laggy, so debounce and do the scan off the UI thread.
    private async Task ApplyRowFilterDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDebounceDelay, cancellationToken);

            var snapshot = _allRows;
            var query = SearchText.Trim();
            var hideEmpty = HideEmptyChannels;
            var filtered = await Task.Run(() => FilterRows(snapshot, query, hideEmpty), cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            Dispatcher.UIThread.Post(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                Rows = new ObservableCollection<GuideRowViewModel>(filtered);
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke/toggle - just drop this pass.
        }
    }

    private void ApplyRowFilter()
    {
        Rows = new ObservableCollection<GuideRowViewModel>(FilterRows(_allRows, SearchText.Trim(), HideEmptyChannels));
    }

    private static List<GuideRowViewModel> FilterRows(List<GuideRowViewModel> rows, string query, bool hideEmpty)
    {
        IEnumerable<GuideRowViewModel> result = rows;
        if (hideEmpty)
        {
            result = result.Where(r => r.HasProgrammes);
        }

        if (!string.IsNullOrEmpty(query))
        {
            result = result.Where(r =>
                r.ChannelName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Programmes.Any(p => p.Title.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        return result.ToList();
    }

    private void TuneChannel(Channel channel) => Player.PlayChannel(channel);

    private void RequestCatchup(Channel channel, EpgProgramme programme) => _ = RequestCatchupAsync(channel, programme);

    // The click that got us here already flipped programme.HasReminder and redrew the row (see
    // EpgRowControl) - this just persists whatever state it's now in.
    private void ToggleReminder(Channel channel, ProgrammeViewModel programme) => _ = ToggleReminderAsync(channel, programme);

    private async Task ToggleReminderAsync(Channel channel, ProgrammeViewModel programme)
    {
        try
        {
            var tvgId = channel.TvgId ?? channel.SourceChannelId;
            if (programme.HasReminder)
            {
                await _reminderRepository.AddAsync(new Reminder
                {
                    ProfileId = channel.ProfileId,
                    ChannelTvgId = tvgId,
                    StartUtc = programme.StartUtc,
                    EndUtc = programme.EndUtc,
                    ChannelName = channel.Name,
                    ProgrammeTitle = programme.Title
                });
            }
            else
            {
                await _reminderRepository.RemoveAsync(channel.ProfileId, tvgId, programme.StartUtc);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist reminder toggle for {Title}", programme.Title);
        }
    }

    private async Task RequestCatchupAsync(Channel channel, EpgProgramme programme)
    {
        try
        {
            var duration = programme.EndUtc - programme.StartUtc;
            var url = await _timeshiftUrlService.BuildTimeshiftUrlAsync(channel, programme.StartUtc, duration);
            if (url is not null)
            {
                Player.PlayTimeshift(channel, url, programme.Title);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build catch-up URL for {ChannelName} / {ProgrammeTitle}", channel.Name, programme.Title);
        }
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var nowUtc = DateTime.UtcNow;
            var dayStartUtc = DateTime.UtcNow.Date.AddDays(DayOffset);
            var windowStart = DayOffset == 0 ? nowUtc.AddMinutes(-30) : dayStartUtc;
            var windowEnd = windowStart.AddHours(6);
            WindowStartUtc = windowStart;
            WindowEndUtc = windowEnd;

            var favoriteIds = await _favoriteRepository.GetFavoriteChannelIdsAsync();
            var reminderKeys = (await _reminderRepository.GetAllAsync())
                .Select(r => (r.ProfileId, r.ChannelTvgId, r.StartUtc))
                .ToHashSet();
            var profiles = await _profileRepository.GetAllAsync();

            var profileData = new List<(IReadOnlyList<Category> Categories, IReadOnlyList<Channel> Channels, IReadOnlyList<EpgProgramme> Programmes)>();
            foreach (var profile in profiles)
            {
                var categories = await _channelRepository.GetCategoriesAsync(profile.Id);
                var channels = await _channelRepository.GetChannelsAsync(profile.Id);
                var programmes = await _epgRepository.GetProgrammesInRangeAsync(profile.Id, windowStart, windowEnd);
                profileData.Add((categories, channels, programmes));
            }

            var selectedCategoryAtLoad = SelectedCategory;

            // Grouping programmes/channels and building a GuideRowViewModel per channel is real CPU
            // work over potentially thousands of rows - keep it off the UI thread.
            var (categoryNames, rows) = await Task.Run(() =>
            {
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                var builtRows = new List<GuideRowViewModel>();

                foreach (var (categories, channels, programmes) in profileData)
                {
                    var programmesByChannel = programmes
                        .GroupBy(p => p.ChannelTvgId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            g => g.Key,
                            g => (IReadOnlyList<EpgProgramme>)g.OrderBy(p => p.StartUtc).ToList(),
                            StringComparer.OrdinalIgnoreCase);

                    var categoryNameById = categories.ToDictionary(c => c.Id, c => c.Name);
                    foreach (var name in categoryNameById.Values)
                    {
                        names.Add(name);
                    }

                    var filteredChannels = selectedCategoryAtLoad == AllCategoriesLabel
                        ? channels
                        : channels.Where(c => c.CategoryId is not null &&
                                               categoryNameById.TryGetValue(c.CategoryId, out var name) &&
                                               name == selectedCategoryAtLoad);

                    foreach (var channel in filteredChannels)
                    {
                        var rawProgrammes = !string.IsNullOrEmpty(channel.TvgId) &&
                                             programmesByChannel.TryGetValue(channel.TvgId, out var found)
                            ? found
                            : [];

                        var channelProgrammes = rawProgrammes
                            .Select(p => new ProgrammeViewModel(p, reminderKeys.Contains((channel.ProfileId, channel.TvgId!, p.StartUtc))))
                            .ToList();

                        builtRows.Add(new GuideRowViewModel(channel, channelProgrammes, windowStart, windowEnd, nowUtc, PixelsPerMinute, favoriteIds.Contains(channel.Id))
                        {
                            TuneRequested = TuneChannel,
                            CatchupRequested = RequestCatchup,
                            ReminderToggleRequested = ToggleReminder
                        });
                    }
                }

                return (names, builtRows);
            });

            var selectedCategory = SelectedCategory;
            Categories.Clear();
            Categories.Add(AllCategoriesLabel);
            foreach (var name in categoryNames)
            {
                Categories.Add(name);
            }

            if (!Categories.Contains(selectedCategory))
            {
                selectedCategory = AllCategoriesLabel;
            }
            SelectedCategory = selectedCategory;

            _allRows = rows.OrderBy(r => r.ChannelName).ToList();
            ApplyRowFilter();
        }
        catch (Exception ex)
        {
            LoadError = "Failed to load guide.";
            _logger.LogError(ex, "Failed to load EPG guide");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
