namespace BitMagic.BennyBox.Core.Services;

// Consulted by PlayerViewModel before playing a Movie/Episode/Clip - if a downloaded copy of the SAME
// title exists in the Downloads profile (see DownloadManager), its local StreamUrl is preferred over
// re-streaming from the original (possibly remote/slow) source. Matching is by normalized title (+
// season/episode for episodes), not any shared identifier - the Downloads profile's own scan produces
// entirely independent Ids/SourceIds from the original profile's, same as any two Local Folder
// profiles scanning overlapping content would. Cheap no-op (one settings read, no further I/O) for
// anyone who's never downloaded anything.
public class DownloadPlaybackResolver
{
    private readonly DownloadManager _downloadManager;
    private readonly IMovieRepository _movieRepository;
    private readonly IClipRepository _clipRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly IEpisodeCacheRepository _episodeCacheRepository;

    public DownloadPlaybackResolver(
        DownloadManager downloadManager,
        IMovieRepository movieRepository,
        IClipRepository clipRepository,
        ISeriesRepository seriesRepository,
        IEpisodeCacheRepository episodeCacheRepository)
    {
        _downloadManager = downloadManager;
        _movieRepository = movieRepository;
        _clipRepository = clipRepository;
        _seriesRepository = seriesRepository;
        _episodeCacheRepository = episodeCacheRepository;
    }

    public async Task<string?> TryResolveMovieStreamUrlAsync(string title, CancellationToken cancellationToken = default)
    {
        var downloadsProfile = await _downloadManager.GetDownloadsProfileIfExistsAsync(cancellationToken);
        if (downloadsProfile is null)
        {
            return null;
        }

        var movies = await _movieRepository.GetMoviesAsync(downloadsProfile.Id, cancellationToken);
        return movies.FirstOrDefault(m => Normalize(m.Name) == Normalize(title))?.StreamUrl;
    }

    public async Task<string?> TryResolveClipStreamUrlAsync(string title, CancellationToken cancellationToken = default)
    {
        var downloadsProfile = await _downloadManager.GetDownloadsProfileIfExistsAsync(cancellationToken);
        if (downloadsProfile is null)
        {
            return null;
        }

        var clips = await _clipRepository.GetClipsAsync(downloadsProfile.Id, cancellationToken);
        return clips.FirstOrDefault(c => Normalize(c.Name) == Normalize(title))?.StreamUrl;
    }

    public async Task<string?> TryResolveEpisodeStreamUrlAsync(string seriesName, int season, int episodeNumber, CancellationToken cancellationToken = default)
    {
        var downloadsProfile = await _downloadManager.GetDownloadsProfileIfExistsAsync(cancellationToken);
        if (downloadsProfile is null)
        {
            return null;
        }

        var seriesList = await _seriesRepository.GetSeriesAsync(downloadsProfile.Id, cancellationToken);
        var matchedSeries = seriesList.FirstOrDefault(s => Normalize(s.Name) == Normalize(seriesName));
        if (matchedSeries is null)
        {
            return null;
        }

        var episodes = await _episodeCacheRepository.GetCachedEpisodesAsync(downloadsProfile.Id, matchedSeries.SourceSeriesId, cancellationToken);
        return episodes?.FirstOrDefault(e => e.Season == season && e.EpisodeNumber == episodeNumber)?.StreamUrl;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
