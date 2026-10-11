using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

public interface IProfileRepository
{
    Task<IReadOnlyList<ProfileSource>> GetAllAsync(CancellationToken cancellationToken = default);

    // What every browsing page (and its bulk refresh) should read - GetAllAsync is for managing profiles
    // themselves (Settings) and for resolving one a stored item already points at.
    Task<IReadOnlyList<ProfileSource>> GetEnabledAsync(CancellationToken cancellationToken = default);
    Task<ProfileSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(ProfileSource profile, CancellationToken cancellationToken = default);
    Task UpdateAsync(ProfileSource profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
