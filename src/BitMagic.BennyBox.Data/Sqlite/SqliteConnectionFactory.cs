using Dapper;
using Microsoft.Data.Sqlite;

namespace BitMagic.BennyBox.Data.Sqlite;

public class SqliteConnectionFactory
{
    private readonly string _connectionString;
    private static bool _schemaCreated;
    private static readonly object SchemaLock = new();
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

        // Every repository call opens its own connection (see comment below), so with the default
        // rollback journal and a 0ms busy timeout, any two connections touching the DB at the same
        // moment - e.g. a refresh's write transaction still running when the window closes and
        // fire-and-forget-saves WindowState - throw SQLITE_BUSY ("database is locked") immediately
        // instead of one just waiting for the other. WAL lets readers and a writer run concurrently,
        // and the busy timeout makes writer-vs-writer contention wait briefly instead of throwing.
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }

        EnsureSchemaCreated(connection);
        EnsureMigrations(connection);

        return connection;
    }

    // Every repository call opens its own connection (see the "no real async I/O" comment on each
    // repository's Task.Run usage) - with several pages reloading concurrently on a single
    // ChannelsUpdatedMessage, that used to mean this ~15-table CREATE TABLE IF NOT EXISTS script ran
    // again on every single one of those connections. Harmless individually, but real, avoidable
    // SQLite overhead multiplied by however many concurrent DB calls are in flight - once per process
    // is enough, same rationale as EnsureMigrations below.
    private static void EnsureSchemaCreated(SqliteConnection connection)
    {
        if (_schemaCreated)
        {
            return;
        }

        lock (SchemaLock)
        {
            if (_schemaCreated)
            {
                return;
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = SqliteSchema.CreateTablesSql;
                command.ExecuteNonQuery();
            }

            _schemaCreated = true;
        }
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
