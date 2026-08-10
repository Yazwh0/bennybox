using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

public interface IDownloadRepository
{
    Task<Guid> CreateAsync(Download download, CancellationToken cancellationToken = default);
    Task UpdateProgressAsync(Guid id, long bytesDownloaded, long? totalBytes, CancellationToken cancellationToken = default);
    Task MarkCompletedAsync(Guid id, string localRelativePath, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(Guid id, string errorMessage, CancellationToken cancellationToken = default);
    Task MarkCanceledAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Download>> GetAllAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Persisted as soon as RunDownloadAsync computes the intended destination - not just on
    // completion - so a Failed/interrupted download's partial file can still be found (for both
    // Delete and resume-on-retry), not only a Completed one's.
    Task SetDestinationPathAsync(Guid id, string localRelativePath, CancellationToken cancellationToken = default);

    // Resets a Failed/Canceled row back to Queued for a resumed retry - reused in place rather than
    // inserting a new row, so the Downloads panel never shows two entries for the same title just
    // because one attempt failed. See DownloadManager.QueueAsync/RetryDownloadAsync.
    Task RequeueAsync(Guid id, CancellationToken cancellationToken = default);

    // Called once at app startup - any row still Downloading is from a session that didn't shut down
    // cleanly (the in-memory copy loop that would finish or fail it is gone). See DownloadManager.
    Task MarkInterruptedDownloadsAsFailedAsync(CancellationToken cancellationToken = default);
}
