using Dapper;
using Iptv.Core.Models;
using Iptv.Core.Services;

namespace Iptv.Data.Sqlite;

public class SqliteWatchedItemRepository : IWatchedItemRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteWatchedItemRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<IReadOnlyList<WatchedItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<WatchedItem>("SELECT * FROM WatchedItems");
            return (IReadOnlyList<WatchedItem>)rows.ToList();
        }, cancellationToken);

    public Task MarkWatchedAsync(Guid profileId, WatchProgressContentType contentType, string contentKey, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                """
                INSERT INTO WatchedItems (ProfileId, ContentType, ContentKey, WatchedUtc)
                VALUES (@profileId, @contentType, @contentKey, @watchedUtc)
                ON CONFLICT (ProfileId, ContentType, ContentKey) DO UPDATE SET WatchedUtc = excluded.WatchedUtc
                """,
                new { profileId, contentType, contentKey, watchedUtc = DateTime.UtcNow });
        }, cancellationToken);

    public Task MarkUnwatchedAsync(Guid profileId, WatchProgressContentType contentType, string contentKey, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "DELETE FROM WatchedItems WHERE ProfileId = @profileId AND ContentType = @contentType AND ContentKey = @contentKey",
                new { profileId, contentType, contentKey });
        }, cancellationToken);
}
