using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.Messages;
using BitMagic.BennyBox.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BitMagic.BennyBox.Android.ViewModels;

// Android's own navigation shell - deliberately not MainWindowViewModel (which composes
// RemoteControlViewModel, a desktop-only feature, and owns desktop-window-shaped state like
// reminders/update-checks/window geometry that don't apply here). Mirrors its CurrentPage/
// NavigateCommand pattern since that part translates directly to a bottom-tab phone shell - see the
// Android port plan, Phase 3. Deliberately minimal for now: no reminders/update-check/RemoteControl -
// add those once there's a reason to (real usage, not just parity for its own sake).
public partial class AndroidShellViewModel : ViewModelBase
{
    private readonly ISettingsStore _settingsStore;

    // Set only while RestoreLastPageAsync itself is applying the saved tab - see its own comment.
    private bool _isRestoringLastPage;

    // NotifyPropertyChangedFor here (not a manual OnPropertyChanged(nameof(CurrentPageTag)) only
    // inside Navigate) matters because CurrentPage is also set directly by the OpenSeriesMessage/
    // OpenMovieMessage/OpenClipMessage handlers below, bypassing Navigate entirely - without this
    // attribute the tab bar's active-tab highlight would silently stay on whatever tab was active
    // before Search/Favorites/Downloads sent one of those messages.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPageTag))]
    private ViewModelBase _currentPage;

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

    public AndroidShellViewModel(
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
        ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
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
        _currentPage = liveTv;

        // Mirrors MainWindowViewModel's same three registrations - without these, tapping a series/
        // movie/clip result on Search, Favorites, or Downloads (all of which only send one of these
        // messages, they don't navigate directly) would silently do nothing on Android: nothing was
        // listening, so the message vanished and the tab never switched or selected the item.
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

        _ = RestoreLastPageAsync();
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

    // Mirrors MainWindowViewModel's OnCurrentPageChanged/RestoreLastPageAsync - remembers whichever
    // tab was last active so the app reopens there next launch instead of always defaulting back to
    // Live TV. Same ISettingsStore key ("LastVisitedPage") as desktop, though each platform has its
    // own local settings store so there's no cross-device syncing implied by sharing the name - it's
    // just consistency for its own sake.
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
}
