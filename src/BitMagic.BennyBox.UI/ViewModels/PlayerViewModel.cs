using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using BitMagic.BennyBox.Messages;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.UI.Services;
using Microsoft.Extensions.Logging;

namespace BitMagic.BennyBox.ViewModels;

public partial class PlayerViewModel : ViewModelBase, IDisposable
{
    // Shared by Live TV/Guide/Favorites so a resize on one page is reflected on the others too, and
    // so the fullscreen-collapse converter always has the last user-chosen width to restore to.
    public const double MinSidebarWidth = 180;
    public const double MaxSidebarWidth = 560;

    private static readonly TimeSpan ProgressSaveInterval = TimeSpan.FromSeconds(15);

    // Defaults for the four playback timings below - user-editable on the Settings page (see
    // SettingsViewModel), persisted via ISettingsStore, and re-read live via
    // PlaybackTimingSettingsChangedMessage since this VM is a long-lived singleton rather than
    // something that gets recreated whenever Settings does.
    private static readonly TimeSpan DefaultSkipInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultLoadTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultStallThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultPauseReconnectThreshold = TimeSpan.FromSeconds(45);

    // A title is treated as "finished" (and its bookmark removed rather than saved) once playback
    // gets this close to the reported duration - avoids leaving a useless "resume at 99%" entry
    // behind for everything the user watches to the end.
    private const double CompletionThreshold = 0.95;

    private readonly ISettingsStore _settingsStore;
    private readonly IWatchProgressRepository _watchProgressRepository;
    private readonly IWatchedItemRepository _watchedItemRepository;
    private readonly DownloadPlaybackResolver _downloadPlaybackResolver;
    private readonly ILogger<PlayerViewModel> _logger;
    private DispatcherTimer? _loadTimeoutTimer;
    private DispatcherTimer? _progressSaveTimer;
    private DispatcherTimer? _stallWatchdogTimer;
    private long _lastKnownTimeMs;
    private DateTime _lastTimeChangeUtc;
    private long _lastDisplayedPictures;
    private DateTime _lastDisplayedPicturesChangeUtc;
    private DateTime? _pausedAtUtc;
    private TimeSpan _skipInterval = DefaultSkipInterval;
    private TimeSpan _loadTimeout = DefaultLoadTimeout;
    private TimeSpan _stallThreshold = DefaultStallThreshold;
    private TimeSpan _pauseReconnectThreshold = DefaultPauseReconnectThreshold;
    private string? _preferredAudioLanguage;
    private string? _preferredSubtitleLanguage;

    // Only the *first* track-list refresh for a given PlayUrl should apply the language preference -
    // later refreshes (a track appearing/disappearing mid-stream) instead follow the engine's current
    // selection, which is whatever the user last manually picked, so a mid-stream track-list change
    // doesn't stomp on their in-session choice. Audio and subtitle tracks can change independently, so
    // each gets its own flag rather than one shared "preferences applied" bool.
    private bool _audioPreferenceApplied;
    private bool _subtitlePreferenceApplied;
    private string? _currentUrl;
    private bool _isApplyingSavedSidebarWidth;
    private bool _isApplyingSavedVolume;
    private bool _isUserSeeking;
    private bool _isSyncingTrackSelection;

    // True once the current PlayUrl call has actually shown a frame (OnPlaying fired) - distinguishes
    // "still connecting, nothing to look at yet" from "playing fine but hit a brief re-buffer", since
    // the engine's Buffering event fires for both and doesn't reset IsPlaying for the latter.
    private bool _hasShownFrame;

    // Set only while playing a movie/episode - live channels are never tracked. See PlayWithResumeAsync.
    private WatchProgressContentType? _currentContentType;
    private Guid? _currentProfileId;
    private string? _currentContentKey;
    private string? _currentTitle;
    private string? _currentCoverUrl;
    private long? _pendingResumeMs;

    // Desktop's MainWindow.axaml.cs casts this to LibVlcPlayerEngine to bind LibVLCSharp.Avalonia's
    // VideoView directly - IPlayerEngine itself has no notion of a native player control (see
    // IPlayerEngine's comment).
    public IPlayerEngine Engine { get; }

    // Populated from the engine's TracksChanged event as it discovers tracks in the current stream -
    // empty until then, so the UI only shows a selector once there's something to choose between.
    public ObservableCollection<TrackOption> AudioTracks { get; } = [];
    public ObservableCollection<TrackOption> SubtitleTracks { get; } = [];

