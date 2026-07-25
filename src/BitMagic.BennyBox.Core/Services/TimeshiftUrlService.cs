using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

public class TimeshiftUrlService
{
    private readonly IEnumerable<IChannelSource> _sources;
    private readonly IProfileRepository _profileRepository;

    public TimeshiftUrlService(IEnumerable<IChannelSource> sources, IProfileRepository profileRepository)
    {
        _sources = sources;
        _profileRepository = profileRepository;
    }

    public async Task<string?> BuildTimeshiftUrlAsync(Channel channel, DateTime startUtc, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var profile = await _profileRepository.GetByIdAsync(channel.ProfileId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var source = _sources.FirstOrDefault(s => s.SourceType == profile.SourceType);
        return source?.BuildTimeshiftUrl(profile, channel, startUtc, duration);
    }
}
