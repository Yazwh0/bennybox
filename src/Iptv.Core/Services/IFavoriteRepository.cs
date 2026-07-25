namespace Iptv.Core.Services;

public interface IFavoriteRepository
{
    Task<IReadOnlySet<Guid>> GetFavoriteChannelIdsAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Guid profileId, Guid channelId, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid channelId, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetFavoriteSeriesIdsAsync(CancellationToken cancellationToken = default);
    Task AddSeriesAsync(Guid profileId, Guid seriesId, CancellationToken cancellationToken = default);
    Task RemoveSeriesAsync(Guid seriesId, CancellationToken cancellationToken = default);
}
