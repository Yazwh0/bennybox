using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BitMagic.BennyBox.ViewModels;
using BitMagic.BennyBox.Core.Services;
using FluentAvalonia.UI.Controls;

namespace BitMagic.BennyBox.Views;

public partial class MainWindow : Window
{
    private readonly ISettingsStore _settingsStore;
    private readonly PlayerViewModel _player;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private WindowState _windowStateBeforeFullscreen = WindowState.Normal;
    private bool _forceClosing;

    public MainWindow(ISettingsStore settingsStore, PlayerViewModel player, MainWindowViewModel mainWindowViewModel)
    {
        _settingsStore = settingsStore;
        _player = player;
        _mainWindowViewModel = mainWindowViewModel;
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        KeyDown += OnKeyDown;
        _player.PropertyChanged += OnPlayerPropertyChanged;

        // Slider's internal Thumb captures pointer input for its own drag logic and marks
        // PointerPressed/PointerReleased as handled before they'd otherwise bubble up to a plain
        // XAML PointerPressed="..." handler on the Slider - so grabbing the actual dot never fired
        // BeginUserSeek, and the periodic position updates kept overwriting the drag mid-gesture
        // (looked like the thumb "snapping back"). Subscribing on the tunnel phase runs before the
        // Thumb gets a chance to mark it handled; handledEventsToo is a belt-and-braces fallback.
        SeekSlider.AddHandler(PointerPressedEvent, OnSeekSliderPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekSlider.AddHandler(PointerReleasedEvent, OnSeekSliderPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

        SidebarContentGrid.SizeChanged += (_, _) => UpdateNavColumns();
    }

    // Two states only - one row of all 10 buttons, or two even rows of 5 - never a lopsided split
    // like 8+2. SidebarContentGrid.Bounds.Width (the nav row's actual container, not NavBar itself)
    // is what's checked: it reflects the sidebar's real allocated width regardless of whether NavBar
    // is currently stretching to fill it or, while Settings is active, capped/left-aligned instead -
    // binding this decision to NavBar's own width would be circular in that second case, since its
    // width would then depend on the very Columns value being decided.
    private const int NavBarButtonCount = 10;
    private const double NavBarMinComfortableColumnWidth = 50;
    private const double NavBarHorizontalMargin = 16; // NavBar's own Margin="8,8,8,4"

    private void UpdateNavColumns()
    {
        var available = SidebarContentGrid.Bounds.Width - NavBarHorizontalMargin;
        NavBar.Columns = available >= NavBarButtonCount * NavBarMinComfortableColumnWidth ? NavBarButtonCount : 5;
    }

    // GridSplitter resizes RootGrid's sidebar column directly via SetCurrentValue, which cooperates
    // with (rather than breaks) the existing MultiBinding - so it doesn't fight it, but also doesn't
    // flow back to the view model on its own. Push the resolved width back only once the drag ends
    // (see PlayerViewModel.SidebarWidth).
    private void OnContentSidebarSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        _player.SidebarWidth = RootGrid.ColumnDefinitions[0].ActualWidth;
    }

    // While the user is actively dragging (or clicking to jump), TimeChanged events from the player
    // must not overwrite the slider's value out from under their cursor - only the actual seek on
    // release should touch playback position (see PlayerViewModel.BeginUserSeek/EndUserSeek).
    private void OnSeekSliderPointerPressed(object? sender, RoutedEventArgs e) => _player.BeginUserSeek();

    private void OnSeekSliderPointerReleased(object? sender, RoutedEventArgs e) => _player.EndUserSeek();

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
        }
        else
        {
            WindowState = _windowStateBeforeFullscreen;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _player.IsFullscreen)
        {
            _player.ExitFullscreen();
            e.Handled = true;
            return;
        }

        // Space/Left/Right double as search-box typing (space, cursor movement) - only treat them as
        // playback shortcuts when focus isn't in a text input.
        if (e.Source is TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space when _player.CanPause:
                _player.TogglePauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right when _player.IsSeekable:
                _player.SkipForwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left when _player.IsSeekable:
                _player.SkipBackwardCommand.Execute(null);
                e.Handled = true;
                break;
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

        var saved = await _settingsStore.GetAsync("WindowState");
        if (saved is null)
        {
            return;
        }

        // "width|height|state" (older saves, before position was tracked) or
        // "width|height|state|x|y" - accepting both means an existing install doesn't lose its
        // saved size/state on the first launch after this X/Y addition.
        var parts = saved.Split('|');
        if (parts.Length >= 3 &&
            double.TryParse(parts[0], out var width) && width > 0 &&
            double.TryParse(parts[1], out var height) && height > 0 &&
            Enum.TryParse<WindowState>(parts[2], out var state))
        {
            Width = width;
            Height = height;
            WindowState = state == WindowState.Minimized ? WindowState.Normal : state;

            // Only trust a saved position if some currently-connected screen actually contains that
            // point - e.g. the window was last on a second monitor that's since been unplugged.
            // Leaving Position untouched otherwise falls back to Avalonia's own default placement,
            // exactly as if no position had ever been saved.
            if (parts.Length == 5 &&
                int.TryParse(parts[3], out var x) &&
                int.TryParse(parts[4], out var y) &&
                Screens.All.Any(screen => screen.Bounds.Contains(new PixelPoint(x, y))))
            {
                Position = new PixelPoint(x, y);
            }
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Never persist FullScreen itself - if the user closes while fullscreen, remember what was active before it.
        var effectiveState = _player.IsFullscreen ? _windowStateBeforeFullscreen : WindowState;
        var state = effectiveState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        var width = state == WindowState.Maximized ? Width : Bounds.Width;
        var height = state == WindowState.Maximized ? Height : Bounds.Height;
        _ = _settingsStore.SetAsync("WindowState", $"{width}|{height}|{state}|{Position.X}|{Position.Y}");

        // _forceClosing lets the "Force Exit" branch below re-invoke Close() without looping back
        // into this same warning - the second pass falls straight through instead of re-cancelling.
        if (_forceClosing || !_mainWindowViewModel.IsAnyRefreshInProgress)
        {
            return;
        }

        e.Cancel = true;

        var dialog = new FAContentDialog
        {
            Title = "Refresh in progress",
            Content = "A refresh is still running. Closing now may leave channels, the guide, or your library partially updated.",
            PrimaryButtonText = "Force Exit",
            CloseButtonText = "Wait",
            DefaultButton = FAContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == FAContentDialogResult.Primary)
        {
            _forceClosing = true;
            Close();
        }
    }
}
