using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BitMagic.BennyBox.Android.Views;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox.Views;

// See LiveTvView.axaml: channel rows in as many columns as fit, and the Category chip's picker.
public partial class LiveTvView : UserControl
{
    // A row needs room for the logo, a channel name and what's on now.
    private const double MinColumnWidth = 340;
    private const double MaxFilterWidth = 720;

    public static readonly StyledProperty<double> ColumnWidthProperty =
        AvaloniaProperty.Register<LiveTvView, double>(nameof(ColumnWidth), 340);

    public double ColumnWidth
    {
        get => GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    private readonly GridRows _grid = new(item => item is ChannelListItemViewModel);

    public LiveTvView()
    {
        InitializeComponent();
        ChannelRows.ItemsSource = _grid.Rows;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is LiveTvViewModel viewModel)
            {
                _grid.Attach(viewModel.Rows);
            }
        };
    }

    // Moves the "how far through" bars on once a minute while the page is showing.
    private readonly Avalonia.Threading.DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(1) };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _clock.Tick += OnClockTick;
        _clock.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _clock.Stop();
        _clock.Tick -= OnClockTick;
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        if (DataContext is LiveTvViewModel viewModel)
        {
            foreach (var channel in viewModel.Rows.OfType<ChannelListItemViewModel>())
            {
                channel.RefreshTimes();
            }
        }
    }

    private void OnListSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var width = Math.Max(0, e.NewSize.Width - 24);
        FilterPanel.Width = Math.Min(MaxFilterWidth, width);
        var (columns, columnWidth) = PosterGrid.FitRows(width, MinColumnWidth);
        ColumnWidth = columnWidth;
        _grid.Columns = columns;
    }

    private void OnCategoryChipClick(object? sender, RoutedEventArgs e) => CategorySheet.Open();

    private void OnCategoryPicked(object? sender, string category)
    {
        if (DataContext is LiveTvViewModel viewModel)
        {
            viewModel.SelectedCategory = category;
        }
    }
}
