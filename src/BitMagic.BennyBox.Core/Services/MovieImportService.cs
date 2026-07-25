using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

public class MovieImportService
{
    private readonly IEnumerable<IMovieSource> _sources;
    private readonly IMovieRepository _movieRepository;

    public MovieImportService(IEnumerable<IMovieSource> sources, IMovieRepository movieRepository)
    {
        _sources = sources;
        _movieRepository = movieRepository;
    }

    // Unlike PlaylistImportService, a missing source is not an error here - most profile source
    // types (e.g. M3U) simply have no movies to import, so this is a normal no-op for them.
    public async Task<MovieImportResult?> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.SourceType == profile.SourceType);
        if (source is null)
        {
            return null;
        }

        var result = await source.ImportAsync(profile, cancellationToken);

        if (!result.NotModified)
        {
            await _movieRepository.ReplaceMoviesAsync(profile.Id, result.Categories, result.Movies, cancellationToken);
        }

        return result;
    }

    public async Task<MovieDetails?> GetDetailsAsync(ProfileSource profile, Movie movie, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.SourceType == profile.SourceType);
        return source is null ? null : await source.GetDetailsAsync(profile, movie, cancellationToken);
    }
}
