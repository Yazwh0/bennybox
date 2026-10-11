using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Jellyfin;

public class JellyfinSeriesSource : ISeriesSource
{
    private readonly JellyfinClient _client;

    public JellyfinSeriesSource(JellyfinClient client)
    {
        _client = client;
    }

    public ProfileSourceType SourceType => ProfileSourceType.Jellyfin;

    public async Task<SeriesImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        var (categories, items, session) = await JellyfinLibraryReader.ReadAsync(_client, profile, JellyfinLibraryTarget.Series, cancellationToken);

        var seriesList = items
            .Select(x => new Series
            {
                ProfileId = profile.Id,
                SourceSeriesId = x.Item.Id,
                CategoryId = x.CategoryId,
                Name = x.Item.Name ?? "Untitled",
                CoverUrl = JellyfinClient.BuildImageUrl(session, x.Item.Id, x.Item.PrimaryImageTag),
                Plot = x.Item.Overview,
                Genre = x.Item.Genres is { Count: > 0 } genres ? string.Join(", ", genres) : null,
                ReleaseDate = JellyfinClient.FormatReleaseDate(x.Item),
                Rating = x.Item.CommunityRating
            })
            .ToList();

        return new SeriesImportResult(categories, seriesList);
    }

    public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(ProfileSource profile, Series series, CancellationToken cancellationToken = default)
    {
        var (response, session) = await _client.GetAsync<JellyfinItemsResponse>(
            profile,
            $"Shows/{series.SourceSeriesId}/Episodes?userId={{userId}}&fields=Overview&enableUserData=false&enableImages=false",
            cancellationToken);

        return response.Items
            .Select(e => new Episode
            {
                SourceEpisodeId = e.Id,
                Title = string.IsNullOrWhiteSpace(e.Name) ? $"Episode {e.IndexNumber}" : e.Name,
                Season = e.ParentIndexNumber ?? 0,
                EpisodeNumber = e.IndexNumber ?? 0,
                PlotSummary = e.Overview,
                StreamUrl = JellyfinClient.BuildStreamUrl(session, e.Id, e.Container)
            })
            .OrderBy(e => e.Season)
            .ThenBy(e => e.EpisodeNumber)
            .ToList();
    }
}
