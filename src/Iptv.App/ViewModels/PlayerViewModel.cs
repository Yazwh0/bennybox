using System;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iptv.Core.Models;
using Iptv.Core.Services;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;

namespace Iptv.App.ViewModels;

public partial class PlayerViewModel : ViewModelBase, IDisposable
{
    // Shared by Live TV/Guide/Favorites so a resize on one page is reflected on the others too, and
    // so the fullscreen-collapse converter always has the last user-chosen width to restore to.
    public const double MinSidebarWidth = 180;
    public const double MaxSidebarWidth = 560;

    private readonly LibVLC _libVlc;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<PlayerViewModel> _logger;
    private Media? _currentMedia;
    private DispatcherTimer? _loadTimeoutTimer;
    private string? _currentUrl;
    private bool _isApplyingSavedSidebarWidth;

    public MediaPlayer MediaPlayer { get; }

    [ObservableProperty]
    private string _statusText = "Idle";

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private string? _nowPlayingChannelName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullscreenButtonText))]
    private bool _isFullscreen;

    [ObservableProperty]
    private double _sidebarWidth = 280;

    public string FullscreenButtonText => IsFullscreen ? "Exit Fullscreen" : "Fullscreen";

    public PlayerViewModel(LibVLC libVlc, ISettingsStore settingsStore, ILogger<PlayerViewModel> logger)
    {
        _libVlc = libVlc;
        _settingsStore = settingsStore;
        _logger = logger;
        MediaPlayer = new MediaPlayer(_libVlc);

        MediaPlayer.Playing += OnPlaying;
        MediaPlayer.Buffering += OnBuffering;
        MediaPlayer.EncounteredError += OnEncounteredError;
        MediaPlayer.EndReached += OnEndReached;
        MediaPlayer.Stopped += OnStopped;

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

    [RelayCommand]
    private void Stop()
    {
        CancelLoadTimeout();
        MediaPlayer.Stop();
        StatusText = "Idle";
        IsPlaying = false;
        NowPlayingChannelName = null;
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

    private void CancelLoadTimeout()
    {
        _loadTimeoutTimer?.Stop();
        _loadTimeoutTimer = null;
    }

    // LibVLCSharp raises these on background threads - always marshal back to the UI thread.
    private void OnPlaying(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            CancelLoadTimeout();
            IsPlaying = true;
            StatusText = "Playing";
        });

    private void OnBuffering(object? sender, MediaPlayerBufferingEventArgs e) =>
        Dispatcher.UIThread.Post(() => StatusText = $"Buffering ({e.Cache:0}%)");

    private void OnEncounteredError(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            CancelLoadTimeout();
            IsPlaying = false;
            StatusText = "Error playing channel";
            _logger.LogError("MediaPlayer encountered an error playing {Url}", _currentUrl);
        });

    private void OnEndReached(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
            StatusText = "Ended";
        });

    private void OnStopped(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => IsPlaying = false);

    public void Dispose()
    {
        CancelLoadTimeout();
        MediaPlayer.Playing -= OnPlaying;
        MediaPlayer.Buffering -= OnBuffering;
        MediaPlayer.EncounteredError -= OnEncounteredError;
        MediaPlayer.EndReached -= OnEndReached;
        MediaPlayer.Stopped -= OnStopped;
        _currentMedia?.Dispose();
        MediaPlayer.Dispose();
    }
}
