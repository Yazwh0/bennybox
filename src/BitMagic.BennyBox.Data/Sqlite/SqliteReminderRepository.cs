using Dapper;
using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Data.Sqlite;

public class SqliteReminderRepository : IReminderRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteReminderRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<IReadOnlyList<Reminder>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<Reminder>("SELECT * FROM Reminders ORDER BY StartUtc");
            return (IReadOnlyList<Reminder>)rows.ToList();
        }, cancellationToken);

    public Task<IReadOnlyList<Reminder>> GetDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = connection.Query<Reminder>(
                // A disabled profile's channels can't be tuned to, so its reminders don't pop up either.
                "SELECT * FROM Reminders WHERE StartUtc <= @nowUtc AND ProfileId IN (SELECT Id FROM Profiles WHERE IsEnabled = 1) ORDER BY StartUtc",
                new { nowUtc });
            return (IReadOnlyList<Reminder>)rows.ToList();
        }, cancellationToken);

    public Task AddAsync(Reminder reminder, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                """
                INSERT INTO Reminders (ProfileId, ChannelTvgId, StartUtc, EndUtc, ChannelName, ProgrammeTitle)
                VALUES (@ProfileId, @ChannelTvgId, @StartUtc, @EndUtc, @ChannelName, @ProgrammeTitle)
                ON CONFLICT (ProfileId, ChannelTvgId, StartUtc) DO UPDATE SET
                    EndUtc = excluded.EndUtc, ChannelName = excluded.ChannelName, ProgrammeTitle = excluded.ProgrammeTitle
                """, reminder);
        }, cancellationToken);

    public Task RemoveAsync(Guid profileId, string channelTvgId, DateTime startUtc, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(
                "DELETE FROM Reminders WHERE ProfileId = @profileId AND ChannelTvgId = @channelTvgId AND StartUtc = @startUtc",
                new { profileId, channelTvgId, startUtc });
        }, cancellationToken);
}
