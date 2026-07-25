using Iptv.Core.Models;

namespace Iptv.Core.Services;

public interface IWatchedItemRepository
{
    Task<IReadOnlyList<WatchedItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task MarkWatchedAsync(Guid profileId, WatchProgressContentType contentType, string contentKey, CancellationToken cancellationToken = default);
    Task MarkUnwatchedAsync(Guid profileId, WatchProgressContentType contentType, string contentKey, CancellationToken cancellationToken = default);
}
