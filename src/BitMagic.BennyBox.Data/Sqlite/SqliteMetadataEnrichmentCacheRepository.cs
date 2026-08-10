using Dapper;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Data.Sqlite;

public class SqliteMetadataEnrichmentCacheRepository : IMetadataEnrichmentCacheRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteMetadataEnrichmentCacheRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private sealed class Row
    {
        public bool Found { get; set; }
        public string? Plot { get; set; }
        public string? Genre { get; set; }
        public string? ReleaseDate { get; set; }
        public string? PosterUrl { get; set; }
        public double? Rating { get; set; }
    }

    // Microsoft.Data.Sqlite has no real async I/O - its "Async" methods run fully synchronously
    // on the calling thread. Task.Run is the documented way to keep SQLite work off the UI thread.
    public Task<CachedEnrichment?> GetAsync(string mediaType, string lookupKey, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var row = connection.QuerySingleOrDefault<Row>(
                """
                SELECT Found, Plot, Genre, ReleaseDate, PosterUrl, Rating
                FROM MetadataEnrichmentCache WHERE MediaType = @mediaType AND LookupKey = @lookupKey
                """,
                new { mediaType, lookupKey });

            if (row is null)
            {
                return (CachedEnrichment?)null;
            }

            var metadata = row.Found ? new EnrichedMetadata(row.Plot, row.Genre, row.ReleaseDate, row.PosterUrl, row.Rating) : null;
            return new CachedEnrichment(row.Found, metadata);
        }, cancellationToken);

    public Task SetAsync(string mediaType, string lookupKey, EnrichedMetadata? metadata, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                """
                INSERT INTO MetadataEnrichmentCache (MediaType, LookupKey, Found, Plot, Genre, ReleaseDate, PosterUrl, Rating)
                VALUES (@MediaType, @LookupKey, @Found, @Plot, @Genre, @ReleaseDate, @PosterUrl, @Rating)
                ON CONFLICT (MediaType, LookupKey) DO UPDATE SET
                    Found = excluded.Found, Plot = excluded.Plot, Genre = excluded.Genre,
                    ReleaseDate = excluded.ReleaseDate, PosterUrl = excluded.PosterUrl, Rating = excluded.Rating
                """,
                new
                {
                    MediaType = mediaType,
                    LookupKey = lookupKey,
                    Found = metadata is not null,
                    metadata?.Plot,
                    metadata?.Genre,
                    metadata?.ReleaseDate,
                    metadata?.PosterUrl,
                    metadata?.Rating
                });
        }, cancellationToken);
}
