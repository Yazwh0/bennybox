using Dapper;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Data.Sqlite;

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

    public Task<IReadOnlySet<Guid>> GetFavoriteSeriesIdsAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var ids = connection.Query<Guid>("SELECT SeriesId FROM SeriesFavorites");
            return (IReadOnlySet<Guid>)ids.ToHashSet();
        }, cancellationToken);

    public Task AddSeriesAsync(Guid profileId, Guid seriesId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "INSERT OR REPLACE INTO SeriesFavorites (SeriesId, ProfileId, AddedUtc) VALUES (@seriesId, @profileId, @addedUtc)",
                new { seriesId, profileId, addedUtc = DateTime.UtcNow });
        }, cancellationToken);

    public Task RemoveSeriesAsync(Guid seriesId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute("DELETE FROM SeriesFavorites WHERE SeriesId = @seriesId", new { seriesId });
        }, cancellationToken);

    public Task<IReadOnlySet<Guid>> GetFavoriteMovieIdsAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var ids = connection.Query<Guid>("SELECT MovieId FROM MovieFavorites");
            return (IReadOnlySet<Guid>)ids.ToHashSet();
        }, cancellationToken);

    public Task AddMovieAsync(Guid profileId, Guid movieId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "INSERT OR REPLACE INTO MovieFavorites (MovieId, ProfileId, AddedUtc) VALUES (@movieId, @profileId, @addedUtc)",
                new { movieId, profileId, addedUtc = DateTime.UtcNow });
        }, cancellationToken);

    public Task RemoveMovieAsync(Guid movieId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute("DELETE FROM MovieFavorites WHERE MovieId = @movieId", new { movieId });
        }, cancellationToken);

    public Task<IReadOnlySet<Guid>> GetFavoriteClipIdsAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var ids = connection.Query<Guid>("SELECT ClipId FROM ClipFavorites");
            return (IReadOnlySet<Guid>)ids.ToHashSet();
        }, cancellationToken);

    public Task AddClipAsync(Guid profileId, Guid clipId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "INSERT OR REPLACE INTO ClipFavorites (ClipId, ProfileId, AddedUtc) VALUES (@clipId, @profileId, @addedUtc)",
                new { clipId, profileId, addedUtc = DateTime.UtcNow });
        }, cancellationToken);

    public Task RemoveClipAsync(Guid clipId, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute("DELETE FROM ClipFavorites WHERE ClipId = @clipId", new { clipId });
        }, cancellationToken);
}
