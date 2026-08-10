using Dapper;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Data.Sqlite;

public class SqliteMovieRepository : IMovieRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteMovieRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // Microsoft.Data.Sqlite has no real async I/O - its "Async" methods run fully synchronously
    // on the calling thread. Task.Run is the documented way to keep SQLite work off the UI thread.
    public Task ReplaceMoviesAsync(
        Guid profileId,
        IReadOnlyList<Category> categories,
        IReadOnlyList<Movie> movies,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            using var transaction = connection.BeginTransaction();

            connection.Execute("DELETE FROM Movies WHERE ProfileId = @profileId", new { profileId }, transaction);
            connection.Execute("DELETE FROM MovieCategories WHERE ProfileId = @profileId", new { profileId }, transaction);

            if (categories.Count > 0)
            {
                connection.Execute(
                    "INSERT INTO MovieCategories (Id, ProfileId, Name, SortOrder) VALUES (@Id, @ProfileId, @Name, @SortOrder)",
                    categories, transaction);
            }

            if (movies.Count > 0)
            {
                // Duration still isn't persisted (no source populates it up front) - fetched on demand
                // per-title like before. Plot/Genre/ReleaseDate ARE persisted, unlike the old comment
                // here used to say - Xtream's bulk VOD listing genuinely doesn't have them (so they're
                // still null here for Xtream movies, refetched live on open, unchanged), but
                // LocalFolder/Sftp movies already read their NFO once during the scan itself - without
                // persisting that here, GetDetailsAsync silently discarded it and re-read the same NFO
                // over a fresh connection every single time the movie was opened, which is real,
                // avoidable latency over SFTP specifically.
                connection.Execute(
                    """
                    INSERT INTO Movies (Id, ProfileId, SourceMovieId, CategoryId, Name, CoverUrl, StreamUrl, Rating, Plot, Genre, ReleaseDate)
                    VALUES (@Id, @ProfileId, @SourceMovieId, @CategoryId, @Name, @CoverUrl, @StreamUrl, @Rating, @Plot, @Genre, @ReleaseDate)
                    """,
                    movies, transaction);
            }

            transaction.Commit();
        }, cancellationToken);

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<Category>(
                "SELECT * FROM MovieCategories WHERE ProfileId = @profileId ORDER BY SortOrder", new { profileId });
            return (IReadOnlyList<Category>)rows.ToList();
        }, cancellationToken);

    public Task<IReadOnlyList<Movie>> GetMoviesAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<Movie>(
                "SELECT * FROM Movies WHERE ProfileId = @profileId ORDER BY Name", new { profileId });
            return (IReadOnlyList<Movie>)rows.ToList();
        }, cancellationToken);
}
