using Iptv.Core.Models;

namespace Iptv.Core.Services;

public sealed record AccountInfo(string? Status, DateTime? ExpiryUtc, int? MaxConnections);

public interface IAccountInfoSource
{
    ProfileSourceType SourceType { get; }
    Task<AccountInfo> GetAccountInfoAsync(ProfileSource profile, CancellationToken cancellationToken = default);
}
