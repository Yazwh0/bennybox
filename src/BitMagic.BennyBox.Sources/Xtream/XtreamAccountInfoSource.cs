using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Xtream;

public class XtreamAccountInfoSource : IAccountInfoSource
{
    private readonly XtreamClient _client;
    private readonly ICredentialProtector _credentialProtector;

    public XtreamAccountInfoSource(XtreamClient client, ICredentialProtector credentialProtector)
    {
        _client = client;
        _credentialProtector = credentialProtector;
    }

    public ProfileSourceType SourceType => ProfileSourceType.XtreamCodes;

    public async Task<AccountInfo> GetAccountInfoAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profile.XtreamServerUrl) || string.IsNullOrWhiteSpace(profile.XtreamUsername))
        {
            throw new InvalidOperationException("Profile has no Xtream Codes server/username configured.");
        }

        var password = _credentialProtector.Unprotect(profile.XtreamPasswordEncrypted)
            ?? throw new InvalidOperationException("Profile has no Xtream Codes password configured.");

        var result = await _client.AuthenticateAsync(profile.XtreamServerUrl.TrimEnd('/'), profile.XtreamUsername, password, cancellationToken);

        DateTime? expiryUtc = long.TryParse(result.UserInfo.ExpDate, out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
        int? maxConnections = int.TryParse(result.UserInfo.MaxConnections, out var max) ? max : null;

        return new AccountInfo(result.UserInfo.Status, expiryUtc, maxConnections);
    }
}
