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
                // Plot/Genre/ReleaseDate/Duration aren't persisted - see Movie.cs, they're fetched
                // on demand per-title and only ever held in memory.
                connection.Execute(
                    """
                    INSERT INTO Movies (Id, ProfileId, SourceMovieId, CategoryId, Name, CoverUrl, StreamUrl, Rating)
                    VALUES (@Id, @ProfileId, @SourceMovieId, @CategoryId, @Name, @CoverUrl, @StreamUrl, @Rating)
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
