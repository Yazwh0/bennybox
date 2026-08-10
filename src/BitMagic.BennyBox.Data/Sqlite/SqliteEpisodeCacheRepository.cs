using Dapper;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Data.Sqlite;

public class SqliteEpisodeCacheRepository : IEpisodeCacheRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteEpisodeCacheRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // Microsoft.Data.Sqlite has no real async I/O - its "Async" methods run fully synchronously
    // on the calling thread. Task.Run is the documented way to keep SQLite work off the UI thread.
    public Task<IReadOnlyList<Episode>?> GetCachedEpisodesAsync(Guid profileId, string seriesId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<Episode>(
                """
                SELECT SourceEpisodeId, Title, Season, EpisodeNumber, PlotSummary, StreamUrl
                FROM CachedEpisodes WHERE ProfileId = @profileId AND SeriesId = @seriesId
                ORDER BY Season, EpisodeNumber
                """,
                new { profileId, seriesId }).ToList();
            return (IReadOnlyList<Episode>?)(rows.Count > 0 ? rows : null);
        }, cancellationToken);

    public Task ReplaceCachedEpisodesAsync(Guid profileId, string seriesId, IReadOnlyList<Episode> episodes, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            using var transaction = connection.BeginTransaction();

            connection.Execute(
                "DELETE FROM CachedEpisodes WHERE ProfileId = @profileId AND SeriesId = @seriesId",
                new { profileId, seriesId }, transaction);

            if (episodes.Count > 0)
            {
                connection.Execute(
                    """
                    INSERT INTO CachedEpisodes (ProfileId, SeriesId, SourceEpisodeId, Title, Season, EpisodeNumber, PlotSummary, StreamUrl)
                    VALUES (@ProfileId, @SeriesId, @SourceEpisodeId, @Title, @Season, @EpisodeNumber, @PlotSummary, @StreamUrl)
                    """,
                    episodes.Select(e => new
                    {
                        ProfileId = profileId,
                        SeriesId = seriesId,
                        e.SourceEpisodeId,
                        e.Title,
                        e.Season,
                        e.EpisodeNumber,
                        e.PlotSummary,
                        e.StreamUrl
                    }),
                    transaction);
            }

            transaction.Commit();
        }, cancellationToken);

    public Task ClearProfileAsync(Guid profileId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute("DELETE FROM CachedEpisodes WHERE ProfileId = @profileId", new { profileId });
        }, cancellationToken);
}
