using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BitMagic.BennyBox.Android.Services;
using BitMagic.BennyBox.Android.ViewModels;
using BitMagic.BennyBox.ViewModels;
using FluentAvalonia.UI.Controls;

namespace BitMagic.BennyBox.Android.Views;

// See SimpleHomeView.axaml for what simple picture mode is. Navigation is a small stack of screens
// (Home -> place -> category -> pictures) kept here rather than in a view model: it only exists while
// this view is showing and drives the normal app's own page view models underneath.
public partial class SimpleHomeView : UserControl
{
    private const double WideWidth = 600;
    private const double Gap = 12;
    private static readonly TimeSpan ExitHoldTime = TimeSpan.FromSeconds(2);

    public static readonly StyledProperty<double> TileWidthProperty =
        AvaloniaProperty.Register<SimpleHomeView, double>(nameof(TileWidth), 160);

    public static readonly StyledProperty<double> TileImageHeightProperty =
        AvaloniaProperty.Register<SimpleHomeView, double>(nameof(TileImageHeight), 136);

    public double TileWidth
    {
        get => GetValue(TileWidthProperty);
        set => SetValue(TileWidthProperty, value);
    }

    public double TileImageHeight
    {
        get => GetValue(TileImageHeightProperty);
        set => SetValue(TileImageHeightProperty, value);
    }

    private enum ScreenKind { Home, Favourites, Categories, Items }

    private sealed record Screen(ScreenKind Kind, SimpleDestination? Destination = null, string? Category = null);

    private readonly Stack<Screen> _history = new();
    private Screen _screen = new(ScreenKind.Home);
    private readonly ObservableCollection<object> _rows = [];
    private List<object> _currentTiles = [];
    private int _columns = 2;

    private AndroidShellViewModel? _shell;
    private INotifyCollectionChanged? _watchedCollection;
    private bool _rebuildQueued;

    private readonly SimpleExitTile _exitTile = new();
    private DispatcherTimer? _exitTimer;
    private DateTime _exitHoldStarted;

    // A press-and-hold reads the name aloud; the release that ends it mustn't also count as a tap.
    private bool _suppressNextClick;

    public SimpleHomeView()
    {
        InitializeComponent();
        RowsList.ItemsSource = _rows;

        DataContextChanged += (_, _) =>
        {
            _shell = DataContext as AndroidShellViewModel;
            if (_shell is not null)
            {
                _shell.Settings.PropertyChanged += OnSettingsChanged;
            }

            GoHome();
        };
    }

