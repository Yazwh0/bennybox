using Dapper;
using Iptv.Core.Services;

namespace Iptv.Data.Sqlite;

public class SqliteFavoriteRepository : IFavoriteRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteFavoriteRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // Microsoft.Data.Sqlite has no real async I/O - its "Async" methods run fully synchronously
    // on the calling thread. Task.Run is the documented way to keep SQLite work off the UI thread.
    public Task<IReadOnlySet<Guid>> GetFavoriteChannelIdsAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var ids = connection.Query<Guid>("SELECT ChannelId FROM Favorites");
            return (IReadOnlySet<Guid>)ids.ToHashSet();
        }, cancellationToken);

    public Task AddAsync(Guid profileId, Guid channelId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "INSERT OR REPLACE INTO Favorites (ChannelId, ProfileId, AddedUtc) VALUES (@channelId, @profileId, @addedUtc)",
                new { channelId, profileId, addedUtc = DateTime.UtcNow });
        }, cancellationToken);

    public Task RemoveAsync(Guid channelId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute("DELETE FROM Favorites WHERE ChannelId = @channelId", new { channelId });
        }, cancellationToken);
}