    public bool HasMultipleAudioTracks => AudioTracks.Count > 1;
    public bool HasSubtitleTracks => SubtitleTracks.Count > 0;

    [ObservableProperty]
    private TrackOption? _selectedAudioTrack;

    [ObservableProperty]
    private TrackOption? _selectedSubtitleTrack;

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingOverlay))]
    [NotifyPropertyChangedFor(nameof(ShowBufferingBadge))]
    private bool _isBuffering;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingOverlay))]
    [NotifyPropertyChangedFor(nameof(ShowBufferingBadge))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private bool _hasPlaybackError;

    // Full video-area takeover: no usable frame yet (still connecting) or playback failed outright.
    // The video surface's IsVisible is bound to !ShowLoadingOverlay (see MainWindow.axaml) so this
    // panel can actually be seen - libVLC's native surface always paints over ordinary Avalonia
    // content regardless of z-order, so there's no way to draw this "on top of" a visible video.
    public bool ShowLoadingOverlay => (IsBuffering && !_hasShownFrame) || HasPlaybackError;

    // Small, unobtrusive indicator for a brief re-buffer once a frame is already showing - the video
    // itself keeps showing its last frame underneath rather than being hidden, since a full takeover
    // for every transient stall would flicker far more than it helps.
    public bool ShowBufferingBadge => IsBuffering && _hasShownFrame && !HasPlaybackError;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseButtonText))]
    private bool _isPaused;

    [ObservableProperty]
    private string? _nowPlayingChannelName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullscreenButtonText))]
    private bool _isFullscreen;

    [ObservableProperty]
    private double _sidebarWidth = 280;

    [ObservableProperty]
    private int _volume = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuteButtonText))]
    private bool _isMuted;

    public string MuteButtonText => IsMuted ? "🔇" : "🔊";

    // Live channels are usually not seekable/pausable, VOD episodes always are - rather than
    // hardcoding that assumption, these mirror the engine's own per-stream capability flags (some
    // IPTV "live" streams do support timeshifting and report seekable too), so the seek bar and
    // pause/skip buttons only ever appear when the current stream genuinely supports them.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    private bool _canPause;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SkipForwardCommand))]
    [NotifyCanExecuteChangedFor(nameof(SkipBackwardCommand))]
    private bool _isSeekable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionLabel))]
    private double _seekSliderValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationLabel))]
    private double _durationSeconds;

    public string FullscreenButtonText => IsFullscreen ? "Exit Fullscreen" : "Fullscreen";
    public string PlayPauseButtonText => IsPaused ? "Play" : "Pause";
    public string PositionLabel => FormatTime(SeekSliderValue);
    public string DurationLabel => FormatTime(DurationSeconds);
    public string SkipBackwardLabel => $"⏪ {(int)_skipInterval.TotalSeconds}s";
    public string SkipForwardLabel => $"{(int)_skipInterval.TotalSeconds}s ⏩";

    public PlayerViewModel(IPlayerEngine engine, ISettingsStore settingsStore, IWatchProgressRepository watchProgressRepository, IWatchedItemRepository watchedItemRepository, DownloadPlaybackResolver downloadPlaybackResolver, ILogger<PlayerViewModel> logger)
    {
        Engine = engine;
        _settingsStore = settingsStore;
        _watchProgressRepository = watchProgressRepository;
        _watchedItemRepository = watchedItemRepository;
        _downloadPlaybackResolver = downloadPlaybackResolver;
        _logger = logger;

        Engine.Playing += OnPlaying;
        Engine.Paused += OnPaused;
        Engine.Buffering += OnBuffering;
        Engine.EncounteredError += OnEncounteredError;
        Engine.EndReached += OnEndReached;
        Engine.Stopped += OnStopped;
        Engine.TimeChanged += OnTimeChanged;
        Engine.LengthChanged += OnLengthChanged;
        Engine.TracksChanged += OnTracksChanged;

        _ = LoadSidebarWidthAsync();
        _ = LoadVolumeAsync();
        _ = LoadPlaybackTimingSettingsAsync();
        _ = LoadTrackPreferencesAsync();
        WeakReferenceMessenger.Default.Register<PlaybackTimingSettingsChangedMessage>(this, (_, _) => _ = LoadPlaybackTimingSettingsAsync());
        WeakReferenceMessenger.Default.Register<PlaybackTrackPreferencesChangedMessage>(this, (_, _) => _ = LoadTrackPreferencesAsync());
    }

    private async Task LoadPlaybackTimingSettingsAsync()
    {
        _skipInterval = await GetSecondsSettingAsync("PlaybackSkipIntervalSeconds", DefaultSkipInterval);
        OnPropertyChanged(nameof(SkipBackwardLabel));
        OnPropertyChanged(nameof(SkipForwardLabel));
        _loadTimeout = await GetSecondsSettingAsync("PlaybackLoadTimeoutSeconds", DefaultLoadTimeout);
        _stallThreshold = await GetSecondsSettingAsync("PlaybackStallThresholdSeconds", DefaultStallThreshold);
        _pauseReconnectThreshold = await GetSecondsSettingAsync("PlaybackPauseReconnectThresholdSeconds", DefaultPauseReconnectThreshold);
    }

    private async Task LoadTrackPreferencesAsync()
    {
        _preferredAudioLanguage = await _settingsStore.GetAsync("PreferredAudioLanguage");
        _preferredSubtitleLanguage = await _settingsStore.GetAsync("PreferredSubtitleLanguage");
    }

    private async Task<TimeSpan> GetSecondsSettingAsync(string key, TimeSpan fallback)
    {
        var saved = await _settingsStore.GetAsync(key);
        return saved is not null && int.TryParse(saved, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : fallback;
    }

    private async Task LoadSidebarWidthAsync()
    {
        var saved = await _settingsStore.GetAsync("SidebarWidth");
        if (saved is null || !double.TryParse(saved, NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
        {
            return;
        }

        _isApplyingSavedSidebarWidth = true;
        SidebarWidth = Math.Clamp(width, MinSidebarWidth, MaxSidebarWidth);
        _isApplyingSavedSidebarWidth = false;
    }

    partial void OnSidebarWidthChanged(double value)
    {
        if (_isApplyingSavedSidebarWidth)
        {
            return;
        }

        _ = _settingsStore.SetAsync("SidebarWidth", value.ToString(CultureInfo.InvariantCulture));
    }

    private async Task LoadVolumeAsync()
    {
        var saved = await _settingsStore.GetAsync("Volume");
        if (saved is null || !int.TryParse(saved, NumberStyles.Integer, CultureInfo.InvariantCulture, out var volume))
        {
            return;
        }

        _isApplyingSavedVolume = true;
        Volume = Math.Clamp(volume, 0, 100);
        _isApplyingSavedVolume = false;
    }

    partial void OnVolumeChanged(int value)
    {
        Engine.Volume = value;

        if (_isApplyingSavedVolume)
        {
            return;
        }

        _ = _settingsStore.SetAsync("Volume", value.ToString(CultureInfo.InvariantCulture));
    }

    partial void OnIsMutedChanged(bool value) => Engine.IsMuted = value;

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    partial void OnSelectedAudioTrackChanged(TrackOption? value)
    {
        if (_isSyncingTrackSelection || value is null)
        {
            return;
        }

        Engine.SelectAudioTrack(value.Id);
    }

    partial void OnSelectedSubtitleTrackChanged(TrackOption? value)
    {
        if (_isSyncingTrackSelection || value is null)
        {
            return;
        }

        Engine.SelectSubtitleTrack(value.Id);
    }

    public void PlayChannel(Channel channel)
    {
        FinalizeCurrentProgress();
        NowPlayingChannelName = channel.Name;
        PlayUrl(channel.StreamUrl);
    }

    // Catch-up playback of a past programme - not resumable/tracked like movies/episodes (see
    // WatchProgress), it's still conceptually "live" viewing of a channel, just shifted in time.
    public void PlayTimeshift(Channel channel, string timeshiftUrl, string programmeTitle)
    {
        FinalizeCurrentProgress();
        NowPlayingChannelName = $"{channel.Name} - {programmeTitle} (Catch-up)";
        PlayUrl(timeshiftUrl);
    }

    public void PlayEpisode(Episode episode, Series series) => _ = PlayEpisodeAsync(episode, series);

    private async Task PlayEpisodeAsync(Episode episode, Series series)
    {
        // See DownloadPlaybackResolver - a downloaded copy's StreamUrl is preferred when one exists,
        // but resume tracking always stays keyed to the ORIGINAL series/episode, not the downloaded
        // copy, so watch progress isn't fragmented by whether a local copy happens to exist.
        var streamUrl = await _downloadPlaybackResolver.TryResolveEpisodeStreamUrlAsync(series.Name, episode.Season, episode.EpisodeNumber)
            ?? episode.StreamUrl;

        await PlayWithResumeAsync(
            WatchProgressContentType.Episode,
            series.ProfileId,
            ContentKeys.ForEpisode(series.SourceSeriesId, episode.SourceEpisodeId),
            $"{series.Name} - S{episode.Season:00}E{episode.EpisodeNumber:00} - {episode.Title}",
            series.CoverUrl,
            streamUrl);
    }

    public void PlayMovie(Movie movie) => _ = PlayMovieAsync(movie);

    private async Task PlayMovieAsync(Movie movie)
    {
        var streamUrl = await _downloadPlaybackResolver.TryResolveMovieStreamUrlAsync(movie.Name) ?? movie.StreamUrl;
        await PlayWithResumeAsync(WatchProgressContentType.Movie, movie.ProfileId, movie.SourceMovieId, movie.Name, movie.CoverUrl, streamUrl);
    }

    // Clips reuse the Movie model as their payload shape (see IClipSource) - SourceMovieId here is
    // really a clip's SourceClipId, just carried on the same property.
    public void PlayClip(Movie clip) => _ = PlayClipAsync(clip);

    private async Task PlayClipAsync(Movie clip)
    {
        var streamUrl = await _downloadPlaybackResolver.TryResolveClipStreamUrlAsync(clip.Name) ?? clip.StreamUrl;
        await PlayWithResumeAsync(WatchProgressContentType.Clip, clip.ProfileId, clip.SourceMovieId, clip.Name, clip.CoverUrl, streamUrl);
    }

    // Used by the "Continue Watching" list, which already has everything it needs from the saved
    // WatchProgress row - no need to look the original Movie/Episode/Series back up first.
    public void ResumeFromProgress(WatchProgress progress) =>
        BeginTrackedPlayback(progress.ContentType, progress.ProfileId, progress.ContentKey, progress.Title, progress.CoverUrl, progress.StreamUrl, progress.PositionSeconds);

    private async Task PlayWithResumeAsync(WatchProgressContentType contentType, Guid profileId, string contentKey, string title, string? coverUrl, string streamUrl)
    {
        double resumeFromSeconds = 0;
        try
        {
            var existing = await _watchProgressRepository.GetAsync(profileId, contentType, contentKey);
            resumeFromSeconds = existing?.PositionSeconds ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to look up watch progress for {ContentKey}", contentKey);
        }

        BeginTrackedPlayback(contentType, profileId, contentKey, title, coverUrl, streamUrl, resumeFromSeconds);
    }

    private void BeginTrackedPlayback(WatchProgressContentType contentType, Guid profileId, string contentKey, string title, string? coverUrl, string streamUrl, double resumeFromSeconds)
    {
        FinalizeCurrentProgress();

        _currentContentType = contentType;
        _currentProfileId = profileId;
        _currentContentKey = contentKey;
        _currentTitle = title;
        _currentCoverUrl = coverUrl;
        // Not worth seeking for a few seconds of previously-watched intro/credits.
        _pendingResumeMs = resumeFromSeconds > 5 ? (long)(resumeFromSeconds * 1000) : null;

        NowPlayingChannelName = title;
        PlayUrl(streamUrl);
        StartProgressSaveTimer();
    }

    [RelayCommand]
    private void Stop()
    {
        FinalizeCurrentProgress();
        CancelLoadTimeout();
        CancelStallWatchdog();
        Engine.Stop();
        StatusText = "Idle";
        IsPlaying = false;
        IsPaused = false;
        IsBuffering = false;
        HasPlaybackError = false;
        NowPlayingChannelName = null;
        ResetSeekState();
    }

    // Re-issues the exact same URL that just failed/froze. _currentUrl and (for tracked content)
    // _pendingResumeMs are left untouched by the error/stall handlers specifically so this can resume
    // from precisely where playback stopped rather than re-running BeginTrackedPlayback's DB lookup,
    // which can be up to ProgressSaveInterval stale.
    [RelayCommand(CanExecute = nameof(HasPlaybackError))]
    private void Retry()
    {
        if (_currentUrl is null)
        {
            return;
        }

        PlayUrl(_currentUrl);
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void TogglePause()
    {
        if (IsPaused && _pausedAtUtc is { } pausedAt && DateTime.UtcNow - pausedAt >= _pauseReconnectThreshold && _currentUrl is not null)
        {
            // IPTV/Xtream-style backends commonly drop a connection that's gone idle for a while,
            // which is exactly what pausing does - a plain resume after a long enough pause would
            // just resume against a dead connection and sit there until the stall watchdog
            // eventually caught it, forcing a manual Retry. Reconnect proactively instead, from the
            // exact position we paused at, so a long pause resumes seamlessly instead of guaranteed
            // stall-then-retry.
            _pendingResumeMs = Engine.PositionMs > 0 ? Engine.PositionMs : _lastKnownTimeMs;
            PlayUrl(_currentUrl);
            return;
        }

        Engine.SetPaused(!IsPaused);
    }

    [RelayCommand(CanExecute = nameof(IsSeekable))]
    private void SkipForward() => SeekBy(_skipInterval);

    [RelayCommand(CanExecute = nameof(IsSeekable))]
    private void SkipBackward() => SeekBy(-_skipInterval);

    private void SeekBy(TimeSpan delta)
    {
        var maxMs = DurationSeconds > 0 ? (long)(DurationSeconds * 1000) : long.MaxValue;
        Engine.PositionMs = Math.Clamp(Engine.PositionMs + (long)delta.TotalMilliseconds, 0, maxMs);
    }

    // Called from the seek Slider's PointerPressed/PointerReleased in MainWindow's code-behind - while
    // the user is actively dragging, incoming TimeChanged events must not overwrite the slider's value
    // out from under their cursor. The actual seek only happens once they let go.
    public void BeginUserSeek() => _isUserSeeking = true;

    public void EndUserSeek()
    {
        _isUserSeeking = false;
        if (IsSeekable)
        {
            Engine.PositionMs = (long)(SeekSliderValue * 1000);
        }
    }

    [RelayCommand]
    private void ToggleFullscreen() => IsFullscreen = !IsFullscreen;

    public void ExitFullscreen() => IsFullscreen = false;

    private void PlayUrl(string url)
    {
        CancelLoadTimeout();
        CancelStallWatchdog();

        _currentUrl = url;

        StatusText = "Loading...";
        IsPaused = false;
        HasPlaybackError = false;
        _hasShownFrame = false;
        _audioPreferenceApplied = false;
        _subtitlePreferenceApplied = false;
        IsBuffering = true;
        ResetSeekState();
        Engine.Play(url);

        // Avoid hammering a dead server: time out once, don't auto-retry.
        _loadTimeoutTimer = new DispatcherTimer { Interval = _loadTimeout };
        _loadTimeoutTimer.Tick += (_, _) =>
        {
            CancelLoadTimeout();
            if (!IsPlaying)
            {
                StatusText = "Channel unavailable (timed out)";
                IsBuffering = false;
                HasPlaybackError = true;
                _logger.LogWarning("Stream load timed out: {Url}", url);
                Engine.Stop();
            }
        };
        _loadTimeoutTimer.Start();
    }

    private void ResetSeekState()
    {
        SeekSliderValue = 0;
        DurationSeconds = 0;
        CanPause = false;
        IsSeekable = false;

        _isSyncingTrackSelection = true;
        AudioTracks.Clear();
        SubtitleTracks.Clear();
        SelectedAudioTrack = null;
        SelectedSubtitleTrack = null;
        _isSyncingTrackSelection = false;
        OnPropertyChanged(nameof(HasMultipleAudioTracks));
        OnPropertyChanged(nameof(HasSubtitleTracks));
    }

    private void CancelLoadTimeout()
    {
        _loadTimeoutTimer?.Stop();
        _loadTimeoutTimer = null;
    }

    // Started once a frame has actually shown (OnPlaying) - the load timeout above only covers the
    // initial connect. Ticks periodically and compares against RenderedFrameCount rather than
    // Engine.PositionMs/TimeChanged - confirmed via live testing that some streams keep their demux
    // clock free-running (position keeps climbing smoothly at real-time rate) for minutes after the
    // connection has actually died and the picture is genuinely frozen, so position alone is not a
    // trustworthy "is a new frame actually being rendered" signal here. RenderedFrameCount only
    // increments when a frame has actually been decoded and handed to the video output.
    private void StartStallWatchdog()
    {
        CancelStallWatchdog();
        _lastDisplayedPictures = Engine.RenderedFrameCount;
        _lastDisplayedPicturesChangeUtc = DateTime.UtcNow;
        // Never check less often than the threshold itself allows, so a short user-configured
        // threshold (e.g. 2s) isn't silently floored to this timer's usual 5s tick rate.
        var tickInterval = _stallThreshold < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(5);
        _stallWatchdogTimer = new DispatcherTimer { Interval = tickInterval };
        _stallWatchdogTimer.Tick += (_, _) =>
        {
            var displayed = Engine.RenderedFrameCount;
            if (displayed > _lastDisplayedPictures)
            {
                _lastDisplayedPictures = displayed;
                _lastDisplayedPicturesChangeUtc = DateTime.UtcNow;
                return;
            }

            if (IsPlaying && DateTime.UtcNow - _lastDisplayedPicturesChangeUtc >= _stallThreshold)
            {
                _logger.LogWarning("Playback stalled (no new frame displayed for {Seconds}s): {Url}", _stallThreshold.TotalSeconds, _currentUrl);
                HandlePlaybackStalled();
            }
        };
        _stallWatchdogTimer.Start();
    }

    private void CancelStallWatchdog()
    {
        _stallWatchdogTimer?.Stop();
        _stallWatchdogTimer = null;
    }

    private void HandlePlaybackStalled()
    {
        CancelStallWatchdog();
        CancelLoadTimeout();
        CaptureExactResumePoint();

        IsPlaying = false;
        IsPaused = false;
        IsBuffering = false;
        HasPlaybackError = true;
        StatusText = "Playback stalled";
        Engine.Stop();
    }

    // Only meaningful for tracked (seekable VOD) content - re-seeking a fresh connection to a live
    // channel's stale absolute time doesn't make sense, so live playback is left to just reconnect at
    // the live point instead (_pendingResumeMs stays null, same as it already is for live playback
    // once BeginTrackedPlayback's one-time resume value has been consumed).
    private void CaptureExactResumePoint()
    {
        if (_currentContentType is null)
        {
            return;
        }

        var lastKnownMs = Engine.PositionMs > 0 ? Engine.PositionMs : _lastKnownTimeMs;
        SaveProgressTick();
        _pendingResumeMs = lastKnownMs > 0 ? lastKnownMs : null;
        WeakReferenceMessenger.Default.Send(new WatchProgressUpdatedMessage());
    }

    private void StartProgressSaveTimer()
    {
        StopProgressSaveTimer();
        _progressSaveTimer = new DispatcherTimer { Interval = ProgressSaveInterval };
        _progressSaveTimer.Tick += (_, _) => SaveProgressTick();
        _progressSaveTimer.Start();
    }

    private void StopProgressSaveTimer()
    {
        _progressSaveTimer?.Stop();
        _progressSaveTimer = null;
    }

    private void SaveProgressTick()
    {
        if (_currentContentType is not { } contentType || _currentProfileId is not { } profileId || _currentContentKey is null)
        {
            return;
        }

        _ = SaveOrRemoveProgressAsync(contentType, profileId, _currentContentKey, _currentTitle!, _currentCoverUrl, _currentUrl!, Engine.PositionMs / 1000.0, DurationSeconds);
    }

    // Called whenever tracked playback is about to switch or stop, so the position reached so far is
    // never lost - one last save/remove, then the timer and tracked-content state are cleared.
    private void FinalizeCurrentProgress()
    {
        SaveProgressTick();
        ClearTrackedContent();
    }

    // Called on EndReached specifically - we know for certain the title was watched to completion,
    // so this always removes the bookmark rather than relying on SaveProgressTick's percentage
    // heuristic (position can lag slightly behind the real end position at this point).
    private void MarkCurrentContentFinished()
    {
        if (_currentContentType is { } contentType && _currentProfileId is { } profileId && _currentContentKey is not null)
        {
            _ = _watchProgressRepository.RemoveAsync(profileId, contentType, _currentContentKey);
            _ = MarkWatchedAndNotifyAsync(profileId, contentType, _currentContentKey);
        }

        ClearTrackedContent();
    }

    // Whatever page is currently showing this movie/episode (if any) only found out its content was
    // watched via a bound VM instance that the toggle command mutated directly - this auto-mark path
    // has no such reference, only a content key, so it has to tell the rest of the app to go re-check
    // for itself instead.
    private async Task MarkWatchedAndNotifyAsync(Guid profileId, WatchProgressContentType contentType, string contentKey)
    {
        await _watchedItemRepository.MarkWatchedAsync(profileId, contentType, contentKey);
        WeakReferenceMessenger.Default.Send(new WatchedStatusUpdatedMessage());
    }

    private void ClearTrackedContent()
    {
        StopProgressSaveTimer();

        // Only worth telling Favorites' "Continue Watching" list to refresh if there was actually
        // tracked content to finalize - avoids a spurious broadcast on every plain channel switch.
        // A dedicated message (rather than FavoritesUpdatedMessage) lets Favorites re-fetch just that
        // section instead of doing a full-page reload on every single playback stop/switch.
        if (_currentContentType is not null)
        {
            WeakReferenceMessenger.Default.Send(new WatchProgressUpdatedMessage());
        }

        _currentContentType = null;
        _currentProfileId = null;
        _currentContentKey = null;
        _currentTitle = null;
        _currentCoverUrl = null;
    }

    private async Task SaveOrRemoveProgressAsync(
        WatchProgressContentType contentType, Guid profileId, string contentKey, string title, string? coverUrl, string streamUrl,
        double positionSeconds, double durationSeconds)
    {
        try
        {
            var isFinished = durationSeconds > 0 && positionSeconds >= durationSeconds * CompletionThreshold;
            if (positionSeconds < 5 || isFinished)
            {
                await _watchProgressRepository.RemoveAsync(profileId, contentType, contentKey);
                if (isFinished)
                {
                    await _watchedItemRepository.MarkWatchedAsync(profileId, contentType, contentKey);
                    WeakReferenceMessenger.Default.Send(new WatchedStatusUpdatedMessage());
                }
                return;
            }

            await _watchProgressRepository.SaveAsync(new WatchProgress
            {
                ProfileId = profileId,
                ContentType = contentType,
                ContentKey = contentKey,
                Title = title,
                CoverUrl = coverUrl,
                StreamUrl = streamUrl,
                PositionSeconds = positionSeconds,
                DurationSeconds = durationSeconds,
                UpdatedUtc = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save watch progress for {ContentKey}", contentKey);
        }
    }

    private static string FormatTime(double totalSeconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"mm\:ss");
    }

    // The engine raises these on background threads - always marshal back to the UI thread.
    private void OnPlaying(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            CancelLoadTimeout();
            StartStallWatchdog();
            _pausedAtUtc = null;
            IsPlaying = true;
            IsPaused = false;
            StatusText = "Playing";

            _hasShownFrame = true;
            IsBuffering = false;
            HasPlaybackError = false;
            OnPropertyChanged(nameof(ShowLoadingOverlay));
            OnPropertyChanged(nameof(ShowBufferingBadge));

            if (_pendingResumeMs is { } resumeMs)
            {
                Engine.PositionMs = resumeMs;
            }
            _pendingResumeMs = null;
        });

    private void OnPaused(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
            IsPaused = true;
            _pausedAtUtc = DateTime.UtcNow;
            StatusText = "Paused";
        });

    private void OnBuffering(object? sender, double cache) =>
        Dispatcher.UIThread.Post(() =>
        {
            StatusText = $"Buffering ({cache:0}%)";
            IsBuffering = cache < 100;
        });

    private void OnEncounteredError(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            CancelLoadTimeout();
            CancelStallWatchdog();
            CaptureExactResumePoint();
            IsPlaying = false;
            IsPaused = false;
            IsBuffering = false;
            HasPlaybackError = true;
            StatusText = "Error playing channel";
            _logger.LogError("MediaPlayer encountered an error playing {Url}", _currentUrl);
        });

    private void OnEndReached(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            // Confirmed via live testing: when the connection drops mid-stream, this particular
            // demuxer/protocol combination reports it as a clean EndReached, not EncounteredError and
            // not a stall (position/RenderedFrameCount both just stop, same as a genuine end) - so a
            // dropped connection 90 seconds into a 40-minute episode looked identical to "the user
            // finished watching it". If we're stopping well short of the known duration, treat this
            // the same as any other interrupted playback (error state + exact-position Retry) instead
            // of silently marking the title watched and discarding the resume point.
            if (DurationSeconds > 0 && Engine.PositionMs < DurationSeconds * 1000 * CompletionThreshold)
            {
                _logger.LogWarning(
                    "EndReached fired well short of the known duration ({Time}ms of {Duration}ms) - treating as an interrupted stream, not a finished one: {Url}",
                    Engine.PositionMs, DurationSeconds * 1000, _currentUrl);
                HandlePlaybackStalled();
                return;
            }

            CancelStallWatchdog();
            MarkCurrentContentFinished();
            IsPlaying = false;
            IsPaused = false;
            IsBuffering = false;
            StatusText = "Ended";
        });

    private void OnStopped(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
            IsPaused = false;
        });

    private void OnTimeChanged(object? sender, long time) =>
        Dispatcher.UIThread.Post(() =>
        {
            CanPause = Engine.CanPause;
            IsSeekable = Engine.IsSeekable;

            // Only reset the stall watchdog's clock on a genuine position advance - the engine can
            // keep firing TimeChanged with the *same* stale value on some sort of internal heartbeat
            // while actually stalled, which would otherwise keep resetting the watchdog forever and
            // mask exactly the freeze it exists to catch.
            if (time != _lastKnownTimeMs)
            {
                _lastTimeChangeUtc = DateTime.UtcNow;
            }
            _lastKnownTimeMs = time;

            if (!_isUserSeeking)
            {
                SeekSliderValue = time / 1000.0;
            }
        });

    private void OnLengthChanged(object? sender, long length) =>
        Dispatcher.UIThread.Post(() => DurationSeconds = length > 0 ? length / 1000.0 : 0);

    private void OnTracksChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshTrackLists);

    // Rereads the full track lists from the engine rather than incrementally applying whatever
    // add/delete triggered this - the engine's lists are already the source of truth and cheap to
    // reread, so there's no separate "list" state to keep in sync by hand.
    private void RefreshTrackLists()
    {
        _isSyncingTrackSelection = true;
        try
        {
            AudioTracks.Clear();
            foreach (var track in Engine.AudioTracks)
            {
                AudioTracks.Add(track);
            }
            TrackOption? preferredAudio = null;
            if (!_audioPreferenceApplied && AudioTracks.Count > 0)
            {
                preferredAudio = FindPreferredTrack(AudioTracks, _preferredAudioLanguage);
                _audioPreferenceApplied = true;
            }
            SelectedAudioTrack = preferredAudio
                ?? AudioTracks.FirstOrDefault(t => t.Id == Engine.SelectedAudioTrackId)
                ?? AudioTracks.FirstOrDefault();

            SubtitleTracks.Clear();
            foreach (var track in Engine.SubtitleTracks)
            {
                SubtitleTracks.Add(track);
            }
            TrackOption? preferredSubtitle = null;
            if (!_subtitlePreferenceApplied && SubtitleTracks.Count > 0)
            {
                // No preferred language configured means subtitles off by default - "Disable" is the
                // engine's own standard entry for that, present whenever a stream has any subtitle
                // tracks at all, so this is deterministic rather than depending on whatever this
                // particular stream/container happened to mark as its own default track.
                preferredSubtitle = FindPreferredTrack(SubtitleTracks, _preferredSubtitleLanguage)
                    ?? SubtitleTracks.FirstOrDefault(t => t.Name.Equals("Disable", StringComparison.OrdinalIgnoreCase));
                _subtitlePreferenceApplied = true;
            }
            SelectedSubtitleTrack = preferredSubtitle
                ?? SubtitleTracks.FirstOrDefault(t => t.Id == Engine.SelectedSubtitleTrackId)
                ?? SubtitleTracks.FirstOrDefault();
        }
        finally
        {
            _isSyncingTrackSelection = false;
        }

        OnPropertyChanged(nameof(HasMultipleAudioTracks));
        OnPropertyChanged(nameof(HasSubtitleTracks));
    }

    // Substring match against the track name the engine reports (e.g. "English", "eng", "English
    // (undetermined)") - there's no standardized language code exposed here, so this is deliberately
    // a loose, best-effort match rather than an exact one.
    private static TrackOption? FindPreferredTrack(IEnumerable<TrackOption> tracks, string? preferredLanguage) =>
        string.IsNullOrWhiteSpace(preferredLanguage)
            ? null
            : tracks.FirstOrDefault(t => t.Name.Contains(preferredLanguage, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        WeakReferenceMessenger.Default.Unregister<PlaybackTimingSettingsChangedMessage>(this);
        WeakReferenceMessenger.Default.Unregister<PlaybackTrackPreferencesChangedMessage>(this);
        FinalizeCurrentProgress();
        CancelLoadTimeout();
        CancelStallWatchdog();
        Engine.Playing -= OnPlaying;
        Engine.Paused -= OnPaused;
        Engine.Buffering -= OnBuffering;
        Engine.EncounteredError -= OnEncounteredError;
        Engine.EndReached -= OnEndReached;
        Engine.Stopped -= OnStopped;
        Engine.TimeChanged -= OnTimeChanged;
        Engine.LengthChanged -= OnLengthChanged;
        Engine.TracksChanged -= OnTracksChanged;
    }
}