    // Always start from Home whenever the mode is switched on.
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.IsSimpleMode) && _shell?.Settings.IsSimpleMode == true)
        {
            GoHome();
        }
    }

    private void GoHome()
    {
        _history.Clear();
        Show(new Screen(ScreenKind.Home));
    }

    private void Navigate(Screen next)
    {
        _history.Push(_screen);
        Show(next);
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            return;
        }

        if (_history.Count > 0)
        {
            Show(_history.Pop());
        }
    }

    private void Show(Screen screen)
    {
        // Leaving a picture grid: put that page's category filter back to "all" so the normal app
        // isn't left filtered to whatever was browsed here.
        if (_screen.Kind == ScreenKind.Items && _screen.Destination is { } previous && screen.Kind != ScreenKind.Items
            && PageFor(previous) is { } page)
        {
            page.SelectedCategory = page.Categories.FirstOrDefault() ?? page.SelectedCategory;
        }

        _screen = screen;
        BackButton.IsVisible = screen.Kind != ScreenKind.Home;
        (TitleIcon.Symbol, TitleText.Text) = screen switch
        {
            { Kind: ScreenKind.Home } => (FASymbol.Home, "Home"),
            { Kind: ScreenKind.Items, Category: { } category } when category != PageFor(screen.Destination!.Value)?.Categories.FirstOrDefault()
                => (IconFor(screen.Destination!.Value), category),
            { Destination: { } destination } => (IconFor(destination), LabelFor(destination)),
            _ => (FASymbol.Home, "Home"),
        };

        if (screen.Kind == ScreenKind.Items && screen.Destination is { } destination2 && PageFor(destination2) is { } itemsPage)
        {
            itemsPage.SelectedCategory = screen.Category ?? itemsPage.Categories.FirstOrDefault() ?? itemsPage.SelectedCategory;
        }

        WatchSource(SourceFor(screen));
        Rebuild();
        RowsList.ScrollIntoView(0);
    }

    // The collection the current screen is built from, so it can rebuild when that changes (lists
    // load in the background, and filtering by category refills them).
    private INotifyCollectionChanged? SourceFor(Screen screen) => screen switch
    {
        { Kind: ScreenKind.Home or ScreenKind.Favourites } => _shell?.Favorites.Rows,
        { Kind: ScreenKind.Categories, Destination: { } d } => PageFor(d)?.Categories,
        { Kind: ScreenKind.Items, Destination: { } d } => PageFor(d)?.Rows,
        _ => null,
    };

    private void WatchSource(INotifyCollectionChanged? source)
    {
        if (_watchedCollection is not null)
        {
            _watchedCollection.CollectionChanged -= OnSourceChanged;
        }

        _watchedCollection = source;
        if (_watchedCollection is not null)
        {
            _watchedCollection.CollectionChanged += OnSourceChanged;
        }
    }

    // Lists refill one item at a time; coalesce into one rebuild per dispatcher pass.
    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildQueued)
        {
            return;
        }

        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            Rebuild();
        }, DispatcherPriority.Background);
    }

    private void Rebuild()
    {
        _currentTiles = _shell is null ? [] : BuildScreen(_shell, _screen);
        Layout();
    }

    private void Layout()
    {
        _rows.Clear();
        var pending = new List<object>();
        void Flush()
        {
            if (pending.Count > 0)
            {
                _rows.Add(new SimpleTileRow(pending));
                pending = [];
            }
        }

        foreach (var item in _currentTiles)
        {
            if (item is SimpleHeader)
            {
                Flush();
                _rows.Add(item);
                continue;
            }

            pending.Add(item);
            if (pending.Count == _columns)
            {
                Flush();
            }
        }

        Flush();
        EmptyMessage.IsVisible = _rows.Count == 0;
    }

    private List<object> BuildScreen(AndroidShellViewModel shell, Screen screen)
    {
        var tiles = new List<object>();
        switch (screen.Kind)
        {
            case ScreenKind.Home:
                var keepWatching = shell.Favorites.Rows.OfType<WatchProgressItemViewModel>().Select(TileFor).ToList();
                if (keepWatching.Count > 0)
                {
                    tiles.Add(new SimpleHeader("Keep watching", FASymbol.PlayFilled));
                    tiles.AddRange(keepWatching);
                }

                tiles.Add(new SimpleHeader("Watch", FASymbol.Video));
                foreach (var destination in Enum.GetValues<SimpleDestination>())
                {
                    if (destination == SimpleDestination.Favourites ? !shell.Favorites.HasNoFavorites : HasContent(destination))
                    {
                        tiles.Add(new SimpleTile { Title = LabelFor(destination), Icon = IconFor(destination), Payload = destination });
                    }
                }

                tiles.Add(new SimpleHeader("Settings", FASymbol.Settings));
                tiles.Add(_exitTile);
                break;

            case ScreenKind.Favourites:
                tiles.AddRange(shell.Favorites.Rows.Where(r => r is not WatchProgressItemViewModel and not CategoryHeaderRow)
                    .Select(TileForItem).OfType<SimpleTile>());
                break;

            case ScreenKind.Categories when screen.Destination is { } destination && PageFor(destination) is { } page:
                tiles.AddRange(page.Categories.Skip(1).Select(name => new SimpleTile
                {
                    Title = name,
                    Icon = IconFor(destination),
                    Payload = new SimpleCategory(destination, name),
                }));
                break;

            case ScreenKind.Items when screen.Destination is { } destination && PageFor(destination) is { } page:
                tiles.AddRange(page.Rows.Select(TileForItem).OfType<SimpleTile>());
                break;
        }

        return tiles;
    }

    private bool HasContent(SimpleDestination destination) =>
        PageFor(destination) is { } page && (page.Categories.Count > 1 || page.Rows.Count > 0);

    private static SimpleTile? TileForItem(object item) => item switch
    {
        ChannelListItemViewModel channel => new SimpleTile
        {
            Title = channel.Name, Payload = channel, ImageUrl = channel.Channel.LogoUrl,
            FallbackImageUrl = channel.FallbackLogoUrl, TmdbFallbackTitle = channel.Name,
        },
        MovieListItemViewModel movie => new SimpleTile { Title = movie.Name, Payload = movie, ImageUrl = movie.CoverUrl },
        SeriesListItemViewModel series => new SimpleTile { Title = series.Name, Payload = series, ImageUrl = series.CoverUrl },
        ClipListItemViewModel clip => new SimpleTile { Title = clip.Name, Payload = clip, ImageUrl = clip.CoverUrl },
        WatchProgressItemViewModel progress => TileFor(progress),
        _ => null,
    };

    private static SimpleTile TileFor(WatchProgressItemViewModel progress) => new()
    {
        Title = progress.Title, Payload = progress, ImageUrl = progress.CoverUrl, Progress = progress.ProgressFraction,
    };

    // The four browsable pages share the same shape (Categories with "all" first, a SelectedCategory
    // that filters Rows); this gives them one face.
    private IBrowsePage? PageFor(SimpleDestination destination) => _shell is null ? null : destination switch
    {
        SimpleDestination.Tv => new BrowsePage(_shell.LiveTv.Categories, _shell.LiveTv.Rows,
            () => _shell.LiveTv.SelectedCategory, v => _shell.LiveTv.SelectedCategory = v),
        SimpleDestination.Films => new BrowsePage(_shell.Movies.Categories, _shell.Movies.Rows,
            () => _shell.Movies.SelectedCategory, v => _shell.Movies.SelectedCategory = v),
        SimpleDestination.Shows => new BrowsePage(_shell.Series.Categories, _shell.Series.Rows,
            () => _shell.Series.SelectedCategory, v => _shell.Series.SelectedCategory = v),
        SimpleDestination.Videos => new BrowsePage(_shell.Clips.Categories, _shell.Clips.Rows,
            () => _shell.Clips.SelectedCategory, v => _shell.Clips.SelectedCategory = v),
        _ => null,
    };

    private interface IBrowsePage
    {
        ObservableCollection<string> Categories { get; }
        ObservableCollection<object> Rows { get; }
        string SelectedCategory { get; set; }
    }

    private sealed class BrowsePage(ObservableCollection<string> categories, ObservableCollection<object> rows,
        Func<string> getCategory, Action<string> setCategory) : IBrowsePage
    {
        public ObservableCollection<string> Categories => categories;
        public ObservableCollection<object> Rows => rows;
        public string SelectedCategory { get => getCategory(); set => setCategory(value); }
    }

    private static string LabelFor(SimpleDestination destination) => destination switch
    {
        SimpleDestination.Favourites => "Favourites",
        SimpleDestination.Tv => "TV",
        SimpleDestination.Films => "Films",
        SimpleDestination.Shows => "Shows",
        SimpleDestination.Videos => "Videos",
        _ => "",
    };

    private static FASymbol IconFor(SimpleDestination destination) => destination switch
    {
        SimpleDestination.Favourites => FASymbol.Star,
        SimpleDestination.Tv => FASymbol.Video,
        SimpleDestination.Films => FASymbol.Library,
        SimpleDestination.Shows => FASymbol.List,
        SimpleDestination.Videos => FASymbol.Flag,
        _ => FASymbol.Home,
    };

    private void OnListSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(0, e.NewSize.Width - 44); // the list's 16px side padding, plus room for the scroll bar
        var columns = e.NewSize.Width >= WideWidth ? 4 : 2;
        TileWidth = Math.Floor((width - Gap * (columns - 1)) / columns);
        TileImageHeight = Math.Floor((TileWidth - 24) * 0.85);
        if (columns != _columns)
        {
            _columns = columns;
            Layout();
        }
    }

    private async void OnTileClick(object? sender, RoutedEventArgs e)
    {
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            return;
        }

        if (_shell is null || sender is not Control { DataContext: SimpleTile tile })
        {
            return;
        }

        var player = _shell.Player;
        switch (tile.Payload)
        {
            case SimpleDestination.Favourites:
                Navigate(new Screen(ScreenKind.Favourites, SimpleDestination.Favourites));
                break;
            case SimpleDestination destination:
                // Skip the category screen when there's only one category (or none) to pick from.
                var categories = PageFor(destination)?.Categories.Count ?? 0;
                Navigate(categories > 2
                    ? new Screen(ScreenKind.Categories, destination)
                    : new Screen(ScreenKind.Items, destination, categories == 2 ? PageFor(destination)!.Categories[1] : null));
                break;
            case SimpleCategory category:
                Navigate(new Screen(ScreenKind.Items, category.Destination, category.Name));
                break;
            case ChannelListItemViewModel channel:
                player.PlayChannel(channel.Channel);
                break;
            case WatchProgressItemViewModel progress:
                player.ResumeFromProgress(progress.Progress);
                break;
            case MovieListItemViewModel movie:
                player.PlayMovie(movie.Movie);
                break;
            case ClipListItemViewModel clip:
                player.PlayClip(clip.Clip);
                break;
            case SeriesListItemViewModel series:
                await PlayNextEpisodeAsync(series);
                break;
        }
    }

    // A show plays straight away: its first unwatched episode (or the first episode once they're all
    // watched). The player itself resumes a part-watched episode where it stopped.
    private async Task PlayNextEpisodeAsync(SeriesListItemViewModel series)
    {
        var seriesPage = _shell!.Series;
        BusyOverlay.IsVisible = true;
        try
        {
            await seriesPage.SelectSeriesCommand.ExecuteAsync(series);
            var episodes = seriesPage.Episodes.OfType<EpisodeListItemViewModel>().ToList();
            var next = episodes.FirstOrDefault(ep => !ep.IsWatched) ?? episodes.FirstOrDefault();
            if (next is not null)
            {
                seriesPage.PlayEpisodeCommand.Execute(next);
            }
        }
        finally
        {
            seriesPage.BackToListCommand.Execute(null);
            BusyOverlay.IsVisible = false;
        }
    }

    private void OnTileHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState == HoldingState.Started && sender is Control { DataContext: SimpleTile tile })
        {
            _suppressNextClick = true;
            ReadAloud.Speak(tile.Title);
        }
    }

    private void OnBackHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState == HoldingState.Started)
        {
            _suppressNextClick = true;
            ReadAloud.Speak("Back");
        }
    }

    private void OnExitPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        e.Pointer.Capture(control);
        _exitHoldStarted = DateTime.UtcNow;
        _exitTile.IsHolding = true;
        _exitTimer?.Stop();
        _exitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _exitTimer.Tick += (_, _) =>
        {
            var progress = (DateTime.UtcNow - _exitHoldStarted) / ExitHoldTime;
            _exitTile.Progress = Math.Min(1, progress);
            if (progress >= 1)
            {
                CancelExitHold();
                if (_shell is not null)
                {
                    _shell.Settings.IsSimpleMode = false;
                    _shell.NavigateCommand.Execute("Settings");
                }
            }
        };
        _exitTimer.Start();
    }

    private void OnExitReleased(object? sender, PointerReleasedEventArgs e) => CancelExitHold();

    private void OnExitCaptureLost(object? sender, PointerCaptureLostEventArgs e) => CancelExitHold();

    private void CancelExitHold()
    {
        _exitTimer?.Stop();
        _exitTimer = null;
        _exitTile.IsHolding = false;
        _exitTile.Progress = 0;
    }
}
