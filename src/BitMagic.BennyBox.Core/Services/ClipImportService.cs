using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

public class ClipImportService
{
    private readonly IEnumerable<IClipSource> _sources;
    private readonly IClipRepository _clipRepository;

    public ClipImportService(IEnumerable<IClipSource> sources, IClipRepository clipRepository)
    {
        _sources = sources;
        _clipRepository = clipRepository;
    }

    // Unlike PlaylistImportService, a missing source is not an error here - most profile source
    // types (e.g. Xtream, M3U) simply have no clips to import, so this is a normal no-op for them.
    public async Task<ClipImportResult?> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.SourceType == profile.SourceType);
        if (source is null)
        {
            return null;
        }

        var result = await source.ImportAsync(profile, cancellationToken);

        if (!result.NotModified)
        {
            await _clipRepository.ReplaceClipsAsync(profile.Id, result.Categories, result.Clips, cancellationToken);
        }

        return result;
    }

    public async Task<MovieDetails?> GetDetailsAsync(ProfileSource profile, Movie clip, CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.SourceType == profile.SourceType);
        return source is null ? null : await source.GetDetailsAsync(profile, clip, cancellationToken);
    }
}
