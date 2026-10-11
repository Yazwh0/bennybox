using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Jellyfin;

public class JellyfinMovieSource : IMovieSource
{
    private readonly JellyfinClient _client;

    public JellyfinMovieSource(JellyfinClient client)
    {
        _client = client;
    }

    public ProfileSourceType SourceType => ProfileSourceType.Jellyfin;

    public async Task<MovieImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        var (categories, items, session) = await JellyfinLibraryReader.ReadAsync(_client, profile, JellyfinLibraryTarget.Movies, cancellationToken);
        var movies = items.Select(x => JellyfinLibraryReader.ToMovie(x.Item, x.CategoryId, profile, session)).ToList();
        return new MovieImportResult(categories, movies);
    }

    // The bulk listing already carries Plot/Genre/ReleaseDate (persisted by SqliteMovieRepository) -
    // this live fetch is mainly for Duration, which isn't stored, and picks up any metadata edits made
    // on the server since the last Refresh.
    public Task<MovieDetails?> GetDetailsAsync(ProfileSource profile, Movie movie, CancellationToken cancellationToken = default) =>
        GetItemDetailsAsync(_client, profile, movie, cancellationToken);

    internal static async Task<MovieDetails?> GetItemDetailsAsync(JellyfinClient client, ProfileSource profile, Movie movie, CancellationToken cancellationToken)
    {
        var (item, _) = await client.GetAsync<JellyfinItem>(profile, $"Items/{movie.SourceMovieId}?userId={{userId}}", cancellationToken);
        if (string.IsNullOrEmpty(item.Id))
        {
            return null;
        }

        return new MovieDetails(
            item.Overview ?? movie.Plot,
            item.Genres is { Count: > 0 } genres ? string.Join(", ", genres) : movie.Genre,
            JellyfinClient.FormatReleaseDate(item) ?? movie.ReleaseDate,
            JellyfinClient.FormatDuration(item.RunTimeTicks),
            item.CommunityRating ?? movie.Rating);
    }
}
