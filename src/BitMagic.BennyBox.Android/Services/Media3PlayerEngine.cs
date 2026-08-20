using Android.Content;
using AndroidX.Media3.Common;
using AndroidX.Media3.ExoPlayer;
using Avalonia.Threading;
using BitMagic.BennyBox.UI.Services;

namespace BitMagic.BennyBox.Android.Services;

// Media3's androidx.media3.common.Player.STATE_* constants - stable, well-known values from the
// Java interface. Not referenced symbolically here because the binding duplicates them as a nested
// InterfaceConsts class per *implementor* (BasePlayer.InterfaceConsts, ForwardingPlayer.
// InterfaceConsts, ...) rather than once on IPlayer/IExoPlayer itself.
internal static class Media3PlaybackState
{
    public const int Idle = 1;
    public const int Buffering = 2;
    public const int Ready = 3;
    public const int Ended = 4;
}

// Android IPlayerEngine implementation, wrapping Media3/ExoPlayer - see the Android port plan for
// why Android needs this instead of reusing desktop's LibVlcPlayerEngine: LibVLCSharp.Avalonia's
// VideoView doesn't work on Android.
//
// Deliberately polls Player.PlaybackState/IsPlaying/PlayerError on a timer instead of registering a
// Player.IPlayerListener - that interface is a real JNI-bound callback interface with ~35 methods,
// and implementing only the ones this class cares about threw
// java.lang.AbstractMethodError: abstract method "...onSurfaceSizeChanged(int, int)" the moment
// ExoPlayer actually dispatched a callback this class hadn't overridden (C#'s default-interface-
// implementation syntax satisfies the compiler but not the JNI proxy the binding generates, which
// only stubs out methods a class explicitly overrides). Polling avoids that whole class of risk at
// the cost of slightly-less-immediate event delivery, which is a fine trade at typical UI-relevant
// intervals (this uses ~300ms).
//
// Track selection (AudioTracks/SubtitleTracks/SelectAudioTrack/SelectSubtitleTrack) is NOT yet
// implemented - it's a real, separate piece of work (ExoPlayer's Tracks/TrackSelectionParameters API
// is structurally different from LibVLC's integer-id ESAdded/ESDeleted model, not just a rename) and
// there's no Android player screen yet to exercise it against. Deferred to whenever the real Android
// player UI is built, not attempted here.
public class Media3PlayerEngine : IPlayerEngine, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);

    private readonly Context _context;
    private readonly Timer _pollTimer;
    private IExoPlayer? _player;
    private float _volumeBeforeMute = 1f;
    private bool _isMuted;

    private int _lastPlaybackState = Media3PlaybackState.Idle;
    private bool _lastIsPlaying;
    private bool _hadError;

    // Exposed for the Android player view's NativeControlHost to bind a Media3 PlayerView to
    // directly - IPlayerEngine itself has no notion of a native player control (see IPlayerEngine's
    // comment).
    public IExoPlayer Player => _player ??= new ExoPlayerBuilder(_context).Build()!;

    public Media3PlayerEngine(Context context)
    {
        _context = context;
        // ExoPlayer enforces same-thread access (whichever thread it was created on, i.e. the UI
        // thread here) - Timer callbacks run on a threadpool thread, so the actual Player access has
        // to be marshaled to the UI thread rather than happening directly in the timer callback.
        _pollTimer = new Timer(_ => Dispatcher.UIThread.Post(Poll), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Play(string url)
    {
        Player.SetMediaItem(MediaItem.FromUri(url));
        Player.Prepare();
        Player.Play();
        _lastPlaybackState = Media3PlaybackState.Idle;
        _lastIsPlaying = false;
        _hadError = false;
        _pollTimer.Change(TimeSpan.Zero, PollInterval);
    }

    public void Stop()
    {
        Player.Stop();
        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    public void SetPaused(bool paused)
    {
        if (paused)
        {
            Player.Pause();
        }
        else
        {
            Player.Play();
        }
    }

    public long PositionMs
    {
        get => Player.CurrentPosition;
        set => Player.SeekTo(value);
    }

    public long DurationMs => Player.Duration > 0 ? Player.Duration : 0;

    // ExoPlayer supports pausing any loaded media - there's no per-stream capability flag the way
    // LibVLC exposes CanPause, so this is "is there something loaded" rather than a real stream
    // capability check.
    public bool CanPause => Player.PlaybackState != Media3PlaybackState.Idle;

    public bool IsSeekable => Player.IsCurrentMediaItemSeekable;

    public int Volume
    {
        get => (int)Math.Round(Player.Volume * 100);
        set => Player.Volume = Math.Clamp(value, 0, 100) / 100f;
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (value == _isMuted)
            {
                return;
            }

            _isMuted = value;
            if (value)
            {
                _volumeBeforeMute = Player.Volume;
                Player.Volume = 0f;
            }
            else
            {
                Player.Volume = _volumeBeforeMute;
            }
        }
    }

    // See IPlayerEngine.RenderedFrameCount - DecoderCounters.RenderedOutputBufferCount is Media3's
    // equivalent of LibVLC's Media.Statistics.DisplayedPictures.
    public long RenderedFrameCount => Player.VideoDecoderCounters?.RenderedOutputBufferCount ?? 0;

    public IReadOnlyList<TrackOption> AudioTracks => [];

    public IReadOnlyList<TrackOption> SubtitleTracks => [];

    public string? SelectedAudioTrackId => null;

    public string? SelectedSubtitleTrackId => null;

    public void SelectAudioTrack(string id)
    {
        // Not yet implemented - see this class's header comment.
    }

    public void SelectSubtitleTrack(string id)
    {
        // Not yet implemented - see this class's header comment.
    }

    public event EventHandler? Playing;
    public event EventHandler? Paused;
    public event EventHandler? Stopped;
    public event EventHandler? EndReached;
    public event EventHandler? EncounteredError;
    public event EventHandler<double>? Buffering;
    public event EventHandler<long>? TimeChanged;
    public event EventHandler<long>? LengthChanged;
    public event EventHandler? TracksChanged;

    private void Poll()
    {
        var player = _player;
        if (player is null)
        {
            return;
        }

        if (player.PlayerError is not null && !_hadError)
        {
            _hadError = true;
            EncounteredError?.Invoke(this, EventArgs.Empty);
            return;
        }

        var state = player.PlaybackState;
        if (state != _lastPlaybackState)
        {
            _lastPlaybackState = state;
            switch (state)
            {
                case Media3PlaybackState.Buffering:
                    Buffering?.Invoke(this, 0);
                    break;
                case Media3PlaybackState.Ended:
                    EndReached?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        var isPlaying = player.IsPlaying;
        if (isPlaying != _lastIsPlaying)
        {
            _lastIsPlaying = isPlaying;
            if (isPlaying)
            {
                Buffering?.Invoke(this, 100);
                Playing?.Invoke(this, EventArgs.Empty);
            }
            else if (state == Media3PlaybackState.Ready)
            {
                Paused?.Invoke(this, EventArgs.Empty);
            }
        }

        TimeChanged?.Invoke(this, player.CurrentPosition);
        if (player.Duration > 0)
        {
            LengthChanged?.Invoke(this, player.Duration);
        }
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        _player?.Release();
    }
}
