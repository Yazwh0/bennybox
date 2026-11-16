using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BitMagic.BennyBox.Android.Views;
using BitMagic.BennyBox.UI.Services;
using CommunityToolkit.Mvvm.Messaging;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox.Views;

// See LiveTvView.axaml: channel rows in as many columns as fit, and the Category chip's picker.
public partial class LiveTvView : UserControl
{
    // A row needs room for the logo, a channel name and what's on now.
    private const double MinColumnWidth = 340;

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
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
                viewModel.Categories.CollectionChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshFavouriteCategories);
                RefreshFavouriteCategories();
            }
        };

        WeakReferenceMessenger.Default.Register<BitMagic.BennyBox.Messages.FavoritesUpdatedMessage>(
            this, (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshFavouriteCategories));
        _ = (CategoryFavourites.Current?.EnsureLoadedAsync() ?? Task.CompletedTask)
            .ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshFavouriteCategories));
    }

    // The favourite category bar: favourite TV categories this page actually has (one that's gone from
    // the provider's list just isn't shown), in the order they were starred.
    private void RefreshFavouriteCategories()
    {
        if (DataContext is not LiveTvViewModel viewModel || CategoryFavourites.Current is not { } favourites)
        {
            return;
        }

        var chips = favourites.Items
            .Where(f => f.Place == CategoryPlace.Tv)
            .Select(f => viewModel.Categories.FirstOrDefault(c => c.Equals(f.Name, StringComparison.OrdinalIgnoreCase)))
            .OfType<string>()
            .Distinct()
            .Select(name => new FavouriteCategoryChip(name, SimplePictures.ForCategory(name) ?? SimplePicture.Tv, name == viewModel.SelectedCategory))
            .ToList();

        FavouriteCategoryChips.ItemsSource = chips;
        FavouriteCategoryBar.IsVisible = chips.Count > 0;
    }

    // Picks that category, or, if it's already the one showing, goes back to every channel.
    private void OnFavouriteCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LiveTvViewModel viewModel && (sender as Control)?.DataContext is FavouriteCategoryChip chip)
        {
            viewModel.SelectedCategory = chip.IsSelected ? viewModel.Categories[0] : chip.Name;
        }
    }

    // A channel's page opens scrolled to what's on now, with catch-up above it and what's coming up
    // below, rather than starting days back at the top of the catch-up window.
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveTvViewModel.SelectedCategory))
        {
            RefreshFavouriteCategories();
        }

        if (e.PropertyName == nameof(LiveTvViewModel.OpenSchedule) && sender is LiveTvViewModel { OpenSchedule: { } schedule })
        {
            schedule.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(ChannelScheduleViewModel.IsLoading) && !schedule.IsLoading)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        var index = schedule.NowRow is { } now ? schedule.Rows.IndexOf(now) : -1;
                        if (index <= 0)
                        {
                            return;
                        }

                        // ScrollIntoView only scrolls far enough to show it (so it lands at the bottom);
                        // then move it up to just below the top, with the previous programme peeking.
                        ScheduleList.ScrollIntoView(index);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (ScheduleList.ContainerFromIndex(index) is { } row
                                && row.TranslatePoint(new Point(0, 0), ScheduleScroller) is { } position)
                            {
                                ScheduleScroller.Offset = ScheduleScroller.Offset.WithY(Math.Max(0, ScheduleScroller.Offset.Y + position.Y - 48));
                            }
                        }, Avalonia.Threading.DispatcherPriority.Background);
                    }, Avalonia.Threading.DispatcherPriority.Background);
                }
            };
        }
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
        FilterLayout.Apply(FilterPanel, FilterChips, width);
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
