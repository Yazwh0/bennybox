using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.ViewModels;

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

    // The same film, episode or clip, whichever instance (the rows are rebuilt as progress is saved).
    public bool IsSameAs(WatchProgressItemViewModel other) =>
        Progress.ProfileId == other.Progress.ProfileId && Progress.ContentType == other.Progress.ContentType
        && Progress.ContentKey == other.Progress.ContentKey;

    public WatchProgressItemViewModel(WatchProgress progress)
    {
        Progress = progress;
    }
}
