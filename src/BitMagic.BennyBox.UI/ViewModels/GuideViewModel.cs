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

public partial class GuideViewModel : ViewModelBase
{
    private const double PixelsPerMinuteValue = 4;
    private const string AllCategoriesLabel = "All Categories";
    private const string LastRefreshSecondsKey = "Guide.LastRefreshSeconds";
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(300);

    private readonly IProfileRepository _profileRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly IEpgRepository _epgRepository;
    private readonly IFavoriteRepository _favoriteRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly TimeshiftUrlService _timeshiftUrlService;
    private readonly IReminderRepository _reminderRepository;
    private readonly EpgImportService _epgImportService;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<GuideViewModel> _logger;

    // Loaded once at startup, updated after every Refresh - lets the "Refreshing..." overlay say how
    // long it took last time, so a genuinely slow-but-working network EPG re-import (tens of seconds
    // for a large provider) doesn't read as a hang. See RefreshAsync/FormatRefreshElapsedLabel.
    private int? _lastRefreshSeconds;

    // The full (category-filtered, sorted) row set for the current window - Rows is re-derived from
    // this on every HideEmptyChannels/SearchText change so that doesn't require a reload from the
    // database.
    private List<GuideRowViewModel> _allRows = [];
    private CancellationTokenSource? _filterCts;

    // Every LoadAsync call opens several fresh DB connections (see SqliteConnectionFactory) and awaits
    // a handful of queries sequentially, so two overlapping calls - e.g. clicking Next Day then Refresh
    // before the first has finished - are not guaranteed to complete in the order they started. Without
    // a guard, whichever happens to finish *last* wins and overwrites Categories/Rows/the day window
    // with its own results, even if that call was the older, now-stale one - visible as the guide
    // flashing to one set of rows and then silently changing again a moment later. Each call captures
    // its own id and checks it's still the latest before touching any observable state.
    private int _loadRequestId;

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

    // What the category ComboBox actually shows - narrowed by CategoryFilterText, but always a
    // subset of Categories. Kept separate so typing a filter query never touches SelectedCategory
    // (and so re-triggers a full reload) until an item is actually picked from the (possibly
    // narrowed) list. Replaced wholesale (not mutated via Clear()+Add()) - clearing the ComboBox's
    // bound ItemsSource down to zero items, even briefly, resets its selection, and since
    // SelectedCategory is often unchanged across a reload (no PropertyChanged fires to re-push it)
    // that selection never came back. A one-shot swap to an already-complete list avoids that empty
    // gap entirely.
    [ObservableProperty]
    private ObservableCollection<string> _filteredCategories = [AllCategoriesLabel];

    public double PixelsPerMinute => PixelsPerMinuteValue;

    [ObservableProperty]
    private string _selectedCategory = AllCategoriesLabel;

    [ObservableProperty]
    private string _categoryFilterText = string.Empty;

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

    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    [ObservableProperty]
    private string? _loadError;

