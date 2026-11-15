using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox.Views;

public partial class LiveTvView : UserControl
{
    public LiveTvView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is LiveTvViewModel viewModel)
            {
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    // A channel's page opens scrolled to what's on now, with catch-up above it and what's coming up
    // below, rather than starting days back at the top of the catch-up window.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LiveTvViewModel.OpenSchedule) || sender is not LiveTvViewModel { OpenSchedule: { } schedule })
        {
            return;
        }

        schedule.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(ChannelScheduleViewModel.IsLoading) || schedule.IsLoading)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                var index = schedule.NowRow is { } now ? schedule.Rows.IndexOf(now) : -1;
                if (index <= 0)
                {
                    return;
                }

                // ScrollIntoView only scrolls far enough to show it; then move it up near the top.
                ScheduleList.ScrollIntoView(index);
                Dispatcher.UIThread.Post(() =>
                {
                    if (ScheduleList.ContainerFromIndex(index) is { } row
                        && row.TranslatePoint(new Point(0, 0), ScheduleScroller) is { } position)
                    {
                        ScheduleScroller.Offset = ScheduleScroller.Offset.WithY(Math.Max(0, ScheduleScroller.Offset.Y + position.Y - 40));
                    }
                }, DispatcherPriority.Background);
            }, DispatcherPriority.Background);
        };
    }
}
