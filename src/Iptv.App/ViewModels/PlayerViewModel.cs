using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Iptv.App.Messages;
using Iptv.Core.Models;
using Iptv.Core.Services;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;

namespace Iptv.App.ViewModels;

// Id/Name pair for one audio or subtitle track, as reported by libVLC's TrackDescription - Id is
// what MediaPlayer.SetAudioTrack/SetSpu expect back, Name is whatever the stream/container labels it
// (language name, "Disable", etc).
public sealed record TrackOption(int Id, string Name);

public partial class PlayerViewModel : ViewModelBase, IDisposable
{
    // Shared by Live TV/Guide/Favorites so a resize on one page is reflected on the others too, and
    // so the fullscreen-collapse converter always has the last user-chosen width to restore to.
    public const double MinSidebarWidth = 180;
    public const double MaxSidebarWidth = 560;

    private static readonly TimeSpan SkipInterval = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ProgressSaveInterval = TimeSpan.FromSeconds(15);

    // A title is treated as "finished" (and its bookmark removed rather than saved) once playback
    // gets this close to the reported duration - avoids leaving a useless "resume at 99%" entry
    // behind for everything the user watches to the end.
    private const double CompletionThreshold = 0.95;

    private readonly LibVLC _libVlc;
    private readonly ISettingsStore _settingsStore;
    private readonly IWatchProgressRepository _watchProgressRepository;
    private readonly ILogger<PlayerViewModel> _logger;
    private Media? _currentMedia;
    private DispatcherTimer? _loadTimeoutTimer;
    private DispatcherTimer? _progressSaveTimer;
    private string? _currentUrl;
    private bool _isApplyingSavedSidebarWidth;
    private bool _isUserSeeking;
    private bool _isSyncingTrackSelection;

    // Set only while playing a movie/episode - live channels are never tracked. See PlayWithResumeAsync.
    private WatchProgressContentType? _currentContentType;
    private Guid? _currentProfileId;
    private string? _currentContentKey;
    private string? _currentTitle;
    private string? _currentCoverUrl;
    private long? _pendingResumeMs;

    public MediaPlayer MediaPlayer { get; }

    // Populated from libVLC's ESAdded/ESDeleted events as the demuxer discovers tracks in the
    // current stream - empty until then, so the UI only shows a selector once there's something to
    // choose between.
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

    // Live channels are usually not seekable/pausable, VOD episodes always are - rather than
    // hardcoding that assumption, these mirror libVLC's own per-stream capability flags (some IPTV
    // "live" streams do support timeshifting and report seekable too), so the seek bar and
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

