using CommunityToolkit.Mvvm.ComponentModel;

namespace Iptv.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsActive))]
    private ViewModelBase _currentPage;

    // Settings is the one page with no video area - MainWindow's single shared video/sidebar layout
    // (see MainWindow.axaml) uses this to give Settings the full window width instead of squeezing it
    // into the sidebar column, and to collapse the video column to 0 while it's active.
    public bool IsSettingsActive => CurrentPage is SettingsViewModel;

    public LiveTvViewModel LiveTv { get; }
    public GuideViewModel Guide { get; }
    public SeriesViewModel Series { get; }
    public FavoritesViewModel Favorites { get; }
    public SettingsViewModel Settings { get; }
    public PlayerViewModel Player { get; }

    public MainWindowViewModel(
        LiveTvViewModel liveTv,
        GuideViewModel guide,
        SeriesViewModel series,
        FavoritesViewModel favorites,
        SettingsViewModel settings,
        PlayerViewModel player)
    {
        LiveTv = liveTv;
        Guide = guide;
        Series = series;
        Favorites = favorites;
        Settings = settings;
        Player = player;
        _currentPage = liveTv;
    }

    public void Navigate(string destination)
    {
        CurrentPage = destination switch
        {
            "LiveTv" => LiveTv,
            "Guide" => Guide,
            "Series" => Series,
            "Favorites" => Favorites,
            "Settings" => Settings,
            _ => CurrentPage
        };
    }
}
