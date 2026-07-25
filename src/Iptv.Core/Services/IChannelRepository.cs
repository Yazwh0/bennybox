using Iptv.Core.Models;

namespace Iptv.Core.Services;

public interface IChannelRepository
{
    Task ReplaceChannelsAsync(Guid profileId, IReadOnlyList<Category> categories, IReadOnlyList<Channel> channels, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Category>> GetCategoriesAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Channel>> GetChannelsAsync(Guid profileId, CancellationToken cancellationToken = default);
}
