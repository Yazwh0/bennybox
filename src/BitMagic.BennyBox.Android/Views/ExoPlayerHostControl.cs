using Android.App;
using AndroidX.Media3.UI;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Platform;
using BitMagic.BennyBox.Android.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BitMagic.BennyBox.Android.Views;

// Hosts a Media3 PlayerView bound to the app's Media3PlayerEngine's IExoPlayer inside the Avalonia
// visual tree - see Avalonia's "Embedding Android native views" docs. This is the same pattern
// proven working in the Phase 0 spike (BennyBox.Android\LibVlcAndroidSpike), just driven by the real
// DI-registered engine instead of constructing its own ExoPlayer. Parameterless so it can be placed
// directly in XAML - resolves the engine from App.Services itself rather than taking it as a
// constructor parameter.
public class ExoPlayerHostControl : NativeControlHost
{
    private readonly Media3PlayerEngine _engine = App.Services!.GetRequiredService<Media3PlayerEngine>();

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var context = (parent as AndroidViewControlHandle)?.View?.Context ?? Application.Context!;
        var playerView = new PlayerView(context)
        {
            Player = _engine.Player,
            // PlayerView shows its own default playback controller overlay (play/pause/skip/seek)
            // by default - AndroidShellView already has its own Avalonia-based transport bar bound to
            // PlayerViewModel, and the two rendered on top of each other, confusingly. This is the
            // video surface only; all playback UI lives in Avalonia.
            UseController = false
        };
        return new AndroidViewControlHandle(playerView);
    }
}
