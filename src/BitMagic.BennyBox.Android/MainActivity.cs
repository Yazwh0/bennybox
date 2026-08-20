using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using Avalonia;
using Avalonia.Android;

namespace BitMagic.BennyBox.Android;

[Activity(
    Label = "Benny Box",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    public static MainActivity? Instance { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Instance = this;
    }

    protected override void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    // Called by AndroidShellView.axaml.cs whenever Player.IsFullscreen changes while something is
    // actually playing - mirrors desktop's WindowState.FullScreen (see
    // MainWindow.axaml.cs.OnPlayerPropertyChanged): hides the system status/nav bars and rotates to
    // landscape for a real cinema-style view, since AndroidShellView's player overlay already covers
    // the app's own chrome (tab bar) but leaves the OS status bar and portrait lock in place otherwise.
    //
    // Exiting fullscreen is a small in-app button (see AndroidShellView.axaml), not the hardware/
    // gesture back action - that route was tried first (Avalonia's own BackRequested event, see
    // AvaloniaActivity.OnBackInvoked) and reliably backgrounded the whole app instead of just exiting
    // fullscreen, even with e.Handled confirmed true via logging. Android 15/API 35's predictive-back
    // system appears to commit its own "go home" transition independent of what the app's callback
    // does in this configuration (emulator, no explicit android:enableOnBackInvokedCallback opt-in) -
    // not worth chasing further when a small always-present in-app button sidesteps the whole native
    // integration question.
    public void SetImmersiveFullscreen(bool enabled)
    {
        var window = Window;
        if (window is null)
        {
            return;
        }

        var controller = WindowCompat.GetInsetsController(window, window.DecorView!);
        if (controller is null)
        {
            return;
        }

        WindowCompat.SetDecorFitsSystemWindows(window, !enabled);
        if (enabled)
        {
            controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
            controller.Hide(WindowInsetsCompat.Type.SystemBars());
            RequestedOrientation = ScreenOrientation.SensorLandscape;
        }
        else
        {
            controller.Show(WindowInsetsCompat.Type.SystemBars());
            RequestedOrientation = ScreenOrientation.Unspecified;
        }
    }
}
