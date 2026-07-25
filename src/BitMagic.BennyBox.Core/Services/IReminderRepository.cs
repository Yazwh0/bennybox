using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

public interface IReminderRepository
{
    Task<IReadOnlyList<Reminder>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Reminder>> GetDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
    Task AddAsync(Reminder reminder, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid profileId, string channelTvgId, DateTime startUtc, CancellationToken cancellationToken = default);
}
