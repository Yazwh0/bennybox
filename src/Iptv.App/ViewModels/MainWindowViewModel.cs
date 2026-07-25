using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Iptv.App.Messages;

namespace Iptv.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    [NotifyPropertyChangedFor(nameof(CurrentPageTag))]
    private ViewModelBase _currentPage;

    // Settings is the one page with no video area - MainWindow's single shared video/sidebar layout
    // (see MainWindow.axaml) uses this to give Settings the full window width instead of squeezing it
    // into the sidebar column, and to collapse the video column to 0 while it's active.
    public bool IsSettingsActive => CurrentPage is SettingsViewModel;

    // Drives which nav button gets highlighted as the active page (see MainWindow.axaml).
    public string CurrentPageTag => CurrentPage switch
    {
        LiveTvViewModel => "LiveTv",
        GuideViewModel => "Guide",
        SeriesViewModel => "Series",
        MoviesViewModel => "Movies",
        FavoritesViewModel => "Favorites",
        SettingsViewModel => "Settings",
        _ => ""
    };

    public LiveTvViewModel LiveTv { get; }
    public GuideViewModel Guide { get; }
    public SeriesViewModel Series { get; }
    public MoviesViewModel Movies { get; }
    public FavoritesViewModel Favorites { get; }
    public SettingsViewModel Settings { get; }
    public PlayerViewModel Player { get; }

    public MainWindowViewModel(
        LiveTvViewModel liveTv,
        GuideViewModel guide,
        SeriesViewModel series,
        MoviesViewModel movies,
        FavoritesViewModel favorites,
        SettingsViewModel settings,
        PlayerViewModel player)
    {
        LiveTv = liveTv;
        Guide = guide;
        Series = series;
        Movies = movies;
        Favorites = favorites;
        Settings = settings;
        Player = player;
        _currentPage = liveTv;

        // A series/movie favorited elsewhere is opened by switching to its page and asking it to
        // select that item, reusing the existing "open a series/movie" flow rather than duplicating it.
        WeakReferenceMessenger.Default.Register<OpenSeriesMessage>(this, (_, message) =>
        {
            CurrentPage = Series;
            Series.SelectSeriesCommand.Execute(new SeriesListItemViewModel(message.Series));
        });

        WeakReferenceMessenger.Default.Register<OpenMovieMessage>(this, (_, message) =>
        {
            CurrentPage = Movies;
            Movies.SelectMovieCommand.Execute(new MovieListItemViewModel(message.Movie));
        });
    }

    [RelayCommand]
    private void Navigate(string? destination)
    {
        CurrentPage = destination switch
        {
            "LiveTv" => LiveTv,
            "Guide" => Guide,
            "Series" => Series,
            "Movies" => Movies,
            "Favorites" => Favorites,
            "Settings" => Settings,
            _ => CurrentPage
        };
    }
}
