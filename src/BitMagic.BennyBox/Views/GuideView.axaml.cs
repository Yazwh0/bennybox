using Avalonia;
using Avalonia.Controls;

namespace BitMagic.BennyBox.Views;

public partial class GuideView : UserControl
{
    private bool _syncingScroll;

    public GuideView()
    {
        InitializeComponent();
        BodyScroll.ScrollChanged += OnBodyScrollChanged;
        ChannelNamesScroll.ScrollChanged += OnChannelNamesScrollChanged;
        TimeHeaderScroll.ScrollChanged += OnTimeHeaderScrollChanged;
    }

    // Avalonia has no built-in frozen-row/frozen-column grid, so the channel-name column and
    // time header are separate ScrollViewers whose offsets are kept in lockstep with the body here.
    private void OnBodyScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_syncingScroll)
        {
            return;
        }

        _syncingScroll = true;
        ChannelNamesScroll.Offset = new Vector(ChannelNamesScroll.Offset.X, BodyScroll.Offset.Y);
        TimeHeaderScroll.Offset = new Vector(BodyScroll.Offset.X, TimeHeaderScroll.Offset.Y);
        _syncingScroll = false;
    }

    private void OnChannelNamesScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_syncingScroll)
        {
            return;
        }

        _syncingScroll = true;
        BodyScroll.Offset = new Vector(BodyScroll.Offset.X, ChannelNamesScroll.Offset.Y);
        _syncingScroll = false;
    }

    private void OnTimeHeaderScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_syncingScroll)
        {
            return;
        }

        _syncingScroll = true;
        BodyScroll.Offset = new Vector(TimeHeaderScroll.Offset.X, BodyScroll.Offset.Y);
        _syncingScroll = false;
    }
}
