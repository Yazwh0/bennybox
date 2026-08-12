using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using BitMagic.BennyBox.Messages;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly TimeSpan ReminderPollInterval = TimeSpan.FromSeconds(30);

    // A reminder is only surfaced as a banner if it's this fresh - avoids a jarring "X is starting
    // now" popping up for something that actually started hours/days ago (e.g. the app was closed
    // when it fired). Stale reminders are still consumed/removed on the next poll, just silently.
    private static readonly TimeSpan ReminderStalenessWindow = TimeSpan.FromHours(2);

    private readonly IReminderRepository _reminderRepository;
    private readonly IChannelRepository _channelRepository;
    private readonly ISettingsStore _settingsStore;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly Queue<Reminder> _pendingReminders = new();
    private readonly DispatcherTimer _reminderTimer;
    private bool _isRestoringLastPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    [NotifyPropertyChangedFor(nameof(CurrentPageTag))]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDueReminder))]
    private Reminder? _dueReminder;

    public bool HasDueReminder => DueReminder is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateAvailable))]
    private UpdateInfo? _availableUpdate;

    public bool HasUpdateAvailable => AvailableUpdate is not null;

    // Settings is the one page with no video area - MainWindow's single shared video/sidebar layout
    // (see MainWindow.axaml) uses this to give Settings the full window width instead of squeezing it
    // into the sidebar column, and to collapse the video column to 0 while it's active.
    public bool IsSettingsActive => CurrentPage is SettingsViewModel;

    // Drives which nav button gets highlighted as the active page (see MainWindow.axaml).
    public string CurrentPageTag => CurrentPage switch
    {
        SearchViewModel => "Search",
        LiveTvViewModel => "LiveTv",
        GuideViewModel => "Guide",
        SeriesViewModel => "Series",
        MoviesViewModel => "Movies",
        ClipsViewModel => "Clips",
        DownloadsViewModel => "Downloads",
        FavoritesViewModel => "Favorites",
        SettingsViewModel => "Settings",
        _ => ""
    };

    // Checked once at window-close time (see MainWindow.OnClosing) rather than kept as a bound,
    // change-notifying property - nothing displays this live, so there's no need to wire up
    // PropertyChanged forwarding from six separate child view models just to answer "is anything
    // still loading right now?" on demand.
    public bool IsAnyRefreshInProgress =>
        LiveTv.IsLoading || Guide.IsLoading || Series.IsLoading || Movies.IsLoading || Clips.IsLoading || Favorites.IsLoading || Search.IsLoading || Settings.IsBusy;

    public SearchViewModel Search { get; }
    public LiveTvViewModel LiveTv { get; }
    public GuideViewModel Guide { get; }
    public SeriesViewModel Series { get; }
    public MoviesViewModel Movies { get; }
    public ClipsViewModel Clips { get; }
    public DownloadsViewModel Downloads { get; }
    public FavoritesViewModel Favorites { get; }
    public SettingsViewModel Settings { get; }
    public PlayerViewModel Player { get; }
    public RemoteControlViewModel RemoteControl { get; }

    public MainWindowViewModel(
        SearchViewModel search,
        LiveTvViewModel liveTv,
        GuideViewModel guide,
        SeriesViewModel series,
        MoviesViewModel movies,
        ClipsViewModel clips,
        DownloadsViewModel downloads,
        FavoritesViewModel favorites,
        SettingsViewModel settings,
        PlayerViewModel player,
        RemoteControlViewModel remoteControl,
        IReminderRepository reminderRepository,
        IChannelRepository channelRepository,
        ISettingsStore settingsStore,
        IUpdateCheckService updateCheckService)
    {
        Search = search;
        LiveTv = liveTv;
        Guide = guide;
        Series = series;
        Movies = movies;
        Clips = clips;
        Downloads = downloads;
        Favorites = favorites;
        Settings = settings;
        Player = player;
        RemoteControl = remoteControl;
        _reminderRepository = reminderRepository;
        _channelRepository = channelRepository;
        _settingsStore = settingsStore;
        _updateCheckService = updateCheckService;
        _currentPage = liveTv;

        // A series/movie favorited elsewhere is opened by switching to its page and asking it to
        // select that item, reusing the existing "open a series/movie" flow rather than duplicating it.
        WeakReferenceMessenger.Default.Register<OpenSeriesMessage>(this, (_, message) =>
        {
            CurrentPage = Series;
            Series.SelectSeriesCommand.Execute(new SeriesListItemViewModel(message.Series, message.SourceName));
        });

        WeakReferenceMessenger.Default.Register<OpenMovieMessage>(this, (_, message) =>
        {
            CurrentPage = Movies;
            Movies.SelectMovieCommand.Execute(new MovieListItemViewModel(message.Movie, message.SourceName));
        });

        WeakReferenceMessenger.Default.Register<OpenClipMessage>(this, (_, message) =>
        {
            CurrentPage = Clips;
            Clips.SelectClipCommand.Execute(new ClipListItemViewModel(message.Clip, message.SourceName));
        });

        _reminderTimer = new DispatcherTimer { Interval = ReminderPollInterval };
        _reminderTimer.Tick += (_, _) => _ = CheckRemindersAsync();
        _reminderTimer.Start();
        _ = CheckRemindersAsync();
        _ = RestoreLastPageAsync();
        _ = CheckForUpdateAsync();
    }

    [RelayCommand]
    private void Navigate(string? destination)
    {
        CurrentPage = destination switch
        {
            "Search" => Search,
            "LiveTv" => LiveTv,
            "Guide" => Guide,
            "Series" => Series,
            "Movies" => Movies,
            "Clips" => Clips,
            "Downloads" => Downloads,
            "Favorites" => Favorites,
            "Settings" => Settings,
            _ => CurrentPage
        };
    }

    // Remembers whichever page was last active so the app reopens there next launch, rather than
    // always defaulting back to Live TV - skipped while RestoreLastPageAsync itself is applying the
    // saved value, so restoring doesn't just immediately re-save the same tag it just read.
    partial void OnCurrentPageChanged(ViewModelBase value)
    {
        if (_isRestoringLastPage)
        {
            return;
        }

        _ = _settingsStore.SetAsync("LastVisitedPage", CurrentPageTag);
    }

    private async Task RestoreLastPageAsync()
    {
        var saved = await _settingsStore.GetAsync("LastVisitedPage");
        if (string.IsNullOrEmpty(saved))
        {
            return;
        }

        _isRestoringLastPage = true;
        NavigateCommand.Execute(saved);
        _isRestoringLastPage = false;
    }

    private async Task CheckRemindersAsync()
    {
        var due = await _reminderRepository.GetDueAsync(DateTime.UtcNow);
        foreach (var reminder in due)
        {
            if (DateTime.UtcNow - reminder.StartUtc <= ReminderStalenessWindow)
            {
                _pendingReminders.Enqueue(reminder);
            }
            await _reminderRepository.RemoveAsync(reminder.ProfileId, reminder.ChannelTvgId, reminder.StartUtc);
        }

        if (DueReminder is null && _pendingReminders.Count > 0)
        {
            DueReminder = _pendingReminders.Dequeue();
        }
    }

    [RelayCommand]
    private void DismissReminder()
    {
        DueReminder = _pendingReminders.Count > 0 ? _pendingReminders.Dequeue() : null;
    }

    [RelayCommand]
    private async Task WatchReminderAsync()
    {
        if (DueReminder is { } reminder)
        {
            var channels = await _channelRepository.GetChannelsAsync(reminder.ProfileId);
            var channel = channels.FirstOrDefault(c => c.TvgId == reminder.ChannelTvgId);
            if (channel is not null)
            {
                if (IsSettingsActive)
                {
                    CurrentPage = LiveTv;
                }
                Player.PlayChannel(channel);
            }
        }

        DismissReminder();
    }

    // Once per launch, not on a repeating timer like reminders - there's nothing to poll for (a new
    // release doesn't ship more often than once every few hours at the very fastest), and hitting
    // GitHub's API repeatedly for the life of the process would be pointless. Opt-out via Settings ->
    // Updates ("CheckForUpdatesAutomatically" - absent/anything but the literal "false" means on, so
    // existing installs default to checking without needing a migration).
    private async Task CheckForUpdateAsync()
    {
        if (await _settingsStore.GetAsync("CheckForUpdatesAutomatically") == "false")
        {
            return;
        }

        AvailableUpdate = await _updateCheckService.CheckForUpdateAsync();
    }

    [RelayCommand]
    private void DismissUpdate() => AvailableUpdate = null;

    [RelayCommand]
    private void OpenUpdate()
    {
        if (AvailableUpdate is { } update)
        {
            try
            {
                Process.Start(new ProcessStartInfo(update.ReleaseUrl) { UseShellExecute = true });
            }
            catch
            {
                // Opening the default browser is a courtesy, not something worth surfacing an error
                // for - the release is still reachable manually from github.com/Yazwh0/bennybox.
            }
        }

        DismissUpdate();
    }
}
