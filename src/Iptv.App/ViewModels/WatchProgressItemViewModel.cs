using Iptv.Core.Models;

namespace Iptv.App.ViewModels;

public sealed class WatchProgressItemViewModel
{
    public WatchProgress Progress { get; }
    public string Title => Progress.Title;
    public string? CoverUrl => Progress.CoverUrl;

    // Drives a small progress bar in the "Continue Watching" row so it's obvious at a glance how far
    // into the title you got, without needing to open the player.
    public double ProgressFraction => Progress.DurationSeconds > 0
        ? Math.Clamp(Progress.PositionSeconds / Progress.DurationSeconds, 0, 1)
        : 0;

    public WatchProgressItemViewModel(WatchProgress progress)
    {
        Progress = progress;
    }
}
