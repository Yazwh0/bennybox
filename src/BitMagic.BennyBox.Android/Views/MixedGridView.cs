using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox.Android.Views;

// Base for the normal-mode pages that show a mix of things (Search, Favourites, Downloads): channels as
// compact rows and everything else as posters, each sized to the width like Live TV and the poster
// pages. A page binds its list to Rows (via AttachRows), puts SizeChanged="OnListSizeChanged" on its
// ScrollViewer, and gives posters Click="OnPosterClick" ContextRequested="OnPosterContextRequested";
// Activate says what a tap on a poster does.
public abstract class MixedGridView : UserControl
{
    public const string PosterKind = "poster";
    public const string RowKind = "row";

    private const double MinRowWidth = 340;
    private const double MaxFilterWidth = 720;

    public static readonly StyledProperty<double> TileWidthProperty =
        AvaloniaProperty.Register<MixedGridView, double>(nameof(TileWidth), 110);

    public static readonly StyledProperty<double> PosterHeightProperty =
        AvaloniaProperty.Register<MixedGridView, double>(nameof(PosterHeight), 165);

    public static readonly StyledProperty<double> ColumnWidthProperty =
        AvaloniaProperty.Register<MixedGridView, double>(nameof(ColumnWidth), 340);

    public double TileWidth
    {
        get => GetValue(TileWidthProperty);
        set => SetValue(TileWidthProperty, value);
    }

    public double PosterHeight
    {
        get => GetValue(PosterHeightProperty);
        set => SetValue(PosterHeightProperty, value);
    }

    public double ColumnWidth
    {
        get => GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    protected GridRows Grid { get; } = new(KindOf);

    // Channels are rows; anything with a picture of its own is a poster; the rest (headings) full width.
    private static string? KindOf(object item) => item switch
    {
        ChannelListItemViewModel => RowKind,
        SeriesListItemViewModel or MovieListItemViewModel or ClipListItemViewModel
            or WatchProgressItemViewModel or DownloadItemViewModel => PosterKind,
        _ => null,
    };

    protected void AttachRows(ItemsControl list, ObservableCollection<object> source)
    {
        list.ItemsSource = Grid.Rows;
        Grid.Attach(source);
    }

    // The search box (if any) stops growing at a comfortable width, lined up with the content.
    protected virtual Control? FilterArea => null;

    protected void OnListSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(0, e.NewSize.Width - 24);
        if (FilterArea is { } filter)
        {
            filter.Width = Math.Min(MaxFilterWidth, width);
        }

        var posters = PosterGrid.Fit(width);
        TileWidth = posters.TileWidth;
        PosterHeight = posters.PosterHeight;
        Grid.SetColumns(PosterKind, posters.Columns);

        var (rows, rowWidth) = PosterGrid.FitRows(width, MinRowWidth);
        ColumnWidth = rowWidth;
        Grid.SetColumns(RowKind, rows);
    }

    // Press and hold opens a poster's menu; the release that ends the hold mustn't also tap it.
    private bool _suppressNextPosterClick;

    protected void OnPosterContextRequested(object? sender, ContextRequestedEventArgs e) => _suppressNextPosterClick = true;

    protected void OnPosterClick(object? sender, RoutedEventArgs e)
    {
        if (_suppressNextPosterClick)
        {
            _suppressNextPosterClick = false;
            return;
        }

        if (sender is Control { DataContext: { } item })
        {
            Activate(item);
        }
    }

    protected abstract void Activate(object item);
}
