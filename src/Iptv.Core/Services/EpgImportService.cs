using Iptv.Core.Models;

namespace Iptv.Core.Services;

public class EpgImportService
{
    private readonly IEnumerable<IEpgSource> _sources;
    private readonly IEpgRepository _epgRepository;
    private readonly IProfileRepository _profileRepository;

    public EpgImportService(IEnumerable<IEpgSource> sources, IEpgRepository epgRepository, IProfileRepository profileRepository)
    {
        _sources = sources;
        _epgRepository = epgRepository;
        _profileRepository = profileRepository;
    }

    public async Task ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        if (profile.EpgSourceType == EpgSourceType.None)
        {
            return;
        }

        var source = _sources.FirstOrDefault(s => s.SourceType == profile.EpgSourceType)
            ?? throw new InvalidOperationException($"No EPG source registered for '{profile.EpgSourceType}'.");

        var fetchResult = await source.GetProgrammesAsync(profile, cancellationToken);

        // A 304 means the EPG feed hasn't changed since we last imported it - skip the (potentially
        // 100k+ row) delete+reinsert entirely.
        if (!fetchResult.NotModified)
        {
            await _epgRepository.ReplaceProgrammesAsync(profile.Id, fetchResult.Programmes!, cancellationToken);
        }

        var changed = false;
        if (fetchResult.ETag is not null)
        {
            profile.EpgETag = fetchResult.ETag;
            changed = true;
        }
        if (fetchResult.LastModified is not null)
        {
            profile.EpgLastModified = fetchResult.LastModified;
            changed = true;
        }
        if (changed)
        {
            await _profileRepository.UpdateAsync(profile, cancellationToken);
        }
    }
}
