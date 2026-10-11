using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Jellyfin;

// Jellyfin sends no caching headers on its JSON API, so this is always a full fetch (NotModified is
// never true) - same situation as Xtream's xmltv.php.
public class JellyfinEpgSource : IEpgSource
{
    // From a day back (Guide's "Previous" day) through whatever the server has guide data for.
    private static readonly TimeSpan History = TimeSpan.FromDays(1);

    private readonly JellyfinClient _client;

    public JellyfinEpgSource(JellyfinClient client)
    {
        _client = client;
    }

    public EpgSourceType SourceType => EpgSourceType.JellyfinEmbedded;

    public async Task<EpgFetchResult> GetProgrammesAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        var minEndDate = Uri.EscapeDataString(DateTime.UtcNow.Subtract(History).ToString("o"));
        var (response, _) = await _client.GetAsync<JellyfinItemsResponse>(
            profile,
            $"LiveTv/Programs?userId={{userId}}&minEndDate={minEndDate}&fields=Overview&enableImages=false&enableUserData=false&enableTotalRecordCount=false",
            cancellationToken);

        var programmes = response.Items
            .Where(p => p.ChannelId is not null && p.StartDate is not null && p.EndDate is not null)
            .Select(p => new EpgProgramme
            {
                ProfileId = profile.Id,
                ChannelTvgId = p.ChannelId!,
                Title = string.IsNullOrWhiteSpace(p.EpisodeTitle) ? p.Name ?? string.Empty : $"{p.Name}: {p.EpisodeTitle}",
                Description = p.Overview,
                StartUtc = p.StartDate!.Value.ToUniversalTime(),
                EndUtc = p.EndDate!.Value.ToUniversalTime()
            })
            .ToList();

        return new EpgFetchResult(false, null, null, programmes.ToAsyncEnumerable());
    }
}