    // LoadError was previously write-only (set on failure, never bound in GuideView) - now that
    // RefreshAsync can set a real, actionable message when an EPG re-import fails, it needs somewhere
    // to actually show up.
    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);

    // Null except while an explicit Refresh is actually running - see RefreshAsync. Bound with
    // TargetNullValue in GuideView.axaml so the overlay falls back to the plain "Loading guide..."
    // text the rest of the time (day navigation, category changes, the initial startup load).
    [ObservableProperty]
    private string? _refreshElapsedLabel;

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
        ISeriesRepository seriesRepository,
        TimeshiftUrlService timeshiftUrlService,
        IReminderRepository reminderRepository,
        EpgImportService epgImportService,
        ISettingsStore settingsStore,
        PlayerViewModel player,
        ILogger<GuideViewModel> logger)
    {
        _profileRepository = profileRepository;
        _channelRepository = channelRepository;
        _epgRepository = epgRepository;
        _favoriteRepository = favoriteRepository;
        _seriesRepository = seriesRepository;
        _timeshiftUrlService = timeshiftUrlService;
        _reminderRepository = reminderRepository;
        _epgImportService = epgImportService;
        _settingsStore = settingsStore;
        Player = player;
        _logger = logger;

        WeakReferenceMessenger.Default.Register<ChannelsUpdatedMessage>(this, (_, _) => _ = LoadAsync());
        WeakReferenceMessenger.Default.Register<FavoritesUpdatedMessage>(this, (_, _) => _ = RefreshFavoriteFlagsAsync());

        _ = LoadAsync();
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

    // The timeline (EpgRowControl) only tunes when a click lands inside a rendered programme block,
    // so a channel with no EPG data in the current window (or a click in the dead space around
    // programmes) had no way to be tuned from Guide at all. This gives the channel name/logo column
    // an always-available tune action that doesn't depend on EPG data being present.
    [RelayCommand]
    private void SelectChannel(GuideRowViewModel? row)
    {
        if (row is not null)
        {
            Player.PlayChannel(row.Channel);
        }
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

    // Day navigation and the initial load only re-read the local cache (fast, no network) - see
    // LoadAsync. An explicit Refresh press is the one moment this page should actually go fetch new
    // EPG data: without this, "Refresh" can never show anything beyond whatever was last imported, and
    // for channels whose provider only ships a day or two of listings, that window quietly rolls past
    // "now" and every future press just re-reads the same now-expired rows - a spinner flash that
    // looks like it did nothing, because from the local cache's point of view it genuinely did nothing.
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
                    await _epgImportService.ImportAsync(profile);
                }
                catch (Exception ex)
                {
                    failedProfiles.Add(profile.Name);
                    _logger.LogWarning(ex, "Failed to refresh EPG for profile {ProfileName}", profile.Name);
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

            // LoadAsync resets LoadError to null on entry, so any failure message has to be applied
            // after it returns rather than before.
            await LoadAsync();
            if (failedProfiles.Count > 0)
            {
                LoadError = $"Couldn't refresh EPG for: {string.Join(", ", failedProfiles)}. Showing cached data.";
            }
        }
    }

    // The ComboBox transiently nulls its own selection whenever FilteredCategories is swapped to a
    // new collection (see ApplyCategoryFilter) - even though the new list still contains a matching
    // entry, that null still gets pushed through the TwoWay binding. Reacting to it here would call
    // LoadAsync, which calls ApplyCategoryFilter again, which nulls the selection again - a tight,
    // self-sustaining reload loop (hundreds of calls a second) that showed up as the "Loading guide"
    // overlay flashing continuously. Ignoring the null breaks the cycle; the ComboBox resolves back
    // to the real value on its own without any help from here.
    partial void OnSelectedCategoryChanged(string value)
    {
        if (value is null)
        {
            return;
        }

        _ = LoadAsync();
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
        // ComboBox reset and transiently null its own selection every time, which OnSelectedCategoryChanged
        // reacts to by reloading again - a self-sustaining loop that showed up as the "Loading guide"
        // overlay flashing continuously. The null-guard on OnSelectedCategoryChanged alone wasn't
        // enough: the ComboBox still resolves back to the real value a moment later, and that
        // *non-null* change was itself enough to keep the cycle going every time this ran.
        if (!matching.SequenceEqual(FilteredCategories))
        {
            FilteredCategories = new ObservableCollection<string>(matching);
        }
    }

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
        var requestId = ++_loadRequestId;
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
            var allSeriesForLogoFallback = new List<Series>();
            foreach (var profile in profiles)
            {
                var categories = await _channelRepository.GetCategoriesAsync(profile.Id);
                var channels = await _channelRepository.GetChannelsAsync(profile.Id);
                var programmes = await _epgRepository.GetProgrammesInRangeAsync(profile.Id, windowStart, windowEnd);
                profileData.Add((categories, channels, programmes));
                allSeriesForLogoFallback.AddRange(await _seriesRepository.GetSeriesAsync(profile.Id));
            }

            // See ChannelLogoFallbackSupport - not scoped per-profile.
            var logoFallbackIndex = ChannelLogoFallbackSupport.BuildSeriesCoverIndex(allSeriesForLogoFallback);

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

                        builtRows.Add(new GuideRowViewModel(channel, channelProgrammes, windowStart, windowEnd, nowUtc, PixelsPerMinute, favoriteIds.Contains(channel.Id),
                            ChannelLogoFallbackSupport.FindFallback(logoFallbackIndex, channel.Name))
                        {
                            TuneRequested = TuneChannel,
                            CatchupRequested = RequestCatchup,
                            ReminderToggleRequested = ToggleReminder
                        });
                    }
                }

                return (names, builtRows);
            });

            if (requestId != _loadRequestId)
            {
                // Superseded by a newer LoadAsync call while these DB reads were in flight - see
                // _loadRequestId. Drop this stale pass instead of overwriting whatever the latest
                // call already produced (or is still producing).
                return;
            }

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
            ApplyCategoryFilter();

            _allRows = rows.OrderBy(r => r.ChannelName).ToList();
            ApplyRowFilter();

            // ApplyRowFilter swaps Rows to a brand-new collection (see its own comment), which the two
            // synced ItemsControls (channel names + EPG timeline body) don't finish re-realizing and
            // rendering synchronously - that layout/render pass is queued, not immediate. Without this,
            // IsLoading flips back to false - hiding the spinner - a frame or two before the list has
            // actually caught up, which reads as "it says it's done but the list is still updating".
            // Awaiting a Render-priority dispatch here blocks until that queued work has drained.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            LoadError = "Failed to load guide.";
            _logger.LogError(ex, "Failed to load EPG guide");
        }
        finally
        {
            // Only the latest call gets to clear the spinner - if a newer LoadAsync started while this
            // one was still running, it's already the one driving IsLoading now.
            if (requestId == _loadRequestId)
            {
                IsLoading = false;
            }
        }
    }
}
