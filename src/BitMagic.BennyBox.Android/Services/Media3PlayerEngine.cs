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
    private int _liveWindowRecoveries;

    // Exposed for the Android player view's NativeControlHost to bind a Media3 PlayerView to
    // directly - IPlayerEngine itself has no notion of a native player control (see IPlayerEngine's
    // comment).
    public IExoPlayer Player => _player ??= CreatePlayer();

    // Xtream catch-up links answer with a redirect from https to a plain-http CDN, which ExoPlayer
    // refuses by default ("Response code: 302") - VLC on desktop just follows it, so do the same.
    // DefaultDataSource still handles local files (downloads) and content:// itself.
    private IExoPlayer CreatePlayer()
    {
        var http = new AndroidX.Media3.DataSource.DefaultHttpDataSource.Factory().SetAllowCrossProtocolRedirects(true)!;
        var dataSources = new AndroidX.Media3.DataSource.DefaultDataSource.Factory(_context, http);
        var mediaSources = new AndroidX.Media3.ExoPlayer.Source.DefaultMediaSourceFactory(dataSources);
        // Decoder fallback: if the first decoder for a stream won't start (a 4K channel on a phone whose
        // main decoder tops out lower, say), try the device's other decoders before failing.
        var renderers = new DefaultRenderersFactory(_context).SetEnableDecoderFallback(true)!;
        return new ExoPlayerBuilder(_context, renderers).SetMediaSourceFactory(mediaSources)!.Build()!;
    }

    public Media3PlayerEngine(Context context)
    {
        _context = context;
        // ExoPlayer enforces same-thread access (whichever thread it was created on, i.e. the UI
        // thread here) - Timer callbacks run on a threadpool thread, so the actual Player access has
        // to be marshaled to the UI thread rather than happening directly in the timer callback.
        _pollTimer = new Timer(_ => Dispatcher.UIThread.Post(Poll), null, Timeout.Infinite, Timeout.Infinite);
    }

    // ExoPlayer picks its own decoders (and falls back by itself), so this isn't needed here.
    public bool SoftwareDecoding { get; set; }

    // See IPlayerEngine.DeviceCannotPlay: a decoding error (ExoPlayer's 4xxx codes - no decoder would
    // start, or the format is beyond this device), or a stream whose picture no decoder here supports.
    // Also true while playing a video this device says is beyond it ("Format exceeds selected codec's
    // capabilities" - 4K HDR HEVC on most phones): ExoPlayer tries anyway, and the decoder gives up after
    // a frame or two (or none), which the stall check catches - PlayerViewModel only takes it as "can't
    // play" when the picture froze early, so a 4K stream that does play and later stalls isn't blamed on it.
    public bool DeviceCannotPlay
    {
        get => _deviceCannotPlay || (_player is { } player && PlayingVideoBeyondDevice(player));
        private set => _deviceCannotPlay = value;
    }

    private bool _deviceCannotPlay;

    public void Play(string url)
    {
        _lastPlaybackState = Media3PlaybackState.Idle;
        _lastIsPlaying = false;
        _hadError = false;
        DeviceCannotPlay = false;
        _liveWindowRecoveries = 0;

        // ExoPlayer throws straight out of SetMediaItem for a stream type it has no module for (it
        // took the whole app down on a DASH channel before the DASH module was added) - whatever the
        // stream, it's an error for that channel, never a crash.
        try
        {
            Player.SetMediaItem(MediaItem.FromUri(url));
            Player.Prepare();
            Player.Play();
        }
        catch (Java.Lang.Exception ex)
        {
            global::Android.Util.Log.Warn("BennyBox", $"Couldn't start playback: {ex.Message}");
            _hadError = true;
            Dispatcher.UIThread.Post(() => EncounteredError?.Invoke(this, EventArgs.Empty));
            return;
        }

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
            // Fell behind a live stream's window (ERROR_CODE_BEHIND_LIVE_WINDOW, seen on live HLS like
            // the UHD sports channels): not a broken stream, just jump back to the live point and carry on.
            if (player.PlayerError.ErrorCode == 1002 && _liveWindowRecoveries < 3)
            {
                _liveWindowRecoveries++;
                player.SeekToDefaultPosition();
                player.Prepare();
                return;
            }

            _hadError = true;
            DeviceCannotPlay = player.PlayerError.ErrorCode is >= 4000 and < 5000;
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
                case Media3PlaybackState.Ready when HasNoPlayableVideo(player):
                    // ExoPlayer would otherwise play just the sound over a black screen.
                    _hadError = true;
                    DeviceCannotPlay = true;
                    player.Stop();
                    EncounteredError?.Invoke(this, EventArgs.Empty);
                    return;
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

    private static bool PlayingVideoBeyondDevice(IExoPlayer player) =>
        ((System.Collections.IEnumerable?)player.CurrentTracks?.Groups)?.Cast<Tracks.Group>()
            .Any(g => g.Type == C.TrackTypeVideo && g.IsSelected && !g.IsSupported) == true;

    // The stream has a picture, but none of it was picked to play because nothing here can decode it.
    private static bool HasNoPlayableVideo(IExoPlayer player)
    {
        var video = ((System.Collections.IEnumerable?)player.CurrentTracks?.Groups)?.Cast<Tracks.Group>().Where(g => g.Type == C.TrackTypeVideo).ToList() ?? [];
        return video.Count > 0 && !video.Any(g => g.IsSelected) && !video.Any(g => g.IsSupported);
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        _player?.Release();
    }
}
