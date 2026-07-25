using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

public interface IWatchProgressRepository
{
    Task SaveAsync(WatchProgress progress, CancellationToken cancellationToken = default);
    Task<WatchProgress?> GetAsync(Guid profileId, WatchProgressContentType contentType, string contentKey, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid profileId, WatchProgressContentType contentType, string contentKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WatchProgress>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default);
}
