using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FluentAvalonia.UI.Controls;
using Iptv.App.ViewModels;
using Iptv.Core.Services;

namespace Iptv.App.Views;

public partial class MainWindow : Window
{
    private const double MinPaneWidth = 200;
    private const double MaxPaneWidth = 480;

    private readonly ISettingsStore _settingsStore;
    private readonly PlayerViewModel _player;
    private readonly ColumnDefinition _navColumn;
    private WindowState _windowStateBeforeFullscreen = WindowState.Normal;
    private double _navPaneWidthBeforeFullscreen = 280;

    public MainWindow(ISettingsStore settingsStore, PlayerViewModel player)
    {
        _settingsStore = settingsStore;
        _player = player;
        InitializeComponent();
        _navColumn = RootGrid.ColumnDefinitions[0];
        // ColumnDefinition doesn't support x:Name for code-behind field generation, and GridSplitter
        // changes its Width directly (not through a binding) - so OpenPaneLength is kept in sync by
        // observing the column's Width property instead of trying to bind to it from XAML.
        _navColumn.PropertyChanged += (_, e) =>
        {
            if (e.Property == ColumnDefinition.WidthProperty)
            {
                NavView.OpenPaneLength = _navColumn.Width.Value;
            }
        };
        Opened += OnOpened;
        Closing += OnClosing;
        KeyDown += OnKeyDown;
        _player.PropertyChanged += OnPlayerPropertyChanged;
    }

    private void OnPaneResizeDragCompleted(object? sender, VectorEventArgs e)
    {
        _ = _settingsStore.SetAsync("NavPaneWidth", _navColumn.Width.Value.ToString(CultureInfo.InvariantCulture));
    }

    // GridSplitter resizes ContentGrid's column directly via SetCurrentValue, which cooperates with
    // (rather than breaks) the existing MultiBinding - so it doesn't fight it, but also doesn't flow
    // back to the view model on its own. Push the resolved width back only once the drag ends (see
    // PlayerViewModel.SidebarWidth).
    private void OnContentSidebarSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        _player.SidebarWidth = ContentGrid.ColumnDefinitions[0].ActualWidth;
    }

    private void OnItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer is FANavigationViewItem { Tag: string tag } && DataContext is MainWindowViewModel vm)
        {
            vm.Navigate(tag);
        }
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PlayerViewModel.IsFullscreen))
        {
            return;
        }

        if (_player.IsFullscreen)
        {
            _windowStateBeforeFullscreen = WindowState is WindowState.FullScreen or WindowState.Minimized
                ? WindowState.Normal
                : WindowState;
            WindowState = WindowState.FullScreen;

            // MinWidth would otherwise stop the column ever reaching 0 - drop it too, same issue as
            // the per-page video sidebar hit.
            _navPaneWidthBeforeFullscreen = _navColumn.Width.Value;
            _navColumn.MinWidth = 0;
            _navColumn.Width = new GridLength(0);
        }
        else
        {
            WindowState = _windowStateBeforeFullscreen;
            _navColumn.MinWidth = MinPaneWidth;
            _navColumn.Width = new GridLength(_navPaneWidthBeforeFullscreen);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _player.IsFullscreen)
        {
            _player.ExitFullscreen();
            e.Handled = true;
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        // LibVLCSharp.Avalonia's VideoView only ever attaches MediaPlayer.Hwnd to the native
        // rendering surface it creates when its MediaPlayer property changes or when it first
        // initializes - neither of which is guaranteed to happen after that native surface actually
        // exists (VideoView.Attach() silently no-ops if the surface isn't ready yet, and it's never
        // retried once MediaPlayer stops changing). By the time the window has Opened, every native
        // child control is guaranteed to be realized, so forcing a fresh assignment here (bypassing
        // the no-op-on-same-reference guard by nulling first) reliably attaches it. Without this,
        // video silently renders nowhere - or libVLC falls back to popping its own native window.
        MainVideoView.MediaPlayer = null;
        MainVideoView.MediaPlayer = _player.MediaPlayer;

        var savedPaneWidth = await _settingsStore.GetAsync("NavPaneWidth");
        if (savedPaneWidth is not null &&
            double.TryParse(savedPaneWidth, NumberStyles.Float, CultureInfo.InvariantCulture, out var paneWidth))
        {
            _navColumn.Width = new GridLength(Math.Clamp(paneWidth, MinPaneWidth, MaxPaneWidth));
        }

        var saved = await _settingsStore.GetAsync("WindowState");
        if (saved is null)
        {
            return;
        }

        var parts = saved.Split('|');
        if (parts.Length == 3 &&
            double.TryParse(parts[0], out var width) && width > 0 &&
            double.TryParse(parts[1], out var height) && height > 0 &&
            Enum.TryParse<WindowState>(parts[2], out var state))
        {
            Width = width;
            Height = height;
            WindowState = state == WindowState.Minimized ? WindowState.Normal : state;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Never persist FullScreen itself - if the user closes while fullscreen, remember what was active before it.
        var effectiveState = _player.IsFullscreen ? _windowStateBeforeFullscreen : WindowState;
        var state = effectiveState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        var width = state == WindowState.Maximized ? Width : Bounds.Width;
        var height = state == WindowState.Maximized ? Height : Bounds.Height;
        _ = _settingsStore.SetAsync("WindowState", $"{width}|{height}|{state}");
    }
}
