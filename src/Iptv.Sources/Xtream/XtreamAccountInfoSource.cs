using System.Runtime.Versioning;
using Iptv.Core.Models;
using Iptv.Core.Services;

namespace Iptv.Sources.Xtream;

[SupportedOSPlatform("windows")]
public class XtreamAccountInfoSource : IAccountInfoSource
{
    private readonly XtreamClient _client;

    public XtreamAccountInfoSource(XtreamClient client)
    {
        _client = client;
    }

    public ProfileSourceType SourceType => ProfileSourceType.XtreamCodes;

    public async Task<AccountInfo> GetAccountInfoAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profile.XtreamServerUrl) || string.IsNullOrWhiteSpace(profile.XtreamUsername))
        {
            throw new InvalidOperationException("Profile has no Xtream Codes server/username configured.");
        }

        var password = CredentialProtector.Unprotect(profile.XtreamPasswordEncrypted)
            ?? throw new InvalidOperationException("Profile has no Xtream Codes password configured.");

        var result = await _client.AuthenticateAsync(profile.XtreamServerUrl.TrimEnd('/'), profile.XtreamUsername, password, cancellationToken);

        DateTime? expiryUtc = long.TryParse(result.UserInfo.ExpDate, out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
        int? maxConnections = int.TryParse(result.UserInfo.MaxConnections, out var max) ? max : null;

        return new AccountInfo(result.UserInfo.Status, expiryUtc, maxConnections);
    }
}
