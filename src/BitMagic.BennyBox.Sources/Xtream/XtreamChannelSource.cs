using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Xtream;

public class XtreamChannelSource : IChannelSource
{
    private readonly XtreamClient _client;
    private readonly ICredentialProtector _credentialProtector;

    public XtreamChannelSource(XtreamClient client, ICredentialProtector credentialProtector)
    {
        _client = client;
        _credentialProtector = credentialProtector;
    }

    public ProfileSourceType SourceType => ProfileSourceType.XtreamCodes;

    public async Task<ChannelImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profile.XtreamServerUrl) || string.IsNullOrWhiteSpace(profile.XtreamUsername))
        {
            throw new InvalidOperationException("Profile has no Xtream Codes server/username configured.");
        }

        var password = _credentialProtector.Unprotect(profile.XtreamPasswordEncrypted)
            ?? throw new InvalidOperationException("Profile has no Xtream Codes password configured.");

        var serverUrl = profile.XtreamServerUrl.TrimEnd('/');
        var username = profile.XtreamUsername;

        // Validate credentials up front so a bad login surfaces a clear error rather than an empty channel list.
        await _client.AuthenticateAsync(serverUrl, username, password, cancellationToken);

        var xtreamCategories = await _client.GetLiveCategoriesAsync(serverUrl, username, password, cancellationToken);
        var xtreamStreams = await _client.GetLiveStreamsAsync(serverUrl, username, password, cancellationToken);

        var categories = xtreamCategories
            .Select((c, index) => new Category
            {
                Id = c.CategoryId,
                ProfileId = profile.Id,
                Name = c.CategoryName,
                SortOrder = index
            })
            .ToList();

        var channels = xtreamStreams
            .Select(s => new Channel
            {
                ProfileId = profile.Id,
                SourceChannelId = s.StreamId,
                CategoryId = s.CategoryId,
                Name = s.Name,
                LogoUrl = string.IsNullOrWhiteSpace(s.StreamIcon) ? null : s.StreamIcon,
                StreamUrl = XtreamClient.BuildStreamUrl(serverUrl, username, password, s.StreamId),
                TvgId = string.IsNullOrWhiteSpace(s.EpgChannelId) ? null : s.EpgChannelId,
                Number = int.TryParse(s.Num, out var number) ? number : 0,
                HasCatchup = s.TvArchive == 1,
                CatchupDays = s.TvArchiveDuration
            })
            .ToList();

        return new ChannelImportResult(categories, channels);
    }

    public string? BuildTimeshiftUrl(ProfileSource profile, Channel channel, DateTime startUtc, TimeSpan duration)
    {
        if (!channel.HasCatchup ||
            string.IsNullOrWhiteSpace(profile.XtreamServerUrl) || string.IsNullOrWhiteSpace(profile.XtreamUsername))
        {
            return null;
        }

        var password = _credentialProtector.Unprotect(profile.XtreamPasswordEncrypted);
        if (password is null)
        {
            return null;
        }

        return XtreamClient.BuildTimeshiftUrl(profile.XtreamServerUrl.TrimEnd('/'), profile.XtreamUsername, password, channel.SourceChannelId, startUtc, duration);
    }
}
