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
using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.Messages;
using BitMagic.BennyBox.UI.Services;
using BitMagic.BennyBox.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

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

    private enum ScreenKind { Home, Categories, Items, Show, Channel }

    private sealed record Screen(ScreenKind Kind, SimpleDestination? Destination = null, string? Category = null, SimplePicture? Picture = null,
        SeriesListItemViewModel? Series = null, ChannelListItemViewModel? Channel = null);

    private readonly Stack<Screen> _history = new();
    private Screen _screen = new(ScreenKind.Home);
    private readonly ObservableCollection<object> _rows = [];
    private List<object> _currentTiles = [];
    private int _columns = 2;

    private AndroidShellViewModel? _shell;
    private List<INotifyCollectionChanged> _watchedCollections = [];
    private bool _holdingForList;
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

        // Favourite categories live in settings; once they're read, the home screen can show them.
        _ = LoadCategoryFavouritesAsync();

        // A stream that just failed (or worked again) updates its tile where it is, rather than
        // rebuilding the screen and losing the scroll position; it moves to the end next time.
        WeakReferenceMessenger.Default.Register<StreamHealthChangedMessage>(this, (_, message) =>
        {
            foreach (var tile in _currentTiles.OfType<SimpleTile>().Where(t => t.StreamUrl == message.StreamUrl))
            {
                tile.DidNotWork = StreamHealth.Current?.DidNotWork(message.StreamUrl) ?? false;
                tile.CannotPlayHere = StreamHealth.Current?.CannotPlayHere(message.StreamUrl) ?? false;
            }
        });

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

    private async Task LoadCategoryFavouritesAsync()
    {
        await CategoryFavourites.EnsureLoadedAsync();
        Rebuild();
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

        // Leaving a channel's page: close it on Live TV too.
        if (_screen.Kind == ScreenKind.Channel && screen.Kind != ScreenKind.Channel)
        {
            _shell?.LiveTv.CloseScheduleCommand.Execute(null);
        }

        // Leaving a show: close it on the Series page too.
        if (_screen.Kind == ScreenKind.Show && screen.Kind != ScreenKind.Show)
        {
            StopWatchingEpisodes();
            _shell?.Series.BackToListCommand.Execute(null);
        }

        _screen = screen;
        BackButton.IsVisible = screen.Kind != ScreenKind.Home;
        (TitleIcon.Picture, TitleText.Text) = screen switch
        {
            { Kind: ScreenKind.Home } => (SimplePicture.Home, "Home"),
            { Kind: ScreenKind.Show, Series: { } show } => (SimplePicture.Shows, show.Name),
            { Kind: ScreenKind.Channel, Channel: { } shown } => (SimplePicture.Tv, shown.Name),
            { Kind: ScreenKind.Items, Category: { } category } when category != PageFor(screen.Destination!.Value)?.Categories.FirstOrDefault()
                => (screen.Picture ?? PictureFor(screen.Destination!.Value), category),
            { Destination: { } destination } => (PictureFor(destination), LabelFor(destination)),
            _ => (SimplePicture.Home, "Home"),
        };

        if (screen.Kind == ScreenKind.Items && screen.Destination is { } destination2 && PageFor(destination2) is { } itemsPage)
        {
            itemsPage.SelectedCategory = screen.Category ?? itemsPage.Categories.FirstOrDefault() ?? itemsPage.SelectedCategory;
        }

        WatchSources(SourcesFor(screen));
        Rebuild();
        RowsList.ScrollIntoView(0);

        if (screen is { Kind: ScreenKind.Show, Series: { } series })
        {
            _ = OpenShowAsync(series);
        }

        if (screen is { Kind: ScreenKind.Channel, Channel: { } channelToOpen })
        {
            _ = OpenChannelAsync(channelToOpen);
        }
    }

    // The collection the current screen is built from, so it can rebuild when that changes (lists
    // load in the background, and filtering by category refills them).
    //
    // Home also watches each place's category list: its TV / Films / Shows / Videos tiles only show
    // once that place has something in it, and those lists load in the background. Watching just the
    // favourites meant a place that finished loading after them (TV, with thousands of channels, nearly
    // always) never got its tile until something else changed.
    private IEnumerable<INotifyCollectionChanged?> SourcesFor(Screen screen) => screen switch
    {
        { Kind: ScreenKind.Home } => [_shell?.Favorites.Rows, .. Enum.GetValues<SimpleDestination>().Select(d => PageFor(d)?.Categories)],
        { Kind: ScreenKind.Categories, Destination: { } d } => [PageFor(d)?.Categories],
        { Kind: ScreenKind.Items, Destination: { } d } => [PageFor(d)?.Rows],
        { Kind: ScreenKind.Show } => [_shell?.Series.Episodes],
        _ => [],
    };

    private void WatchSources(IEnumerable<INotifyCollectionChanged?> sources)
    {
        foreach (var watched in _watchedCollections)
        {
            watched.CollectionChanged -= OnSourceChanged;
        }

        _watchedCollections = sources.OfType<INotifyCollectionChanged>().ToList();
        foreach (var watched in _watchedCollections)
        {
            watched.CollectionChanged += OnSourceChanged;
        }
    }

    // Lists refill one item at a time; coalesce into one rebuild per dispatcher pass. Normal priority so it
    // runs before the next frame is drawn, never showing the old tiles for a frame.
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
        }, DispatcherPriority.Normal);
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
            if (item is SimpleHeader or SimplePlayNextTile or SimpleWatchNowTile)
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
        EmptyMessage.IsVisible = _rows.Count == 0 && !_holdingForList;
    }

    // The page is still filtering to the category being shown (its list is cleared until then), or its
    // list still has a heading for some other category.
    private static bool IsStale(IBrowsePage page, string? category) =>
        page.IsFiltering
        || category is not null && category != page.Categories.FirstOrDefault()
        && page.Rows.OfType<CategoryHeaderRow>().Any(header => header.Name != category);

    private List<object> BuildScreen(AndroidShellViewModel shell, Screen screen)
    {
        _holdingForList = false;
        var tiles = new List<object>();
        switch (screen.Kind)
        {
            case ScreenKind.Home:
                var keepWatching = shell.Favorites.Rows.OfType<WatchProgressItemViewModel>().Select(TileFor).ToList();
                if (keepWatching.Count > 0)
                {
                    tiles.Add(new SimpleHeader("Keep watching", SimplePicture.KeepWatching));
                    tiles.AddRange(keepWatching);
                }

                // Favourites, a section per kind, each headed by that kind's own picture.
                var favourites = shell.Favorites.Rows;
                AddSection(tiles, "My channels", SimplePicture.Tv, favourites.OfType<ChannelListItemViewModel>());
                AddSection(tiles, "My shows", SimplePicture.Shows, favourites.OfType<SeriesListItemViewModel>());
                AddSection(tiles, "My films", SimplePicture.Films, favourites.OfType<MovieListItemViewModel>());
                AddSection(tiles, "My videos", SimplePicture.Videos, favourites.OfType<ClipListItemViewModel>());

                // Favourite categories ("UK | Sport"): one tap to everything in them. Skipped while that
                // place is still loading, or if the category has gone from the source.
                var favouriteCategories = CategoryFavourites.Items
                    .Select(f => (Destination: DestinationFor(f.Place), f.Name))
                    .Where(f => PageFor(f.Destination)?.Categories.Contains(f.Name) == true)
                    .Select(f => CategoryTile(f.Destination, PageFor(f.Destination)!, f.Name, [], withBadge: true))
                    .ToList();
                if (favouriteCategories.Count > 0)
                {
                    tiles.Add(new SimpleHeader("My categories", SimplePicture.Favourite));
                    tiles.AddRange(favouriteCategories);
                }

                var places = Enum.GetValues<SimpleDestination>().Where(HasContent).ToList();
                if (places.Count > 0)
                {
                    tiles.Add(new SimpleHeader("Everything", SimplePicture.Home));
                    tiles.AddRange(places.Select(destination =>
                        new SimpleTile { Title = LabelFor(destination), Icon = PictureFor(destination), Payload = destination }));
                }

                tiles.Add(new SimpleHeader("Settings", SimplePicture.Settings));
                tiles.Add(_exitTile);
                break;

            case ScreenKind.Categories when screen.Destination is { } destination && PageFor(destination) is { } page:
                var usedPictures = new HashSet<SimplePicture>();
                tiles.AddRange(page.Categories.Skip(1).Select(name => CategoryTile(destination, page, name, usedPictures)));
                break;

            case ScreenKind.Show:
                BuildShow(shell.Series.Episodes, tiles);
                break;

            case ScreenKind.Channel when screen.Channel is { } channel:
                BuildChannel(channel, shell.LiveTv.OpenSchedule, tiles);
                break;

            case ScreenKind.Items when screen.Destination is { } destination && PageFor(destination) is { } page:
                // Picking a category filters the page's list in the background, so for a moment it still
                // holds the last one. Show nothing until it's caught up rather than the wrong tiles first.
                if (IsStale(page, screen.Category))
                {
                    _holdingForList = true;
                    break;
                }

                // Anything that didn't play last time goes to the end (OrderBy keeps the rest in order).
                tiles.AddRange(page.Rows.Select(TileForItem).OfType<SimpleTile>().OrderBy(tile => tile.IsUnplayable));
                break;
        }

        return tiles;
    }

    // A category's picture: a fixed one when its name has a known word (Sport, News, Kids...), so it
    // looks the same every time; otherwise the first four pictures inside it, with the place's badge.
    // Only falls back to the place's own picture when the category has nothing with a picture yet.
    // withBadge: the place's picture in the corner too, for the home screen's favourite categories, where TV
    // and film categories sit side by side.
    private SimpleTile CategoryTile(SimpleDestination destination, IBrowsePage page, string name, HashSet<SimplePicture> usedPictures,
        bool withBadge = false)
    {
        var payload = new SimpleCategory(destination, name);
        var isFavourite = CategoryFavourites.IsFavourite(PlaceFor(destination), name);
        // Each fixed picture is used once per screen (the first category to match gets it), so no two
        // tiles on a screen look the same - "Adventure" after "Action" gets a collage instead.
        if (SimplePictures.ForCategory(name) is { } picture && usedPictures.Add(picture))
        {
            return new SimpleTile { Title = name, Icon = picture, Payload = payload, IsFavourite = isFavourite,
                Badge = withBadge ? PictureFor(destination) : null };
        }

        var images = page.ItemsInCategory(name)
            .Select(TileForItem)
            .Where(tile => !string.IsNullOrEmpty(tile?.ImageUrl))
            .DistinctBy(tile => tile!.ImageUrl)
            .Take(4)
            .Select(tile => new SimpleImage(tile!.ImageUrl, tile.FallbackImageUrl, tile.TmdbFallbackTitle))
            .ToList();

        return images.Count == 0
            ? new SimpleTile { Title = name, Icon = PictureFor(destination), Payload = payload, IsFavourite = isFavourite }
            : new SimpleTile { Title = name, Collage = images, Badge = PictureFor(destination), Payload = payload, IsFavourite = isFavourite };
    }

    private static CategoryFavourites CategoryFavourites => App.Services!.GetRequiredService<CategoryFavourites>();

    private static CategoryPlace PlaceFor(SimpleDestination destination) => destination switch
    {
        SimpleDestination.Tv => CategoryPlace.Tv,
        SimpleDestination.Films => CategoryPlace.Films,
        SimpleDestination.Shows => CategoryPlace.Shows,
        _ => CategoryPlace.Videos,
    };

    private static SimpleDestination DestinationFor(CategoryPlace place) => place switch
    {
        CategoryPlace.Tv => SimpleDestination.Tv,
        CategoryPlace.Films => SimpleDestination.Films,
        CategoryPlace.Shows => SimpleDestination.Shows,
        _ => SimpleDestination.Videos,
    };

    private static void AddSection(List<object> tiles, string title, SimplePicture picture, IEnumerable<object> items)
    {
        var section = items.Select(TileForItem).OfType<SimpleTile>().ToList();
        if (section.Count > 0)
        {
            tiles.Add(new SimpleHeader(title, picture));
            tiles.AddRange(section);
        }
    }

    private bool HasContent(SimpleDestination destination) =>
        PageFor(destination) is { } page && (page.Categories.Count > 1 || page.Rows.Count > 0);

    private static SimpleTile? TileForItem(object item) => item switch
    {
        ChannelListItemViewModel channel => new SimpleTile
        {
            Title = channel.Name, Payload = channel, ImageUrl = channel.Channel.LogoUrl,
            FallbackImageUrl = channel.FallbackLogoUrl, TmdbFallbackTitle = channel.Name,
            Badge = SimplePicture.Tv, IsFavourite = channel.IsFavorite, DidNotWork = channel.DidNotWork, CannotPlayHere = channel.CannotPlayHere,
        },
        MovieListItemViewModel movie => new SimpleTile
        {
            Title = movie.Name, Payload = movie, ImageUrl = movie.CoverUrl, Badge = SimplePicture.Films, IsFavourite = movie.IsFavorite, DidNotWork = movie.DidNotWork, CannotPlayHere = movie.CannotPlayHere,
        },
        SeriesListItemViewModel series => new SimpleTile
        {
            Title = series.Name, Payload = series, ImageUrl = series.CoverUrl, Badge = SimplePicture.Shows, IsFavourite = series.IsFavorite,
        },
        ClipListItemViewModel clip => new SimpleTile
        {
            Title = clip.Name, Payload = clip, ImageUrl = clip.CoverUrl, Badge = SimplePicture.Videos, IsFavourite = clip.IsFavorite, DidNotWork = clip.DidNotWork, CannotPlayHere = clip.CannotPlayHere,
        },
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
        SimpleDestination.Tv => new BrowsePage(_shell.LiveTv.Categories, _shell.LiveTv.Rows, _shell.LiveTv.ItemsInCategory, () => _shell.LiveTv.IsFiltering,
            () => _shell.LiveTv.SelectedCategory, v => _shell.LiveTv.SelectedCategory = v),
        SimpleDestination.Films => new BrowsePage(_shell.Movies.Categories, _shell.Movies.Rows, _shell.Movies.ItemsInCategory, () => _shell.Movies.IsFiltering,
            () => _shell.Movies.SelectedCategory, v => _shell.Movies.SelectedCategory = v),
        SimpleDestination.Shows => new BrowsePage(_shell.Series.Categories, _shell.Series.Rows, _shell.Series.ItemsInCategory, () => _shell.Series.IsFiltering,
            () => _shell.Series.SelectedCategory, v => _shell.Series.SelectedCategory = v),
        SimpleDestination.Videos => new BrowsePage(_shell.Clips.Categories, _shell.Clips.Rows, _shell.Clips.ItemsInCategory, () => _shell.Clips.IsFiltering,
            () => _shell.Clips.SelectedCategory, v => _shell.Clips.SelectedCategory = v),
        _ => null,
    };

    private interface IBrowsePage
    {
        ObservableCollection<string> Categories { get; }
        ObservableCollection<object> Rows { get; }
        bool IsFiltering { get; }
        string SelectedCategory { get; set; }
        IReadOnlyList<object> ItemsInCategory(string category);
    }

    private sealed class BrowsePage(ObservableCollection<string> categories, ObservableCollection<object> rows,
        Func<string, IReadOnlyList<object>> itemsInCategory, Func<bool> isFiltering, Func<string> getCategory, Action<string> setCategory) : IBrowsePage
    {
        public ObservableCollection<string> Categories => categories;
        public ObservableCollection<object> Rows => rows;
        public bool IsFiltering => isFiltering();
        public string SelectedCategory { get => getCategory(); set => setCategory(value); }
        public IReadOnlyList<object> ItemsInCategory(string category) => itemsInCategory(category);
    }

    private static string LabelFor(SimpleDestination destination) => destination switch
    {
        SimpleDestination.Tv => "TV",
        SimpleDestination.Films => "Films",
        SimpleDestination.Shows => "Shows",
        SimpleDestination.Videos => "Videos",
        _ => "",
    };

    private static SimplePicture PictureFor(SimpleDestination destination) => destination switch
    {
        SimpleDestination.Tv => SimplePicture.Tv,
        SimpleDestination.Films => SimplePicture.Films,
        SimpleDestination.Shows => SimplePicture.Shows,
        SimpleDestination.Videos => SimplePicture.Videos,
        _ => SimplePicture.Home,
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

        // A channel opens its page (Watch now, then what can be watched again), like a show does; the
        // hold panel's Play still plays it straight away.
        if (tile.Payload is ChannelListItemViewModel channel)
        {
            Navigate(new Screen(ScreenKind.Channel, Channel: channel));
            return;
        }

        await ActivateAsync(tile);
    }

    // A channel's page, built from the same schedule Live TV's channel page uses (this drives it).
    private async Task OpenChannelAsync(ChannelListItemViewModel channel)
    {
        BusyOverlay.IsVisible = true;
        try
        {
            await _shell!.LiveTv.OpenScheduleCommand.ExecuteAsync(channel);
        }
        finally
        {
            BusyOverlay.IsVisible = false;
        }

        if (_screen.Channel == channel)
        {
            Rebuild();
        }
    }

    // Watch now first, a whole row wide; then everything that can be watched again, newest first and
    // under a heading per day ("Today", "Yesterday"...); then what's still to come, faded, where a tap
    // reads it out. Times are the big text on each tile, so they can be told apart without reading.
    private static void BuildChannel(ChannelListItemViewModel channel, ChannelScheduleViewModel? schedule, List<object> tiles)
    {
        var loaded = schedule is { IsLoading: false } && schedule.Channel == channel;
        var now = loaded ? schedule!.NowRow : null;
        tiles.Add(new SimpleWatchNowTile(channel, now?.Title ?? channel.Now?.Title, now?.NowProgress ?? channel.NowProgress, now?.IconUrl ?? channel.Now?.IconUrl));
        if (!loaded)
        {
            return;
        }

        var days = new List<(string Day, List<ScheduleProgrammeViewModel> Programmes)>();
        foreach (var row in schedule!.Rows)
        {
            switch (row)
            {
                case ScheduleDayHeader header:
                    days.Add((header.Name, []));
                    break;
                case ScheduleProgrammeViewModel programme when days.Count > 0:
                    days[^1].Programmes.Add(programme);
                    break;
            }
        }

        foreach (var (day, programmes) in Enumerable.Reverse(days))
        {
            var again = programmes.Where(p => p.CanCatchUp).Reverse().ToList();
            if (again.Count == 0)
            {
                continue;
            }

            tiles.Add(new SimpleHeader(day == "Today" ? "Watch again" : $"Watch again: {day}", SimplePicture.WatchAgain));
            tiles.AddRange(again.Select(p => ProgrammeTile(p, day, isLater: false)));
        }

        foreach (var (day, programmes) in days)
        {
            var later = programmes.Where(p => !p.IsPast && !p.IsNow).ToList();
            if (later.Count == 0)
            {
                continue;
            }

            tiles.Add(new SimpleHeader(day == "Today" ? "Later today" : day, SimplePicture.Later));
            tiles.AddRange(later.Select(p => ProgrammeTile(p, day, isLater: true)));
        }
    }

    private static SimpleTile ProgrammeTile(ScheduleProgrammeViewModel programme, string day, bool isLater) => new()
    {
        Title = programme.Title,
        Payload = programme,
        BigText = programme.TimeLabel,
        ImageUrl = programme.IconUrl,
        TmdbFallbackTitle = ProgrammeTitles.Clean(programme.Title),
        ShowsTimeOverPicture = true,
        // Without the guide's little superscript "New" marker and the like, which read-aloud stumbles over.
        SpokenText = $"{day} at {programme.TimeLabel}. {ProgrammeTitles.Clean(programme.Title)}",
        Badge = isLater ? SimplePicture.Later : SimplePicture.WatchAgain,
        IsLater = isLater,
    };

    private void OnWatchNowClick(object? sender, RoutedEventArgs e)
    {
        if (!TakeSuppressedClick() && sender is Control { DataContext: SimpleWatchNowTile watchNow })
        {
            _shell?.Player.PlayChannel(watchNow.Channel.Channel);
        }
    }

    private void OnWatchNowHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState == HoldingState.Started && sender is Control { DataContext: SimpleWatchNowTile watchNow })
        {
            _suppressNextClick = true;
            ReadAloud.Speak(watchNow.NowTitle is { } title ? $"Watch {watchNow.Channel.Name} now. {title}" : $"Watch {watchNow.Channel.Name} now");
        }
    }

    private async Task ActivateAsync(SimpleTile tile)
    {
        if (_shell is null)
        {
            return;
        }

        var player = _shell.Player;
        switch (tile.Payload)
        {
            case SimpleDestination destination:
                // Skip the category screen when there's only one category (or none) to pick from.
                var categories = PageFor(destination)?.Categories.Count ?? 0;
                Navigate(categories > 2
                    ? new Screen(ScreenKind.Categories, destination)
                    : new Screen(ScreenKind.Items, destination, categories == 2 ? PageFor(destination)!.Categories[1] : null));
                break;
            case SimpleCategory category:
                // The title shows the same picture as the tile that was tapped (a collage shows the place's).
                Navigate(new Screen(ScreenKind.Items, category.Destination, category.Name, tile.Icon));
                break;
            case ChannelListItemViewModel channel:
                player.PlayChannel(channel.Channel);
                break;
            case ScheduleProgrammeViewModel programme when _shell.LiveTv.OpenSchedule is { } schedule:
                if (programme.CanPlay)
                {
                    await schedule.PlayProgrammeCommand.ExecuteAsync(programme);
                }
                else
                {
                    ReadAloud.Speak(tile.Speech);
                }

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
                Navigate(new Screen(ScreenKind.Show, Series: series));
                break;
            case EpisodeListItemViewModel episode:
                _shell.Series.PlayEpisodeCommand.Execute(episode);
                break;
        }
    }

    // A show's screen: a big "Play next" button, then every episode by season with a tick on the ones
    // already watched, so it's easy to see how far through it is (or pick a different episode). The
    // episodes are loaded by the normal Series page, which this drives.
    private async Task OpenShowAsync(SeriesListItemViewModel series)
    {
        BusyOverlay.IsVisible = true;
        try
        {
            await _shell!.Series.SelectSeriesCommand.ExecuteAsync(series);
        }
        finally
        {
            BusyOverlay.IsVisible = false;
        }

        if (_screen.Series != series)
        {
            return;
        }

        WatchEpisodes();
        Rebuild();
    }

    private static void BuildShow(IEnumerable<object> rows, List<object> tiles)
    {
        var episodes = rows.OfType<EpisodeListItemViewModel>().ToList();
        if (episodes.Count == 0)
        {
            return;
        }

        // The first episode not yet watched; once they all are, back to the start. The player resumes
        // a part-watched episode where it stopped.
        var next = episodes.FirstOrDefault(e => !e.IsWatched);
        var target = next ?? episodes[0];
        tiles.Add(new SimplePlayNextTile(target,
            next is null ? "Play from the start" : "Play next",
            $"Season {target.Episode.Season}, episode {target.Episode.EpisodeNumber}",
            target.ShortTitle));

        foreach (var row in rows)
        {
            switch (row)
            {
                case SeasonHeaderRow season:
                    tiles.Add(new SimpleHeader(season.Name, SimplePicture.Shows));
                    break;
                case EpisodeListItemViewModel episode:
                    tiles.Add(new SimpleTile
                    {
                        Title = episode.ShortTitle,
                        Payload = episode,
                        BigText = episode.Episode.EpisodeNumber.ToString(),
                        SpokenText = $"Episode {episode.Episode.EpisodeNumber}. {episode.ShortTitle}",
                        IsWatched = episode.IsWatched,
                        // The one Play next will play gets a play badge.
                        Badge = episode == target ? SimplePicture.Play : null,
                        DidNotWork = episode.DidNotWork,
                        CannotPlayHere = episode.CannotPlayHere,
                    });
                    break;
            }
        }
    }

    // Watching an episode ticks it (and moves Play next on); rebuild when that happens.
    private readonly List<EpisodeListItemViewModel> _watchedEpisodes = [];

    private void WatchEpisodes()
    {
        StopWatchingEpisodes();
        _watchedEpisodes.AddRange(_shell!.Series.Episodes.OfType<EpisodeListItemViewModel>());
        foreach (var episode in _watchedEpisodes)
        {
            episode.PropertyChanged += OnEpisodeChanged;
        }
    }

    private void StopWatchingEpisodes()
    {
        foreach (var episode in _watchedEpisodes)
        {
            episode.PropertyChanged -= OnEpisodeChanged;
        }

        _watchedEpisodes.Clear();
    }

    private void OnEpisodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EpisodeListItemViewModel.IsWatched))
        {
            OnSourceChanged(sender, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    private void OnPlayNextClick(object? sender, RoutedEventArgs e)
    {
        if (TakeSuppressedClick())
        {
            return;
        }

        if (sender is Control { DataContext: SimplePlayNextTile playNext })
        {
            _shell?.Series.PlayEpisodeCommand.Execute(playNext.Episode);
        }
    }

    private void OnPlayNextHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState == HoldingState.Started && sender is Control { DataContext: SimplePlayNextTile playNext })
        {
            _suppressNextClick = true;
            ReadAloud.Speak($"{playNext.Label}. {playNext.Detail}. {playNext.EpisodeTitle}");
        }
    }

    // Holding reads the name aloud, and for a channel/film/show/video opens the item panel.
    private void OnTileHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState == HoldingState.Started && sender is Control { DataContext: SimpleTile tile })
        {
            _suppressNextClick = true;
            ReadAloud.Speak(tile.Speech);
            if (tile.CanFavourite)
            {
                ItemCard.DataContext = tile;
                ItemPanel.IsVisible = true;
            }
        }
    }

    private void CloseItemPanel()
    {
        ItemPanel.IsVisible = false;
        ItemCard.DataContext = null;
    }

    private void OnItemPanelBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only a press on the dimmed area around the card, not on the card itself.
        if (ReferenceEquals(e.Source, ItemPanel))
        {
            CloseItemPanel();
        }
    }

    private bool TakeSuppressedClick()
    {
        if (!_suppressNextClick)
        {
            return false;
        }

        _suppressNextClick = false;
        return true;
    }

    private async void OnPanelPlayClick(object? sender, RoutedEventArgs e)
    {
        if (TakeSuppressedClick() || ItemCard.DataContext is not SimpleTile tile)
        {
            return;
        }

        CloseItemPanel();
        await ActivateAsync(tile);
    }

    private void OnPanelBackClick(object? sender, RoutedEventArgs e)
    {
        if (!TakeSuppressedClick())
        {
            CloseItemPanel();
        }
    }

    private void OnPanelButtonHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState == HoldingState.Started && sender is Control { Tag: string label })
        {
            _suppressNextClick = true;
            ReadAloud.Speak(label);
        }
    }

    // Adds or removes the favourite, saying which it did. The home screen rebuilds from the reloaded
    // favourites; the browse pages pick up the change through FavoritesUpdatedMessage.
    private async void OnPanelFavouriteClick(object? sender, RoutedEventArgs e)
    {
        if (TakeSuppressedClick() || ItemCard.DataContext is not SimpleTile tile)
        {
            return;
        }

        var repository = App.Services!.GetRequiredService<IFavoriteRepository>();
        var add = !tile.IsFavourite;
        switch (tile.Payload)
        {
            case SimpleCategory category:
                CategoryFavourites.Toggle(PlaceFor(category.Destination), category.Name);
                break;
            case ChannelListItemViewModel channel:
                await (add ? repository.AddAsync(channel.Channel.ProfileId, channel.Channel.Id) : repository.RemoveAsync(channel.Channel.Id));
                channel.IsFavorite = add;
                break;
            case SeriesListItemViewModel series:
                if (add)
                {
                    await repository.AddSeriesAsync(series.Series.ProfileId, series.Series.Id);
                }
                else
                {
                    foreach (var part in series.Parts)
                    {
                        await repository.RemoveSeriesAsync(part.Id);
                    }
                }

                series.IsFavorite = add;
                break;
            case MovieListItemViewModel movie:
                await (add ? repository.AddMovieAsync(movie.Movie.ProfileId, movie.Movie.Id) : repository.RemoveMovieAsync(movie.Movie.Id));
                movie.IsFavorite = add;
                break;
            case ClipListItemViewModel clip:
                await (add ? repository.AddClipAsync(clip.Clip.ProfileId, clip.Clip.Id) : repository.RemoveClipAsync(clip.Clip.Id));
                clip.IsFavorite = add;
                break;
            default:
                return;
        }

        tile.IsFavourite = add;
        ReadAloud.Speak(add ? "Added to favourites" : "Removed from favourites");
        CloseItemPanel();
        WeakReferenceMessenger.Default.Send(new FavoritesUpdatedMessage());
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
