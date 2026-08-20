using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using BitMagic.BennyBox.Sources.Xmltv;

namespace BitMagic.BennyBox.Sources.Xtream;

public class XtreamEpgSource : IEpgSource
{
    private readonly HttpClient _httpClient;
    private readonly ICredentialProtector _credentialProtector;

    public XtreamEpgSource(HttpClient httpClient, ICredentialProtector credentialProtector)
    {
        _httpClient = httpClient;
        _credentialProtector = credentialProtector;
    }

    public EpgSourceType SourceType => EpgSourceType.XtreamEmbedded;

    public Task<EpgFetchResult> GetProgrammesAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profile.XtreamServerUrl) || string.IsNullOrWhiteSpace(profile.XtreamUsername))
        {
            throw new InvalidOperationException("Profile has no Xtream Codes server/username configured.");
        }

        var password = _credentialProtector.Unprotect(profile.XtreamPasswordEncrypted)
            ?? throw new InvalidOperationException("Profile has no Xtream Codes password configured.");

        var url = $"{profile.XtreamServerUrl.TrimEnd('/')}/xmltv.php?username={Uri.EscapeDataString(profile.XtreamUsername)}&password={Uri.EscapeDataString(password)}";

        // This panel's xmltv.php sends no ETag/Last-Modified headers, so this conditional GET is
        // currently a no-op (always a fresh 200) - but reuse the shared helper for correctness/consistency
        // in case the panel adds caching support later.
        return XmltvEpgSource.FetchAsync(url, profile.Id, profile.EpgETag, profile.EpgLastModified, _httpClient, cancellationToken);
    }
}
