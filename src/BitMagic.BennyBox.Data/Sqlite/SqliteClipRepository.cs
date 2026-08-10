using Dapper;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Data.Sqlite;

public class SqliteClipRepository : IClipRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteClipRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // Microsoft.Data.Sqlite has no real async I/O - its "Async" methods run fully synchronously
    // on the calling thread. Task.Run is the documented way to keep SQLite work off the UI thread.
    public Task ReplaceClipsAsync(
        Guid profileId,
        IReadOnlyList<Category> categories,
        IReadOnlyList<Movie> clips,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            using var transaction = connection.BeginTransaction();

            connection.Execute("DELETE FROM Clips WHERE ProfileId = @profileId", new { profileId }, transaction);
            connection.Execute("DELETE FROM ClipCategories WHERE ProfileId = @profileId", new { profileId }, transaction);

            if (categories.Count > 0)
            {
                connection.Execute(
                    "INSERT INTO ClipCategories (Id, ProfileId, Name, SortOrder) VALUES (@Id, @ProfileId, @Name, @SortOrder)",
                    categories, transaction);
            }

            if (clips.Count > 0)
            {
                connection.Execute(
                    """
                    INSERT INTO Clips (Id, ProfileId, SourceClipId, CategoryId, Name, CoverUrl, StreamUrl, Rating, Plot, Genre, ReleaseDate)
                    VALUES (@Id, @ProfileId, @SourceMovieId, @CategoryId, @Name, @CoverUrl, @StreamUrl, @Rating, @Plot, @Genre, @ReleaseDate)
                    """,
                    clips, transaction);
            }

            transaction.Commit();
        }, cancellationToken);

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<Category>(
                "SELECT * FROM ClipCategories WHERE ProfileId = @profileId ORDER BY SortOrder", new { profileId });
            return (IReadOnlyList<Category>)rows.ToList();
        }, cancellationToken);

    public Task<IReadOnlyList<Movie>> GetClipsAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<Movie>(
                "SELECT Id, ProfileId, SourceClipId AS SourceMovieId, CategoryId, Name, CoverUrl, StreamUrl, Rating, Plot, Genre, ReleaseDate FROM Clips WHERE ProfileId = @profileId ORDER BY Name",
                new { profileId });
            return (IReadOnlyList<Movie>)rows.ToList();
        }, cancellationToken);
}
