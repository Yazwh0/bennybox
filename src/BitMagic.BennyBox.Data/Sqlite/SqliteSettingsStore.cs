using Dapper;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Data.Sqlite;

public class SqliteSettingsStore : ISettingsStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteSettingsStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // Microsoft.Data.Sqlite has no real async I/O - its "Async" methods run fully synchronously
    // on the calling thread. Task.Run is the documented way to keep SQLite work off the UI thread.
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            return connection.QuerySingleOrDefault<string?>("SELECT Value FROM Settings WHERE Key = @key", new { key });
        }, cancellationToken);

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "INSERT INTO Settings (Key, Value) VALUES (@key, @value) ON CONFLICT(Key) DO UPDATE SET Value = @value",
                new { key, value });
        }, cancellationToken);
}
