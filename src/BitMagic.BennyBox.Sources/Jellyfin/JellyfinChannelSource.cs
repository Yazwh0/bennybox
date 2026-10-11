using System.Globalization;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Jellyfin;

// Jellyfin's own Live TV (tuners/M3U tuners configured on the server). A server with no Live TV set up
// simply returns no channels - a normal "no channels" result, not an error.
public class JellyfinChannelSource : IChannelSource
{
    private const string TvCategoryId = "tv";
    private const string RadioCategoryId = "radio";

    private readonly JellyfinClient _client;

    public JellyfinChannelSource(JellyfinClient client)
    {
        _client = client;
    }

    public ProfileSourceType SourceType => ProfileSourceType.Jellyfin;

    public async Task<ChannelImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        var (response, session) = await _client.GetAsync<JellyfinItemsResponse>(
            profile,
            "LiveTv/Channels?userId={userId}&enableUserData=false&imageTypeLimit=1&enableImageTypes=Primary&sortBy=SortName",
            cancellationToken);

        // Jellyfin has no channel groups of its own - just TV vs Radio.
        var channels = response.Items
            .Select(c => new Channel
            {
                ProfileId = profile.Id,
                SourceChannelId = c.Id,
                CategoryId = string.Equals(c.ChannelType, "Radio", StringComparison.OrdinalIgnoreCase) ? RadioCategoryId : TvCategoryId,
                Name = c.Name ?? c.Number ?? "Channel",
                LogoUrl = JellyfinClient.BuildImageUrl(session, c.Id, c.PrimaryImageTag, maxHeight: 200),
                StreamUrl = JellyfinClient.BuildLiveStreamUrl(session, c.Id),
                // JellyfinEpgSource keys programmes on the channel's item id.
                TvgId = c.Id,
                Number = ParseChannelNumber(c.Number)
            })
            .ToList();

        var categories = new List<Category>();
        if (channels.Any(c => c.CategoryId == TvCategoryId))
        {
            categories.Add(new Category { Id = TvCategoryId, ProfileId = profile.Id, Name = "TV", SortOrder = categories.Count });
        }
        if (channels.Any(c => c.CategoryId == RadioCategoryId))
        {
            categories.Add(new Category { Id = RadioCategoryId, ProfileId = profile.Id, Name = "Radio", SortOrder = categories.Count });
        }

        return new ChannelImportResult(categories, channels);
    }

    // Jellyfin's Live TV has no catch-up/timeshift API.
    public string? BuildTimeshiftUrl(ProfileSource profile, Channel channel, DateTime startUtc, TimeSpan duration) => null;

    // Channel numbers can be "5", "5.1" (ATSC sub-channels), or blank - only the major number fits Channel.Number.
    private static int ParseChannelNumber(string? number) =>
        number is not null && int.TryParse(number.Split('.', '-')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
}
