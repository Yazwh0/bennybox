using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

public class PlaylistImportService
{
    private readonly IEnumerable<IChannelSource> _sources;
    private readonly IChannelRepository _channelRepository;
    private readonly IProfileRepository _profileRepository;

    public PlaylistImportService(
        IEnumerable<IChannelSource> sources,
        IChannelRepository channelRepository,
        IProfileRepository profileRepository)
    {
        _sources = sources;
        _channelRepository = channelRepository;
        _profileRepository = profileRepository;
    }

    // Unlike the original Xtream/M3U-only version of this method, a missing source is not an error -
    // LocalFolder/Sftp profiles have no channels by design (they're Movies- or Series-only, see
    // MediaKind), the same "no equivalent source" case SeriesImportService/MovieImportService already
    // treat as a normal no-op rather than a refresh failure.
    public async Task<ChannelImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.SourceType == profile.SourceType);
        if (source is null)
        {
            return new ChannelImportResult([], [], NotModified: true);
        }

        var result = await source.ImportAsync(profile, cancellationToken);

        // A 304 from the server means the DB already reflects the latest data - skip the (potentially
        // large) delete+reinsert entirely, we only need to record that we checked.
        if (!result.NotModified)
        {
            await _channelRepository.ReplaceChannelsAsync(profile.Id, result.Categories, result.Channels, cancellationToken);
        }

        profile.LastRefreshedUtc = DateTime.UtcNow;
        if (result.ETag is not null)
        {
            profile.PlaylistETag = result.ETag;
        }
        if (result.LastModified is not null)
        {
            profile.PlaylistLastModified = result.LastModified;
        }
        await _profileRepository.UpdateAsync(profile, cancellationToken);

        return result;
    }
}
