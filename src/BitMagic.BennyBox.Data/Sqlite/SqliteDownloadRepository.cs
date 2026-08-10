using Dapper;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Data.Sqlite;

public class SqliteDownloadRepository : IDownloadRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteDownloadRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<Guid> CreateAsync(Download download, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                """
                INSERT INTO Downloads (Id, OriginalProfileId, ContentType, OriginalSourceId, Title, CoverUrl, Status, BytesDownloaded, TotalBytes, LocalRelativePath, ErrorMessage, StartedUtc, CompletedUtc)
                VALUES (@Id, @OriginalProfileId, @ContentType, @OriginalSourceId, @Title, @CoverUrl, @Status, @BytesDownloaded, @TotalBytes, @LocalRelativePath, @ErrorMessage, @StartedUtc, @CompletedUtc)
                """,
                download);
            return download.Id;
        }, cancellationToken);

    public Task UpdateProgressAsync(Guid id, long bytesDownloaded, long? totalBytes, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "UPDATE Downloads SET Status = @status, BytesDownloaded = @bytesDownloaded, TotalBytes = @totalBytes WHERE Id = @id",
                new { id, status = DownloadStatus.Downloading, bytesDownloaded, totalBytes });
        }, cancellationToken);

    public Task MarkCompletedAsync(Guid id, string localRelativePath, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "UPDATE Downloads SET Status = @status, LocalRelativePath = @localRelativePath, CompletedUtc = @completedUtc WHERE Id = @id",
                new { id, status = DownloadStatus.Completed, localRelativePath, completedUtc = DateTime.UtcNow });
        }, cancellationToken);

    public Task MarkFailedAsync(Guid id, string errorMessage, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "UPDATE Downloads SET Status = @status, ErrorMessage = @errorMessage, CompletedUtc = @completedUtc WHERE Id = @id",
                new { id, status = DownloadStatus.Failed, errorMessage, completedUtc = DateTime.UtcNow });
        }, cancellationToken);

    public Task MarkCanceledAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "UPDATE Downloads SET Status = @status, CompletedUtc = @completedUtc WHERE Id = @id",
                new { id, status = DownloadStatus.Canceled, completedUtc = DateTime.UtcNow });
        }, cancellationToken);

    public Task<IReadOnlyList<Download>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<Download>("SELECT * FROM Downloads ORDER BY StartedUtc DESC");
            return (IReadOnlyList<Download>)rows.ToList();
        }, cancellationToken);

    public Task SetDestinationPathAsync(Guid id, string localRelativePath, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "UPDATE Downloads SET LocalRelativePath = @localRelativePath WHERE Id = @id",
                new { id, localRelativePath });
        }, cancellationToken);

    public Task RequeueAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "UPDATE Downloads SET Status = @status, ErrorMessage = NULL, CompletedUtc = NULL WHERE Id = @id",
                new { id, status = DownloadStatus.Queued });
        }, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute("DELETE FROM Downloads WHERE Id = @id", new { id });
        }, cancellationToken);

    public Task MarkInterruptedDownloadsAsFailedAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "UPDATE Downloads SET Status = @failed, ErrorMessage = @message, CompletedUtc = @completedUtc WHERE Status IN (@queued, @downloading)",
                new { failed = DownloadStatus.Failed, message = "Interrupted - the app closed before this finished.", completedUtc = DateTime.UtcNow, queued = DownloadStatus.Queued, downloading = DownloadStatus.Downloading });
        }, cancellationToken);
}
