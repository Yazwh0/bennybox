using Dapper;
using Iptv.Core.Models;
using Iptv.Core.Services;

namespace Iptv.Data.Sqlite;

public class SqliteSeriesRepository : ISeriesRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteSeriesRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // Microsoft.Data.Sqlite has no real async I/O - its "Async" methods run fully synchronously
    // on the calling thread. Task.Run is the documented way to keep SQLite work off the UI thread.
    public Task ReplaceSeriesAsync(
        Guid profileId,
        IReadOnlyList<Category> categories,
        IReadOnlyList<Series> seriesList,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            using var transaction = connection.BeginTransaction();

            connection.Execute("DELETE FROM Series WHERE ProfileId = @profileId", new { profileId }, transaction);
            connection.Execute("DELETE FROM SeriesCategories WHERE ProfileId = @profileId", new { profileId }, transaction);

            if (categories.Count > 0)
            {
                connection.Execute(
                    "INSERT INTO SeriesCategories (Id, ProfileId, Name, SortOrder) VALUES (@Id, @ProfileId, @Name, @SortOrder)",
                    categories, transaction);
            }

            if (seriesList.Count > 0)
            {
                connection.Execute(
                    """
                    INSERT INTO Series (Id, ProfileId, SourceSeriesId, CategoryId, Name, CoverUrl, Plot, Genre, ReleaseDate, Rating)
                    VALUES (@Id, @ProfileId, @SourceSeriesId, @CategoryId, @Name, @CoverUrl, @Plot, @Genre, @ReleaseDate, @Rating)
                    """,
                    seriesList, transaction);
            }

            transaction.Commit();
        }, cancellationToken);

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<Category>(
                "SELECT * FROM SeriesCategories WHERE ProfileId = @profileId ORDER BY SortOrder", new { profileId });
            return (IReadOnlyList<Category>)rows.ToList();
        }, cancellationToken);

    public Task<IReadOnlyList<Series>> GetSeriesAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<Series>(
                "SELECT * FROM Series WHERE ProfileId = @profileId ORDER BY Name", new { profileId });
            return (IReadOnlyList<Series>)rows.ToList();
        }, cancellationToken);
}
