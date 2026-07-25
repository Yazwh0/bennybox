using Dapper;
using Iptv.Core.Models;
using Iptv.Core.Services;

namespace Iptv.Data.Sqlite;

public class SqliteWatchProgressRepository : IWatchProgressRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteWatchProgressRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task SaveAsync(WatchProgress progress, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                """
                INSERT INTO WatchProgress (ProfileId, ContentType, ContentKey, Title, CoverUrl, StreamUrl, PositionSeconds, DurationSeconds, UpdatedUtc)
                VALUES (@ProfileId, @ContentType, @ContentKey, @Title, @CoverUrl, @StreamUrl, @PositionSeconds, @DurationSeconds, @UpdatedUtc)
                ON CONFLICT (ProfileId, ContentType, ContentKey) DO UPDATE SET
                    Title = excluded.Title,
                    CoverUrl = excluded.CoverUrl,
                    StreamUrl = excluded.StreamUrl,
                    PositionSeconds = excluded.PositionSeconds,
                    DurationSeconds = excluded.DurationSeconds,
                    UpdatedUtc = excluded.UpdatedUtc
                """, progress);
        }, cancellationToken);

    public Task<WatchProgress?> GetAsync(Guid profileId, WatchProgressContentType contentType, string contentKey, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            return connection.QuerySingleOrDefault<WatchProgress>(
                "SELECT * FROM WatchProgress WHERE ProfileId = @profileId AND ContentType = @contentType AND ContentKey = @contentKey",
                new { profileId, contentType, contentKey });
        }, cancellationToken);

    public Task RemoveAsync(Guid profileId, WatchProgressContentType contentType, string contentKey, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "DELETE FROM WatchProgress WHERE ProfileId = @profileId AND ContentType = @contentType AND ContentKey = @contentKey",
                new { profileId, contentType, contentKey });
        }, cancellationToken);

    public Task<IReadOnlyList<WatchProgress>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<WatchProgress>(
                "SELECT * FROM WatchProgress ORDER BY UpdatedUtc DESC LIMIT @limit", new { limit });
            return (IReadOnlyList<WatchProgress>)rows.ToList();
        }, cancellationToken);
}
