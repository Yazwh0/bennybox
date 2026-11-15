using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BitMagic.BennyBox.Android.Views;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox.Views;

// See SeriesView.axaml. The poster grid's column count follows the width (PosterGrid), and the
// category / source chips open their pickers from here.
public partial class SeriesView : UserControl
{
    public static readonly StyledProperty<double> TileWidthProperty =
        AvaloniaProperty.Register<SeriesView, double>(nameof(TileWidth), 110);

    public static readonly StyledProperty<double> PosterHeightProperty =
        AvaloniaProperty.Register<SeriesView, double>(nameof(PosterHeight), 165);

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

    private readonly GridRows _grid = new(item => item is SeriesListItemViewModel);

    public SeriesView()
    {
        InitializeComponent();
        PosterRows.ItemsSource = _grid.Rows;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SeriesViewModel viewModel)
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
    // mustn't then also open the show underneath it.
    private bool _suppressNextPosterClick;

    private void OnPosterContextRequested(object? sender, ContextRequestedEventArgs e) => _suppressNextPosterClick = true;

    private void OnPosterClick(object? sender, RoutedEventArgs e)
    {
        if (_suppressNextPosterClick)
        {
            _suppressNextPosterClick = false;
            return;
        }

        if (sender is Control { DataContext: SeriesListItemViewModel series } && DataContext is SeriesViewModel viewModel)
        {
            viewModel.SelectSeriesCommand.Execute(series);
        }
    }

    private void OnCategoryChipClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SeriesViewModel viewModel)
        {
            viewModel.CategoryFilterText = "";
        }

        CategoryList.SelectedItem = null;
        CategorySheet.IsVisible = true;
    }

    private void OnCategoryPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is string category && DataContext is SeriesViewModel viewModel)
        {
            viewModel.SelectedCategory = category;
            CloseSheet();
        }
    }

    private void OnSheetBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, CategorySheet))
        {
            CloseSheet();
        }
    }

    private void OnCloseSheetClick(object? sender, RoutedEventArgs e) => CloseSheet();

    private void CloseSheet()
    {
        CategorySheet.IsVisible = false;
        if (DataContext is SeriesViewModel viewModel)
        {
            viewModel.CategoryFilterText = "";
        }
    }

    // A short list (usually two or three sources), so a plain menu under the chip.
    private void OnSourceChipClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SeriesViewModel viewModel)
        {
            return;
        }

        var menu = new MenuFlyout();
        foreach (var source in viewModel.Sources)
        {
            var item = new MenuItem { Header = source };
            item.Click += (_, _) => viewModel.SelectedSource = source;
            menu.Items.Add(item);
        }

        menu.ShowAt(SourceChip);
    }
}
