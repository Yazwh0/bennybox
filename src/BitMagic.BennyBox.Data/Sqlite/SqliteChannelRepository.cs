using Dapper;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Data.Sqlite;

public class SqliteChannelRepository : IChannelRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteChannelRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // Microsoft.Data.Sqlite has no real async I/O - its "Async" methods run fully synchronously
    // on the calling thread. Task.Run is the documented way to keep SQLite work off the UI thread.
    public Task ReplaceChannelsAsync(
        Guid profileId,
        IReadOnlyList<Category> categories,
        IReadOnlyList<Channel> channels,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            using var transaction = connection.BeginTransaction();

            connection.Execute("DELETE FROM Channels WHERE ProfileId = @profileId", new { profileId }, transaction);
            connection.Execute("DELETE FROM Categories WHERE ProfileId = @profileId", new { profileId }, transaction);

            if (categories.Count > 0)
            {
                connection.Execute(
                    "INSERT INTO Categories (Id, ProfileId, Name, SortOrder) VALUES (@Id, @ProfileId, @Name, @SortOrder)",
                    categories, transaction);
            }

            if (channels.Count > 0)
            {
                connection.Execute(
                    """
                    INSERT INTO Channels (Id, ProfileId, SourceChannelId, CategoryId, Name, LogoUrl, StreamUrl, TvgId, Number, HasCatchup, CatchupDays)
                    VALUES (@Id, @ProfileId, @SourceChannelId, @CategoryId, @Name, @LogoUrl, @StreamUrl, @TvgId, @Number, @HasCatchup, @CatchupDays)
                    """,
                    channels, transaction);
            }

            transaction.Commit();
        }, cancellationToken);

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<Category>(
                "SELECT * FROM Categories WHERE ProfileId = @profileId ORDER BY SortOrder", new { profileId });
            return (IReadOnlyList<Category>)rows.ToList();
        }, cancellationToken);

    public Task<IReadOnlyList<Channel>> GetChannelsAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<Channel>(
                "SELECT * FROM Channels WHERE ProfileId = @profileId ORDER BY Number", new { profileId });
            return (IReadOnlyList<Channel>)rows.ToList();
        }, cancellationToken);
}
