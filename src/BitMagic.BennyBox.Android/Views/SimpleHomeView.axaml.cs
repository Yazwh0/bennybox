using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BitMagic.BennyBox.Android.Services;
using BitMagic.BennyBox.Android.ViewModels;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox.Android.Views;

public partial class SimpleHomeView : UserControl
{
    // Phones get two big tiles per row; anything 600dp or wider (tablets) gets four.
    private const double WideWidth = 600;
    private const double Gap = 12;
    private static readonly TimeSpan ExitHoldTime = TimeSpan.FromSeconds(3);

    public static readonly StyledProperty<double> TileWidthProperty =
        AvaloniaProperty.Register<SimpleHomeView, double>(nameof(TileWidth), 160);

    public static readonly StyledProperty<double> TileImageHeightProperty =
        AvaloniaProperty.Register<SimpleHomeView, double>(nameof(TileImageHeight), 136);

    public static readonly StyledProperty<double> ContentWidthProperty =
        AvaloniaProperty.Register<SimpleHomeView, double>(nameof(ContentWidth), 320);

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

    // Section headings span the full row so each section starts on its own line.
    public double ContentWidth
    {
        get => GetValue(ContentWidthProperty);
        set => SetValue(ContentWidthProperty, value);
    }

    // FavoritesViewModel.Rows minus Shows (series): tapping one would open the text-heavy series page,
    // so they stay out until a tap can resume the next episode directly.
    private readonly ObservableCollection<object> _tiles = [];
    private FavoritesViewModel? _favorites;
    private DispatcherTimer? _exitTimer;

    // A press-and-hold reads the name aloud; the release that ends it mustn't then also count as a
    // tap and start playing.
    private bool _suppressNextClick;

    public SimpleHomeView()
    {
        InitializeComponent();
        TilesList.ItemsSource = _tiles;

        DataContextChanged += (_, _) =>
        {
            if (_favorites is not null)
            {
                _favorites.Rows.CollectionChanged -= OnRowsChanged;
            }

            _favorites = DataContext as FavoritesViewModel;
            if (_favorites is not null)
            {
                _favorites.Rows.CollectionChanged += OnRowsChanged;
            }

            RebuildTiles();
        };
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildTiles();

    private void RebuildTiles()
    {
        _tiles.Clear();
        if (_favorites is null)
        {
            return;
        }

        foreach (var row in _favorites.Rows)
        {
            if (row is SeriesListItemViewModel or CategoryHeaderRow { Name: "Series" })
            {
                continue;
            }

            _tiles.Add(row);
        }
    }

    private void OnContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(0, e.NewSize.Width - 32); // ItemsControl's 16px side margins
        var columns = e.NewSize.Width >= WideWidth ? 4 : 2;
        ContentWidth = width;
        TileWidth = Math.Floor((width - Gap * (columns - 1)) / columns);
        TileImageHeight = Math.Floor((TileWidth - 24) * 0.85); // inside the tile's 12px padding
    }

    private void OnTileClick(object? sender, RoutedEventArgs e)
    {
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            return;
        }

        if (_favorites is null || sender is not Control { DataContext: { } item })
        {
            return;
        }

        var player = _favorites.Player;
        switch (item)
        {
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
        }
    }

    private void OnTileHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started || sender is not Control { DataContext: { } item })
        {
            return;
        }

        _suppressNextClick = true;
        ReadAloud.Speak(item switch
        {
            ChannelListItemViewModel channel => channel.Name,
            WatchProgressItemViewModel progress => progress.Title,
            MovieListItemViewModel movie => movie.Name,
            ClipListItemViewModel clip => clip.Name,
            _ => null,
        });
    }

    private void OnHeaderHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState == HoldingState.Started && sender is Control { DataContext: CategoryHeaderRow header })
        {
            ReadAloud.Speak(SimpleModeConverters.SectionLabel.Convert(header.Name, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture) as string);
        }
    }

    private void OnExitPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Pointer.Capture(ExitCorner);
        _exitTimer?.Stop();
        _exitTimer = new DispatcherTimer { Interval = ExitHoldTime };
        _exitTimer.Tick += (_, _) =>
        {
            CancelExitHold();
            if (this.FindAncestorOfType<AndroidShellView>()?.DataContext is AndroidShellViewModel shell)
            {
                shell.Settings.IsSimpleMode = false;
                shell.NavigateCommand.Execute("Settings");
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
    }
}