    public PlayerViewModel(LibVLC libVlc, ISettingsStore settingsStore, IWatchProgressRepository watchProgressRepository, ILogger<PlayerViewModel> logger)
    {
        _libVlc = libVlc;
        _settingsStore = settingsStore;
        _watchProgressRepository = watchProgressRepository;
        _logger = logger;
        MediaPlayer = new MediaPlayer(_libVlc);

        MediaPlayer.Playing += OnPlaying;
        MediaPlayer.Paused += OnPaused;
        MediaPlayer.Buffering += OnBuffering;
        MediaPlayer.EncounteredError += OnEncounteredError;
        MediaPlayer.EndReached += OnEndReached;
        MediaPlayer.Stopped += OnStopped;
        MediaPlayer.TimeChanged += OnTimeChanged;
        MediaPlayer.LengthChanged += OnLengthChanged;
        MediaPlayer.ESAdded += OnEsAdded;
        MediaPlayer.ESDeleted += OnEsDeleted;

        _ = LoadSidebarWidthAsync();
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

    partial void OnSelectedAudioTrackChanged(TrackOption? value)
    {
        if (_isSyncingTrackSelection || value is null)
        {
            return;
        }

        MediaPlayer.SetAudioTrack(value.Id);
    }

    partial void OnSelectedSubtitleTrackChanged(TrackOption? value)
    {
        if (_isSyncingTrackSelection || value is null)
        {
            return;
        }

        MediaPlayer.SetSpu(value.Id);
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

    public void PlayEpisode(Episode episode, Series series) =>
        _ = PlayWithResumeAsync(
            WatchProgressContentType.Episode,
            series.ProfileId,
            BuildEpisodeContentKey(series.SourceSeriesId, episode.SourceEpisodeId),
            $"{series.Name} - S{episode.Season:00}E{episode.EpisodeNumber:00} - {episode.Title}",
            series.CoverUrl,
            episode.StreamUrl);

    public void PlayMovie(Movie movie) =>
        _ = PlayWithResumeAsync(WatchProgressContentType.Movie, movie.ProfileId, movie.SourceMovieId, movie.Name, movie.CoverUrl, movie.StreamUrl);

    // Used by the "Continue Watching" list, which already has everything it needs from the saved
    // WatchProgress row - no need to look the original Movie/Episode/Series back up first.
    public void ResumeFromProgress(WatchProgress progress) =>
        BeginTrackedPlayback(progress.ContentType, progress.ProfileId, progress.ContentKey, progress.Title, progress.CoverUrl, progress.StreamUrl, progress.PositionSeconds);

    private static string BuildEpisodeContentKey(string sourceSeriesId, string sourceEpisodeId) => $"{sourceSeriesId}:{sourceEpisodeId}";

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
        MediaPlayer.Stop();
        StatusText = "Idle";
        IsPlaying = false;
        IsPaused = false;
        NowPlayingChannelName = null;
        ResetSeekState();
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void TogglePause() => MediaPlayer.SetPause(!IsPaused);

    [RelayCommand(CanExecute = nameof(IsSeekable))]
    private void SkipForward() => SeekBy(SkipInterval);

    [RelayCommand(CanExecute = nameof(IsSeekable))]
    private void SkipBackward() => SeekBy(-SkipInterval);

    private void SeekBy(TimeSpan delta)
    {
        var maxMs = DurationSeconds > 0 ? (long)(DurationSeconds * 1000) : long.MaxValue;
        MediaPlayer.Time = Math.Clamp(MediaPlayer.Time + (long)delta.TotalMilliseconds, 0, maxMs);
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
            MediaPlayer.Time = (long)(SeekSliderValue * 1000);
        }
    }

    [RelayCommand]
    private void ToggleFullscreen() => IsFullscreen = !IsFullscreen;

    public void ExitFullscreen() => IsFullscreen = false;

    private void PlayUrl(string url)
    {
        CancelLoadTimeout();

        _currentUrl = url;
        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVlc, url, FromType.FromLocation);

        StatusText = "Loading...";
        IsPaused = false;
        ResetSeekState();
        MediaPlayer.Play(_currentMedia);

        // Avoid hammering a dead server: time out once, don't auto-retry.
        _loadTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _loadTimeoutTimer.Tick += (_, _) =>
        {
            CancelLoadTimeout();
            if (!IsPlaying)
            {
                StatusText = "Channel unavailable (timed out)";
                _logger.LogWarning("Stream load timed out: {Url}", url);
                MediaPlayer.Stop();
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

        _ = SaveOrRemoveProgressAsync(contentType, profileId, _currentContentKey, _currentTitle!, _currentCoverUrl, _currentUrl!, MediaPlayer.Time / 1000.0, DurationSeconds);
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
    // heuristic (MediaPlayer.Time can lag slightly behind the real end position at this point).
    private void MarkCurrentContentFinished()
    {
        if (_currentContentType is { } contentType && _currentProfileId is { } profileId && _currentContentKey is not null)
        {
            _ = _watchProgressRepository.RemoveAsync(profileId, contentType, _currentContentKey);
        }

        ClearTrackedContent();
    }

    private void ClearTrackedContent()
    {
        StopProgressSaveTimer();

        // Only worth telling Favorites' "Continue Watching" list to refresh if there was actually
        // tracked content to finalize - avoids a spurious broadcast on every plain channel switch.
        if (_currentContentType is not null)
        {
            WeakReferenceMessenger.Default.Send(new FavoritesUpdatedMessage());
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

    // LibVLCSharp raises these on background threads - always marshal back to the UI thread.
    private void OnPlaying(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            CancelLoadTimeout();
            IsPlaying = true;
            IsPaused = false;
            StatusText = "Playing";

            if (_pendingResumeMs is { } resumeMs)
            {
                MediaPlayer.Time = resumeMs;
            }
            _pendingResumeMs = null;
        });

    private void OnPaused(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
            IsPaused = true;
            StatusText = "Paused";
        });

    private void OnBuffering(object? sender, MediaPlayerBufferingEventArgs e) =>
        Dispatcher.UIThread.Post(() => StatusText = $"Buffering ({e.Cache:0}%)");

    private void OnEncounteredError(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            FinalizeCurrentProgress();
            CancelLoadTimeout();
            IsPlaying = false;
            IsPaused = false;
            StatusText = "Error playing channel";
            _logger.LogError("MediaPlayer encountered an error playing {Url}", _currentUrl);
        });

    private void OnEndReached(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            MarkCurrentContentFinished();
            IsPlaying = false;
            IsPaused = false;
            StatusText = "Ended";
        });

    private void OnStopped(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
            IsPaused = false;
        });

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            CanPause = MediaPlayer.CanPause;
            IsSeekable = MediaPlayer.IsSeekable;

