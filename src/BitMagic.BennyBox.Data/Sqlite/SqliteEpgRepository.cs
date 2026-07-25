using Dapper;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;
using Microsoft.Data.Sqlite;

namespace BitMagic.BennyBox.Data.Sqlite;

public class SqliteEpgRepository : IEpgRepository
{
    private const int BatchSize = 500;

    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteEpgRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // The outer method stays async because it interleaves with a genuinely-async IAsyncEnumerable
    // (streamed over HTTP), but every individual SQLite operation - which Microsoft.Data.Sqlite runs
    // fully synchronously regardless of the "Async" name - is pushed onto a background thread.
    public async Task ReplaceProgrammesAsync(Guid profileId, IAsyncEnumerable<EpgProgramme> programmes, CancellationToken cancellationToken = default)
    {
        using var connection = await Task.Run(() => _connectionFactory.CreateConnection(), cancellationToken);

        await Task.Run(() =>
        {
            using var deleteTransaction = connection.BeginTransaction();
            connection.Execute("DELETE FROM EpgProgramme WHERE ProfileId = @profileId", new { profileId }, deleteTransaction);
            deleteTransaction.Commit();
        }, cancellationToken);

        var batch = new List<EpgProgramme>(BatchSize);
        await foreach (var programme in programmes.WithCancellation(cancellationToken))
        {
            batch.Add(programme);
            if (batch.Count >= BatchSize)
            {
                var batchToInsert = batch;
                batch = new List<EpgProgramme>(BatchSize);
                await Task.Run(() => InsertBatch(connection, batchToInsert), cancellationToken);
            }
        }

        if (batch.Count > 0)
        {
            await Task.Run(() => InsertBatch(connection, batch), cancellationToken);
        }
    }

    private static void InsertBatch(SqliteConnection connection, List<EpgProgramme> batch)
    {
        using var transaction = connection.BeginTransaction();
        connection.Execute(
            """
            INSERT INTO EpgProgramme (Id, ProfileId, ChannelTvgId, Title, Description, StartUtc, EndUtc)
            VALUES (@Id, @ProfileId, @ChannelTvgId, @Title, @Description, @StartUtc, @EndUtc)
            """, batch, transaction);
        transaction.Commit();
    }

    public Task<IReadOnlyDictionary<string, EpgNowNext>> GetNowNextAsync(Guid profileId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<EpgProgramme>(
                """
                SELECT Id, ProfileId, ChannelTvgId, Title, Description, StartUtc, EndUtc FROM (
                    SELECT *, ROW_NUMBER() OVER (PARTITION BY ChannelTvgId ORDER BY StartUtc) AS Rn
                    FROM EpgProgramme
                    WHERE ProfileId = @profileId AND EndUtc > @nowUtc
                )
                WHERE Rn <= 2
                """, new { profileId, nowUtc });

            var result = new Dictionary<string, EpgNowNext>();
            foreach (var group in rows.GroupBy(r => r.ChannelTvgId))
            {
                var ordered = group.OrderBy(p => p.StartUtc).ToList();
                var now = ordered.FirstOrDefault(p => p.StartUtc <= nowUtc && p.EndUtc > nowUtc);
                var next = ordered.FirstOrDefault(p => p.StartUtc > nowUtc);
                result[group.Key] = new EpgNowNext(now, next);
            }

            return (IReadOnlyDictionary<string, EpgNowNext>)result;
        }, cancellationToken);

    public Task<IReadOnlyList<EpgProgramme>> GetProgrammesInRangeAsync(Guid profileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<EpgProgramme>(
                "SELECT * FROM EpgProgramme WHERE ProfileId = @profileId AND StartUtc < @endUtc AND EndUtc > @startUtc",
                new { profileId, startUtc, endUtc });
            return (IReadOnlyList<EpgProgramme>)rows.ToList();
        }, cancellationToken);
}
