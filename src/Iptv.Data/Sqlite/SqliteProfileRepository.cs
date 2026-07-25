using Dapper;
using Iptv.Core.Models;
using Iptv.Core.Services;

namespace Iptv.Data.Sqlite;

public class SqliteProfileRepository : IProfileRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteProfileRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // Microsoft.Data.Sqlite has no real async I/O - its "Async" methods run fully synchronously
    // on the calling thread. Task.Run is the documented way to keep SQLite work off the UI thread.
    public Task<IReadOnlyList<ProfileSource>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<ProfileSource>("SELECT * FROM Profiles ORDER BY SortOrder");
            return (IReadOnlyList<ProfileSource>)rows.ToList();
        }, cancellationToken);

    public Task<ProfileSource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            return connection.QuerySingleOrDefault<ProfileSource>("SELECT * FROM Profiles WHERE Id = @id", new { id });
        }, cancellationToken);

    public Task AddAsync(ProfileSource profile, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                """
                INSERT INTO Profiles (Id, Name, SourceType, M3uUrl, XtreamServerUrl, XtreamUsername, XtreamPasswordEncrypted, EpgSourceType, EpgUrl, LastRefreshedUtc, SortOrder, PlaylistETag, PlaylistLastModified, EpgETag, EpgLastModified, XtreamStatus, XtreamExpiryUtc, XtreamMaxConnections)
                VALUES (@Id, @Name, @SourceType, @M3uUrl, @XtreamServerUrl, @XtreamUsername, @XtreamPasswordEncrypted, @EpgSourceType, @EpgUrl, @LastRefreshedUtc, @SortOrder, @PlaylistETag, @PlaylistLastModified, @EpgETag, @EpgLastModified, @XtreamStatus, @XtreamExpiryUtc, @XtreamMaxConnections)
                """, profile);
        }, cancellationToken);

    public Task UpdateAsync(ProfileSource profile, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                """
                UPDATE Profiles SET
                    Name = @Name, SourceType = @SourceType, M3uUrl = @M3uUrl,
                    XtreamServerUrl = @XtreamServerUrl, XtreamUsername = @XtreamUsername, XtreamPasswordEncrypted = @XtreamPasswordEncrypted,
                    EpgSourceType = @EpgSourceType, EpgUrl = @EpgUrl, LastRefreshedUtc = @LastRefreshedUtc, SortOrder = @SortOrder,
                    PlaylistETag = @PlaylistETag, PlaylistLastModified = @PlaylistLastModified, EpgETag = @EpgETag, EpgLastModified = @EpgLastModified,
                    XtreamStatus = @XtreamStatus, XtreamExpiryUtc = @XtreamExpiryUtc, XtreamMaxConnections = @XtreamMaxConnections
                WHERE Id = @Id
                """, profile);
        }, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute("DELETE FROM Channels WHERE ProfileId = @id", new { id });
            connection.Execute("DELETE FROM Categories WHERE ProfileId = @id", new { id });
            connection.Execute("DELETE FROM Series WHERE ProfileId = @id", new { id });
            connection.Execute("DELETE FROM SeriesCategories WHERE ProfileId = @id", new { id });
            connection.Execute("DELETE FROM Movies WHERE ProfileId = @id", new { id });
            connection.Execute("DELETE FROM MovieCategories WHERE ProfileId = @id", new { id });
            connection.Execute("DELETE FROM Profiles WHERE Id = @id", new { id });
        }, cancellationToken);
}
