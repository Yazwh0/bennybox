using Iptv.Core.Models;

namespace Iptv.Core.Services;

public class AccountInfoService
{
    private readonly IEnumerable<IAccountInfoSource> _sources;

    public AccountInfoService(IEnumerable<IAccountInfoSource> sources)
    {
        _sources = sources;
    }

    // Not every profile source type has account info to report (e.g. M3U) - null is a normal,
    // expected result for those, not an error.
    public async Task<AccountInfo?> GetAccountInfoAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.SourceType == profile.SourceType);
        return source is null ? null : await source.GetAccountInfoAsync(profile, cancellationToken);
    }
}
