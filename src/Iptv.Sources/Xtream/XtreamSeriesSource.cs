using System.Runtime.Versioning;
using Iptv.Core.Models;
using Iptv.Core.Services;

namespace Iptv.Sources.Xtream;

[SupportedOSPlatform("windows")]
public class XtreamSeriesSource : ISeriesSource
{
    private readonly XtreamClient _client;

    public XtreamSeriesSource(XtreamClient client)
    {
        _client = client;
    }

    public ProfileSourceType SourceType => ProfileSourceType.XtreamCodes;

    public async Task<SeriesImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        var (serverUrl, username, password) = ResolveCredentials(profile);

        var xtreamCategories = await _client.GetSeriesCategoriesAsync(serverUrl, username, password, cancellationToken);
        var xtreamSeries = await _client.GetSeriesAsync(serverUrl, username, password, cancellationToken);

        var categories = xtreamCategories
            .Select((c, index) => new Category
            {
                Id = c.CategoryId,
                ProfileId = profile.Id,
                Name = c.CategoryName,
                SortOrder = index
            })
            .ToList();

        var seriesList = xtreamSeries
            .Select(s => new Series
            {
                ProfileId = profile.Id,
                SourceSeriesId = s.SeriesId,
                CategoryId = s.CategoryId,
                Name = s.Name,
                CoverUrl = string.IsNullOrWhiteSpace(s.Cover) ? null : s.Cover,
                Plot = s.Plot,
                Genre = s.Genre,
                ReleaseDate = s.ReleaseDate,
                Rating = s.Rating
            })
            .ToList();

        return new SeriesImportResult(categories, seriesList);
    }

    public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(ProfileSource profile, Series series, CancellationToken cancellationToken = default)
    {
        var (serverUrl, username, password) = ResolveCredentials(profile);

        var info = await _client.GetSeriesInfoAsync(serverUrl, username, password, series.SourceSeriesId, cancellationToken);
        if (info.Episodes is null)
        {
            return [];
        }

        var episodes = new List<Episode>();
        foreach (var (seasonKey, seasonEpisodes) in info.Episodes)
        {
            var season = int.TryParse(seasonKey, out var s) ? s : 0;
            foreach (var e in seasonEpisodes)
            {
                episodes.Add(new Episode
                {
                    SourceEpisodeId = e.Id,
                    Title = string.IsNullOrWhiteSpace(e.Title) ? $"Episode {e.EpisodeNum}" : e.Title,
                    Season = season,
                    EpisodeNumber = e.EpisodeNum,
                    PlotSummary = e.Info?.Plot,
                    StreamUrl = XtreamClient.BuildSeriesEpisodeUrl(serverUrl, username, password, e.Id, e.ContainerExtension)
                });
            }
        }

        return episodes
            .OrderBy(e => e.Season)
            .ThenBy(e => e.EpisodeNumber)
            .ToList();
    }

    private static (string ServerUrl, string Username, string Password) ResolveCredentials(ProfileSource profile)
    {
        if (string.IsNullOrWhiteSpace(profile.XtreamServerUrl) || string.IsNullOrWhiteSpace(profile.XtreamUsername))
        {
            throw new InvalidOperationException("Profile has no Xtream Codes server/username configured.");
        }

        var password = CredentialProtector.Unprotect(profile.XtreamPasswordEncrypted)
            ?? throw new InvalidOperationException("Profile has no Xtream Codes password configured.");

        return (profile.XtreamServerUrl.TrimEnd('/'), profile.XtreamUsername, password);
    }
}
