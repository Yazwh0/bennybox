namespace BitMagic.BennyBox.UI.Services;

// Id/Name pair for one audio or subtitle track. Id is an engine-internal string identifier (LibVLC:
// the numeric track id as a string; ExoPlayer/Media3: whatever the engine needs to re-select the
// same track) - callers should treat it as opaque and only ever pass it back into
// SelectAudioTrack/SelectSubtitleTrack.
public sealed record TrackOption(string Id, string Name);

// Wraps whichever native player (LibVLC on desktop, ExoPlayer/Media3 on Android - see the Android
// port plan) actually renders video, so PlayerViewModel can stay platform-neutral. Deliberately
// mirrors LibVLCSharp.Shared.MediaPlayer's shape closely rather than inventing a more "generic"
// player API - PlayerViewModel's stall-watchdog/load-timeout/pause-reconnect/track-preference logic
// is real, hard-won behavior (see PlayerViewModel's comments) that both engines need to support
// identically, not something to simplify away at the abstraction boundary.
public interface IPlayerEngine
{
    void Play(string url);

    void Stop();

    void SetPaused(bool paused);

    // Current playback position. The setter seeks.
    long PositionMs { get; set; }

    long DurationMs { get; }

    bool CanPause { get; }

    bool IsSeekable { get; }

    int Volume { get; set; }

    bool IsMuted { get; set; }

    // Monotonically increasing count of frames actually handed to the video output - NOT the same as
    // PositionMs advancing (see PlayerViewModel.StartStallWatchdog: some streams keep their demux
    // clock free-running for minutes after the connection has actually died, so PositionMs alone is
    // not a trustworthy "is this actually still playing" signal). LibVLC: Media.Statistics.
    // DisplayedPictures. ExoPlayer: DecoderCounters.RenderedOutputBufferCount.
    long RenderedFrameCount { get; }

    IReadOnlyList<TrackOption> AudioTracks { get; }

    IReadOnlyList<TrackOption> SubtitleTracks { get; }

    string? SelectedAudioTrackId { get; }

    string? SelectedSubtitleTrackId { get; }

    void SelectAudioTrack(string id);

    void SelectSubtitleTrack(string id);

    event EventHandler? Playing;

    event EventHandler? Paused;

    event EventHandler? Stopped;

    event EventHandler? EndReached;

    event EventHandler? EncounteredError;

    // Cache percentage, 0-100.
    event EventHandler<double>? Buffering;

    event EventHandler<long>? TimeChanged;

    event EventHandler<long>? LengthChanged;

    // Fired whenever the audio/subtitle track list changes - listeners should re-read
    // AudioTracks/SubtitleTracks/SelectedAudioTrackId/SelectedSubtitleTrackId rather than trying to
    // apply an incremental add/delete, matching how PlayerViewModel.RefreshTrackLists already treats
    // the engine's track lists as the source of truth.
    event EventHandler? TracksChanged;
}
