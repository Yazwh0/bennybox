using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    private readonly LibVLC _libVlc;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<PlayerViewModel> _logger;
    private Media? _currentMedia;
    private DispatcherTimer? _loadTimeoutTimer;
    private string? _currentUrl;
    private bool _isApplyingSavedSidebarWidth;
    private bool _isUserSeeking;
    private bool _isSyncingTrackSelection;

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

    public PlayerViewModel(LibVLC libVlc, ISettingsStore settingsStore, ILogger<PlayerViewModel> logger)
    {
        _libVlc = libVlc;
        _settingsStore = settingsStore;
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
        NowPlayingChannelName = channel.Name;
        PlayUrl(channel.StreamUrl);
    }

    public void PlayEpisode(Episode episode)
    {
        NowPlayingChannelName = episode.Title;
        PlayUrl(episode.StreamUrl);
    }

    public void PlayMovie(Movie movie)
    {
        NowPlayingChannelName = movie.Name;
        PlayUrl(movie.StreamUrl);
    }

    [RelayCommand]
    private void Stop()
    {
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
            CancelLoadTimeout();
            IsPlaying = false;
            IsPaused = false;
            StatusText = "Error playing channel";
            _logger.LogError("MediaPlayer encountered an error playing {Url}", _currentUrl);
        });

    private void OnEndReached(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
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
