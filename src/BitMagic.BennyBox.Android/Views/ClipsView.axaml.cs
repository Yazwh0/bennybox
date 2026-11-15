using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BitMagic.BennyBox.Android.Views;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox.Views;

// See ClipsView.axaml (and SeriesView, which it mirrors). The poster grid's column count follows the width (PosterGrid), and the
// category / source chips open their pickers from here.
public partial class ClipsView : UserControl
{
    public static readonly StyledProperty<double> TileWidthProperty =
        AvaloniaProperty.Register<ClipsView, double>(nameof(TileWidth), 110);

    public static readonly StyledProperty<double> PosterHeightProperty =
        AvaloniaProperty.Register<ClipsView, double>(nameof(PosterHeight), 165);

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

    private const double MaxFilterWidth = 720;

    private readonly GridRows _grid = new(item => item is ClipListItemViewModel);

    public ClipsView()
    {
        InitializeComponent();
        PosterRows.ItemsSource = _grid.Rows;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ClipsViewModel viewModel)
            {
                _grid.Attach(viewModel.Rows);
            }
        };
    }

    private void OnGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // The search box and chips stop growing at a comfortable width on a tablet, lined up with
        // the grid's left edge rather than stretched across the whole screen.
        FilterPanel.Width = Math.Min(MaxFilterWidth, Math.Max(0, e.NewSize.Width - 24));

        var layout = PosterGrid.Fit(e.NewSize.Width - 24);
        TileWidth = layout.TileWidth;
        PosterHeight = layout.PosterHeight;
        _grid.Columns = layout.Columns;
    }

    // Press and hold opens the poster's menu (its ContextFlyout); the release that ends the hold
    // mustn't then also open the video underneath it.
    private bool _suppressNextPosterClick;

    private void OnPosterContextRequested(object? sender, ContextRequestedEventArgs e) => _suppressNextPosterClick = true;

    private void OnPosterClick(object? sender, RoutedEventArgs e)
    {
        if (_suppressNextPosterClick)
        {
            _suppressNextPosterClick = false;
            return;
        }

        if (sender is Control { DataContext: ClipListItemViewModel clip } && DataContext is ClipsViewModel viewModel)
        {
            viewModel.SelectClipCommand.Execute(clip);
        }
    }

    private void OnCategoryChipClick(object? sender, RoutedEventArgs e) => CategorySheet.Open();

    private void OnCategoryPicked(object? sender, string category)
    {
        if (DataContext is ClipsViewModel viewModel)
        {
            viewModel.SelectedCategory = category;
        }
    }

    private void OnSourceChipClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClipsViewModel viewModel)
        {
            SourceMenu.Show(SourceChip, viewModel.Sources, source => viewModel.SelectedSource = source);
        }
    }
}