            if (!_isUserSeeking)
            {
                SeekSliderValue = e.Time / 1000.0;
            }
        });

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e) =>
        Dispatcher.UIThread.Post(() => DurationSeconds = e.Length > 0 ? e.Length / 1000.0 : 0);

    private void OnEsAdded(object? sender, MediaPlayerESAddedEventArgs e)
    {
        if (e.Type is TrackType.Audio or TrackType.Text)
        {
            Dispatcher.UIThread.Post(RefreshTrackLists);
        }
    }

    private void OnEsDeleted(object? sender, MediaPlayerESDeletedEventArgs e)
    {
        if (e.Type is TrackType.Audio or TrackType.Text)
        {
            Dispatcher.UIThread.Post(RefreshTrackLists);
        }
    }

    // Rereads the full track lists from libVLC rather than incrementally applying the add/delete
    // event that triggered this - the description arrays are already the source of truth and cheap
    // to reread, so there's no separate "list" state to keep in sync by hand.
    private void RefreshTrackLists()
    {
        _isSyncingTrackSelection = true;
        try
        {
            AudioTracks.Clear();
            foreach (var track in MediaPlayer.AudioTrackDescription ?? [])
            {
                AudioTracks.Add(new TrackOption(track.Id, track.Name));
            }
            SelectedAudioTrack = AudioTracks.FirstOrDefault(t => t.Id == MediaPlayer.AudioTrack) ?? AudioTracks.FirstOrDefault();

            SubtitleTracks.Clear();
            foreach (var track in MediaPlayer.SpuDescription ?? [])
            {
                SubtitleTracks.Add(new TrackOption(track.Id, track.Name));
            }
            SelectedSubtitleTrack = SubtitleTracks.FirstOrDefault(t => t.Id == MediaPlayer.Spu) ?? SubtitleTracks.FirstOrDefault();
        }
        finally
        {
            _isSyncingTrackSelection = false;
        }

        OnPropertyChanged(nameof(HasMultipleAudioTracks));
        OnPropertyChanged(nameof(HasSubtitleTracks));
    }

    public void Dispose()
    {
        FinalizeCurrentProgress();
        CancelLoadTimeout();
        MediaPlayer.Playing -= OnPlaying;
        MediaPlayer.Paused -= OnPaused;
        MediaPlayer.Buffering -= OnBuffering;
        MediaPlayer.EncounteredError -= OnEncounteredError;
        MediaPlayer.EndReached -= OnEndReached;
        MediaPlayer.Stopped -= OnStopped;
        MediaPlayer.TimeChanged -= OnTimeChanged;
        MediaPlayer.LengthChanged -= OnLengthChanged;
        MediaPlayer.ESAdded -= OnEsAdded;
        MediaPlayer.ESDeleted -= OnEsDeleted;
        _currentMedia?.Dispose();
        MediaPlayer.Dispose();
    }
}
