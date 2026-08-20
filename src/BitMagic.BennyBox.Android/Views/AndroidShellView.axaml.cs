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
    }

    // Tap-to-reveal for ExitFullscreenButton (hidden by default while fullscreen - see its own
    // IsVisible) - the tap-catcher Border this is wired to only exists while Player.IsFullscreen, so
    // there's no need to re-check that here.
    private void OnVideoTapped(object? sender, RoutedEventArgs e) => ExitFullscreenButton.IsVisible = !ExitFullscreenButton.IsVisible;
}
