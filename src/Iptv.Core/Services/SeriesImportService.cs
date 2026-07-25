using Iptv.Core.Models;

namespace Iptv.Core.Services;

public class SeriesImportService
{
    private readonly IEnumerable<ISeriesSource> _sources;
    private readonly ISeriesRepository _seriesRepository;
    private readonly IProfileRepository _profileRepository;

    public SeriesImportService(
        IEnumerable<ISeriesSource> sources,
        ISeriesRepository seriesRepository,
        IProfileRepository profileRepository)
    {
        _sources = sources;
        _seriesRepository = seriesRepository;
        _profileRepository = profileRepository;
    }

    // Unlike PlaylistImportService, a missing source is not an error here - most profile source
    // types (e.g. M3U) simply have no series to import, so this is a normal no-op for them.
    public async Task<SeriesImportResult?> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.SourceType == profile.SourceType);
        if (source is null)
        {
            return null;
        }

        var result = await source.ImportAsync(profile, cancellationToken);

        if (!result.NotModified)
        {
            await _seriesRepository.ReplaceSeriesAsync(profile.Id, result.Categories, result.SeriesList, cancellationToken);
        }

        return result;
    }

    public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(ProfileSource profile, Series series, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.SourceType == profile.SourceType);
        return source is null ? [] : await source.GetEpisodesAsync(profile, series, cancellationToken);
    }
}
