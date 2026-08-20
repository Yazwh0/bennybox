using BitMagic.BennyBox.UI.Services;
using LibVLCSharp.Shared;

namespace BitMagic.BennyBox.Services;

// Desktop IPlayerEngine implementation, wrapping LibVLCSharp.Shared.MediaPlayer - see the Android
// port plan for why Android needs a different engine (Media3/ExoPlayer) rather than reusing this
// one: LibVLCSharp.Avalonia's VideoView doesn't work on Android.
public class LibVlcPlayerEngine : IPlayerEngine, IDisposable
{
    private readonly LibVLC _libVlc;

    // Reassigned wholesale (never mutated in place) by RefreshTrackLists - AudioTracks/SubtitleTracks
    // hand callers this same reference, and PlayerViewModel enumerates it on the UI thread from a
    // TracksChanged handler that can itself be queued behind another ES event already mutating this
    // state. A shared mutable List that's Clear()/Add()-ed in place threw "Collection was modified"
    // under exactly that interleaving; swapping the reference instead means any list a caller already
    // has stays untouched even if a newer one replaces it here moments later.
    private IReadOnlyList<TrackOption> _audioTracks = [];
    private IReadOnlyList<TrackOption> _subtitleTracks = [];
    private Media? _currentMedia;

    // Exposed for MainWindow.axaml.cs to bind LibVLCSharp.Avalonia's VideoView to directly - the
    // IPlayerEngine interface itself has no notion of a native player control, since that's a
    // desktop (LibVLC VideoView) vs. Android (NativeControlHost + ExoPlayer PlayerView) concept, not
    // a portable one.
    public MediaPlayer MediaPlayer { get; }

    public LibVlcPlayerEngine(LibVLC libVlc)
    {
        _libVlc = libVlc;
        MediaPlayer = new MediaPlayer(_libVlc);

        MediaPlayer.Playing += (_, _) => Playing?.Invoke(this, EventArgs.Empty);
        MediaPlayer.Paused += (_, _) => Paused?.Invoke(this, EventArgs.Empty);
        MediaPlayer.Stopped += (_, _) => Stopped?.Invoke(this, EventArgs.Empty);
        MediaPlayer.EndReached += (_, _) => EndReached?.Invoke(this, EventArgs.Empty);
        MediaPlayer.EncounteredError += (_, _) => EncounteredError?.Invoke(this, EventArgs.Empty);
        MediaPlayer.Buffering += (_, e) => Buffering?.Invoke(this, e.Cache);
        MediaPlayer.TimeChanged += (_, e) => TimeChanged?.Invoke(this, e.Time);
        MediaPlayer.LengthChanged += (_, e) => LengthChanged?.Invoke(this, e.Length);
        MediaPlayer.ESAdded += OnEsChanged;
        MediaPlayer.ESDeleted += OnEsChanged;
    }

    public void Play(string url)
    {
        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVlc, url, FromType.FromLocation);
        MediaPlayer.Play(_currentMedia);
    }

    public void Stop() => MediaPlayer.Stop();

    public void SetPaused(bool paused) => MediaPlayer.SetPause(paused);

    public long PositionMs
    {
        get => MediaPlayer.Time;
        set => MediaPlayer.Time = value;
    }

    public long DurationMs => MediaPlayer.Length;

    public bool CanPause => MediaPlayer.CanPause;

    public bool IsSeekable => MediaPlayer.IsSeekable;

    public int Volume
    {
        get => MediaPlayer.Volume;
        set => MediaPlayer.Volume = value;
    }

    public bool IsMuted
    {
        get => MediaPlayer.Mute;
        set => MediaPlayer.Mute = value;
    }

    // See the "no new frame displayed" comment on PlayerViewModel.StartStallWatchdog for why this,
    // not PositionMs, is what stall detection is built on.
    public long RenderedFrameCount => _currentMedia?.Statistics.DisplayedPictures ?? 0;

    public IReadOnlyList<TrackOption> AudioTracks => _audioTracks;

    public IReadOnlyList<TrackOption> SubtitleTracks => _subtitleTracks;

    public string? SelectedAudioTrackId => MediaPlayer.AudioTrack is var id and >= 0 ? id.ToString() : null;

    public string? SelectedSubtitleTrackId => MediaPlayer.Spu is var id and >= 0 ? id.ToString() : null;

    public void SelectAudioTrack(string id)
    {
        if (int.TryParse(id, out var trackId))
        {
            MediaPlayer.SetAudioTrack(trackId);
        }
    }

    public void SelectSubtitleTrack(string id)
    {
        if (int.TryParse(id, out var trackId))
        {
            MediaPlayer.SetSpu(trackId);
        }
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

    private void OnEsChanged(object? sender, EventArgs e)
    {
        var type = e switch
        {
            MediaPlayerESAddedEventArgs added => added.Type,
            MediaPlayerESDeletedEventArgs deleted => deleted.Type,
            _ => (TrackType?)null
        };

        if (type is not (TrackType.Audio or TrackType.Text))
        {
            return;
        }

        RefreshTrackLists();
        TracksChanged?.Invoke(this, EventArgs.Empty);
    }

    // Rereads the full track lists from libVLC rather than incrementally applying the add/delete
    // event that triggered this - the description arrays are already the source of truth and cheap
    // to reread.
    private void RefreshTrackLists()
    {
        _audioTracks = (MediaPlayer.AudioTrackDescription ?? [])
            .Select(track => new TrackOption(track.Id.ToString(), track.Name))
            .ToList();

        _subtitleTracks = (MediaPlayer.SpuDescription ?? [])
            .Select(track => new TrackOption(track.Id.ToString(), track.Name))
            .ToList();
    }

    public void Dispose()
    {
        MediaPlayer.ESAdded -= OnEsChanged;
        MediaPlayer.ESDeleted -= OnEsChanged;
        _currentMedia?.Dispose();
        MediaPlayer.Dispose();
    }
}
