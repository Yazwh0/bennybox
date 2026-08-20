using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.ViewModels;

// Shared by MoviesViewModel/ClipsViewModel/SeriesViewModel/SearchViewModel - computes a row's
// DownloadUiState the same way everywhere: Completed if the caller already knows a matching downloaded
// copy exists (see DownloadPlaybackResolver - same normalized-title matching for Movies/Clips, +
// season/episode number for episodes - each caller determines this itself since the check differs by
// content kind), else Queued/Downloading if an active Download row exists for this exact original
// item, else NotDownloaded.
public static class DownloadRowSupport
{
    public static (DownloadUiState State, double Progress) ComputeState(
        IReadOnlyCollection<Download> activeDownloads, bool isAlreadyDownloaded,
        Guid profileId, WatchProgressContentType contentType, string sourceId)
    {
        if (isAlreadyDownloaded)
        {
            return (DownloadUiState.Completed, 1);
        }

        var active = activeDownloads.FirstOrDefault(d =>
            d.OriginalProfileId == profileId && d.ContentType == contentType && d.OriginalSourceId == sourceId &&
            d.Status is DownloadStatus.Queued or DownloadStatus.Downloading);

        if (active is null)
        {
            return (DownloadUiState.NotDownloaded, 0);
        }

        var progress = active.TotalBytes is { } total && total > 0 ? (double)active.BytesDownloaded / total : 0;
        return (active.Status == DownloadStatus.Queued ? DownloadUiState.Queued : DownloadUiState.Downloading, progress);
    }

    public static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
