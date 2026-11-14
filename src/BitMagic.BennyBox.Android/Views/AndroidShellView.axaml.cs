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
        if (e.PropertyName is not (nameof(PlayerViewModel.IsFullscreen) or nameof(PlayerViewModel.NowPlayingChannelName)))
        {
            return;
        }

        var shouldBeImmersive = _player is { IsFullscreen: true, NowPlayingChannelName: not null };
        MainActivity.Instance?.SetImmersiveFullscreen(shouldBeImmersive);

        // Reset to hidden every time fullscreen is (re)entered/exited, not just once - otherwise a
        // second fullscreen session in the same player-overlay lifetime could start with the button
        // still showing from wherever the user last left it tap-toggled to.
        ExitFullscreenButton.IsVisible = false;
        SimpleFullscreenControls.IsVisible = false;
    }

    // Tap-to-reveal for ExitFullscreenButton (hidden by default while fullscreen - see its own
    // IsVisible) - the tap-catcher Border this is wired to only exists while Player.IsFullscreen, so
    // there's no need to re-check that here.
    // In simple picture mode the tap shows/hides its big fullscreen controls instead.
    private void OnVideoTapped(object? sender, RoutedEventArgs e)
    {
        if ((DataContext as AndroidShellViewModel)?.Settings.IsSimpleMode == true)
        {
            SimpleFullscreenControls.IsVisible = !SimpleFullscreenControls.IsVisible;
        }
        else
        {
            ExitFullscreenButton.IsVisible = !ExitFullscreenButton.IsVisible;
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
