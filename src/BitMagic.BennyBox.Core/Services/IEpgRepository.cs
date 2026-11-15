using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

public sealed record EpgNowNext(EpgProgramme? Now, EpgProgramme? Next);

public interface IEpgRepository
{
    Task ReplaceProgrammesAsync(Guid profileId, IAsyncEnumerable<EpgProgramme> programmes, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, EpgNowNext>> GetNowNextAsync(Guid profileId, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EpgProgramme>> GetProgrammesInRangeAsync(Guid profileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    // One channel's programmes overlapping a time range, in start order - for a channel's own schedule page.
    Task<IReadOnlyList<EpgProgramme>> GetChannelProgrammesAsync(Guid profileId, string channelTvgId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    // When a source's guide runs out (its last programme's end), or null with no guide at all.
    Task<DateTime?> GetLatestProgrammeEndAsync(Guid profileId, CancellationToken cancellationToken = default);
}
