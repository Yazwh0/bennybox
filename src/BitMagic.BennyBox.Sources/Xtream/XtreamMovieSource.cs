using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Xtream;

public class XtreamMovieSource : IMovieSource
{
    private readonly XtreamClient _client;
    private readonly ICredentialProtector _credentialProtector;

    public XtreamMovieSource(XtreamClient client, ICredentialProtector credentialProtector)
    {
        _client = client;
        _credentialProtector = credentialProtector;
    }

    public ProfileSourceType SourceType => ProfileSourceType.XtreamCodes;

    public async Task<MovieImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        var (serverUrl, username, password) = ResolveCredentials(profile);

        var xtreamCategories = await _client.GetVodCategoriesAsync(serverUrl, username, password, cancellationToken);
        var xtreamMovies = await _client.GetVodStreamsAsync(serverUrl, username, password, cancellationToken);

        var categories = xtreamCategories
            .Select((c, index) => new Category
            {
                Id = c.CategoryId,
                ProfileId = profile.Id,
                Name = c.CategoryName,
                SortOrder = index
            })
            .ToList();

        var movies = xtreamMovies
            .Select(m => new Movie
            {
                ProfileId = profile.Id,
                SourceMovieId = m.StreamId,
                CategoryId = m.CategoryId,
                Name = m.Name,
                CoverUrl = string.IsNullOrWhiteSpace(m.StreamIcon) ? null : m.StreamIcon,
                Rating = m.Rating,
                StreamUrl = XtreamClient.BuildVodStreamUrl(serverUrl, username, password, m.StreamId, m.ContainerExtension)
            })
            .ToList();

        return new MovieImportResult(categories, movies);
    }

    public async Task<MovieDetails?> GetDetailsAsync(ProfileSource profile, Movie movie, CancellationToken cancellationToken = default)
    {
        var (serverUrl, username, password) = ResolveCredentials(profile);

        var response = await _client.GetVodInfoAsync(serverUrl, username, password, movie.SourceMovieId, cancellationToken);
        if (response.Info is null)
        {
            return null;
        }

        return new MovieDetails(
            response.Info.Plot,
            response.Info.Genre,
            response.Info.ReleaseDate,
            response.Info.Duration,
            response.Info.Rating ?? movie.Rating);
    }

    private (string ServerUrl, string Username, string Password) ResolveCredentials(ProfileSource profile)
    {
        if (string.IsNullOrWhiteSpace(profile.XtreamServerUrl) || string.IsNullOrWhiteSpace(profile.XtreamUsername))
        {
            throw new InvalidOperationException("Profile has no Xtream Codes server/username configured.");
        }

        var password = _credentialProtector.Unprotect(profile.XtreamPasswordEncrypted)
            ?? throw new InvalidOperationException("Profile has no Xtream Codes password configured.");

        return (profile.XtreamServerUrl.TrimEnd('/'), profile.XtreamUsername, password);
    }
}
