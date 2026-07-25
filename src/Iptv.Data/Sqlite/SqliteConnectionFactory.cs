using Dapper;
using Microsoft.Data.Sqlite;

namespace Iptv.Data.Sqlite;

public class SqliteConnectionFactory
{
    private readonly string _connectionString;
    private static bool _migrationsApplied;
    private static readonly object MigrationLock = new();

    static SqliteConnectionFactory()
    {
        SqlMapper.AddTypeHandler(new GuidTypeHandler());
        SqlMapper.AddTypeHandler(new NullableGuidTypeHandler());
    }

    public SqliteConnectionFactory(string? dbPath = null)
    {
        var path = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BennyBox", "iptv.db");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
    }

    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = SqliteSchema.CreateTablesSql;
            command.ExecuteNonQuery();
        }

        EnsureMigrations(connection);

        return connection;
    }

    // Columns added after the initial schema go here as best-effort ALTER TABLEs, applied once per
    // process (not once per connection - CreateConnection runs per-call, this would otherwise retry
    // and swallow a "duplicate column" exception on every single repository operation).
    private static void EnsureMigrations(SqliteConnection connection)
    {
        if (_migrationsApplied)
        {
            return;
        }

        lock (MigrationLock)
        {
            if (_migrationsApplied)
            {
                return;
            }

            TryAddColumn(connection, "Profiles", "PlaylistETag", "TEXT");
            TryAddColumn(connection, "Profiles", "PlaylistLastModified", "TEXT");
            TryAddColumn(connection, "Profiles", "EpgETag", "TEXT");
            TryAddColumn(connection, "Profiles", "EpgLastModified", "TEXT");
            TryAddColumn(connection, "Profiles", "XtreamStatus", "TEXT");
            TryAddColumn(connection, "Profiles", "XtreamExpiryUtc", "TEXT");
            TryAddColumn(connection, "Profiles", "XtreamMaxConnections", "INTEGER");
            TryAddColumn(connection, "Channels", "HasCatchup", "INTEGER NOT NULL DEFAULT 0");
            TryAddColumn(connection, "Channels", "CatchupDays", "INTEGER NOT NULL DEFAULT 0");

            _migrationsApplied = true;
        }
    }

    private static void TryAddColumn(SqliteConnection connection, string table, string column, string sqlType)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {sqlType}";
            command.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Column already exists from a previous run - fine.
        }
    }
}
