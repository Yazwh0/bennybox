using Iptv.Core.Models;

namespace Iptv.Core.Services;

public sealed record EpgNowNext(EpgProgramme? Now, EpgProgramme? Next);

public interface IEpgRepository
{
    Task ReplaceProgrammesAsync(Guid profileId, IAsyncEnumerable<EpgProgramme> programmes, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, EpgNowNext>> GetNowNextAsync(Guid profileId, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EpgProgramme>> GetProgrammesInRangeAsync(Guid profileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
}
