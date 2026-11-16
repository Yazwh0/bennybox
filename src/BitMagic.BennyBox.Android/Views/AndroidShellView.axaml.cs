using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using BitMagic.BennyBox.Android.ViewModels;
using BitMagic.BennyBox.ViewModels;

namespace BitMagic.BennyBox.Android.Views;

public partial class AndroidShellView : UserControl
{
    private PlayerViewModel? _player;

    public AndroidShellView()
    {
        InitializeComponent();
        VideoHost.VideoTapped += OnVideoTapped;

        // DataContext is set after construction (see App.axaml.cs), not passed in via constructor
        // like desktop's MainWindow gets PlayerViewModel injected directly - so Player can only be
        // reached once it's actually assigned.
        DataContextChanged += (_, _) =>
        {
            if (_player is not null)
            {
                _player.PropertyChanged -= OnPlayerPropertyChanged;
            }

            _player = (DataContext as AndroidShellViewModel)?.Player;

            if (_player is not null)
            {
                _player.PropertyChanged += OnPlayerPropertyChanged;
            }
        };
    }

    // Mirrors desktop's MainWindow.axaml.cs.OnPlayerPropertyChanged - see
    // MainActivity.SetImmersiveFullscreen for what "fullscreen" means on Android (system bars +
    // landscape, since the app's own chrome is already covered by the player overlay either way).
    // Also re-checked on NowPlayingChannelName, not just IsFullscreen: PlayerViewModel.Stop() doesn't
    // reset IsFullscreen (matches desktop's own equivalent gap), so without this, pressing Close while
    // fullscreen would leave the phone stuck in landscape/immersive mode with no player on screen to
    // exit fullscreen from.
    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerViewModel.NowPlayingChannelName) or nameof(PlayerViewModel.IsPaused))
        {
            UpdateKeepScreenOn();
        }

        if (e.PropertyName is not (nameof(PlayerViewModel.IsFullscreen) or nameof(PlayerViewModel.NowPlayingChannelName)))
        {
            return;
        }

        var shouldBeImmersive = _player is { IsFullscreen: true, NowPlayingChannelName: not null };
        MainActivity.Instance?.SetImmersiveFullscreen(shouldBeImmersive);
    }

    // Like any video player: the screen stays on while something is playing, and goes back to the
    // phone's normal timeout when it's paused or stopped.
    private void UpdateKeepScreenOn()
    {
        var window = MainActivity.Instance?.Window;
        if (window is null)
        {
            return;
        }

        if (_player is { NowPlayingChannelName: not null, IsPaused: false })
        {
            window.AddFlags(global::Android.Views.WindowManagerFlags.KeepScreenOn);
        }
        else
        {
            window.ClearFlags(global::Android.Views.WindowManagerFlags.KeepScreenOn);
        }
    }

    // A tap on the video in fullscreen shows or hides the control bar below it (see
    // AndroidShellViewModel.ShowFullscreenControls). Comes from the native video view itself - see
    // ExoPlayerHostControl.VideoTapped for why nothing is laid over the picture.
    //
    // One tap can arrive twice - from the native video view and from the Avalonia area underneath
    // (both get the touch) - so taps within a short window count once, or the bar would show and
    // immediately hide again.
    private static readonly TimeSpan VideoTapDebounce = TimeSpan.FromMilliseconds(400);
    private DateTime _lastVideoTap;

    private void OnVideoAreaTapped(object? sender, RoutedEventArgs e) => OnVideoTapped(sender, EventArgs.Empty);

    private void OnVideoTapped(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if (now - _lastVideoTap < VideoTapDebounce)
        {
            return;
        }

        _lastVideoTap = now;
        if (DataContext is AndroidShellViewModel { Player.IsFullscreen: true } shell)
        {
            shell.ShowFullscreenControls = !shell.ShowFullscreenControls;
        }
    }

    // Material's breakpoint between compact (phone) and medium (tablet/foldable) widths.
    private const double WideTabBarWidth = 600;

    // Phones show four tabs plus More; wide screens show all nine. See the tab bar's comment.
    private void OnTabBarSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= WideTabBarWidth;
        SearchTab.IsVisible = ClipsTab.IsVisible = DownloadsTab.IsVisible = FavoritesTab.IsVisible = SettingsTab.IsVisible = wide;
        MoreTab.IsVisible = !wide;
        TabBar.Columns = wide ? 9 : 5;
    }

    // Simple picture mode's player buttons (see the simple-control bar in the XAML). A press-and-hold
    // reads the button's name aloud; the release that ends the hold mustn't then also press it, so
    // Click marks itself handled, which stops Button running its Command.
    private bool _suppressNextSimpleClick;

    private void OnSimpleControlHolding(object? sender, Avalonia.Input.HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != Avalonia.Input.HoldingState.Started || sender is not Avalonia.Controls.Button button)
        {
            return;
        }

        _suppressNextSimpleClick = true;
        var label = button.Tag as string;
        if (label == "Play" && _player is { IsPaused: false })
        {
            label = "Pause";
        }

        Services.ReadAloud.Speak(label);
    }

    private void OnSimpleCannotPlayHolding(object? sender, Avalonia.Input.HoldingRoutedEventArgs e)
    {
        if (e.HoldingState == Avalonia.Input.HoldingState.Started)
        {
            Services.ReadAloud.Speak("This won't play on this device. Try a different channel.");
        }
    }

    private void OnSimpleRetryHolding(object? sender, Avalonia.Input.HoldingRoutedEventArgs e)
    {
        if (e.HoldingState == Avalonia.Input.HoldingState.Started)
        {
            _suppressNextSimpleClick = true;
            Services.ReadAloud.Speak("Try again");
        }
    }

    private void OnSimpleControlClick(object? sender, RoutedEventArgs e)
    {
        if (_suppressNextSimpleClick)
        {
            _suppressNextSimpleClick = false;
            e.Handled = true;
        }
    }

    // Quieter/Louder step the phone's own media volume and show its usual volume bar, so the change is
    // visible. Louder also unmutes, since simple mode has no mute button to undo a mute set elsewhere.
    private void OnSimpleVolumeClick(object? sender, RoutedEventArgs e)
    {
        if (_suppressNextSimpleClick)
        {
            _suppressNextSimpleClick = false;
            return;
        }

        var louder = (sender as Avalonia.Controls.Button)?.Tag as string == "Louder";
        if (louder && _player is { IsMuted: true })
        {
            _player.ToggleMuteCommand.Execute(null);
        }

        var audio = global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.AudioService) as global::Android.Media.AudioManager;
        audio?.AdjustStreamVolume(global::Android.Media.Stream.Music,
            louder ? global::Android.Media.Adjust.Raise : global::Android.Media.Adjust.Lower,
            global::Android.Media.VolumeNotificationFlags.ShowUi);
    }

    private void OnMoreItemClick(object? sender, RoutedEventArgs e)
    {
        MoreTab.Flyout?.Hide();
        if (sender is Avalonia.Controls.Button { Tag: string tag } && DataContext is AndroidShellViewModel shell)
        {
            shell.NavigateCommand.Execute(tag);
        }
    }
}
