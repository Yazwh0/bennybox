namespace Iptv.Core.Services;

public interface IFavoriteRepository
{
    Task<IReadOnlySet<Guid>> GetFavoriteChannelIdsAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Guid profileId, Guid channelId, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid channelId, CancellationToken cancellationToken = default);
}
