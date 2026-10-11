using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Sources.Jellyfin;

// Whichever libraries the profile assigned to Clips - by default home videos, music videos, and
// mixed-content libraries. See JellyfinLibraryReader/JellyfinLibraryMapping.
public class JellyfinClipSource : IClipSource
{
    private readonly JellyfinClient _client;

    public JellyfinClipSource(JellyfinClient client)
    {
        _client = client;
    }

    public ProfileSourceType SourceType => ProfileSourceType.Jellyfin;

    public async Task<ClipImportResult> ImportAsync(ProfileSource profile, CancellationToken cancellationToken = default)
    {
        var (categories, items, session) = await JellyfinLibraryReader.ReadAsync(_client, profile, JellyfinLibraryTarget.Clips, cancellationToken);
        var clips = items.Select(x => JellyfinLibraryReader.ToMovie(x.Item, x.CategoryId, profile, session)).ToList();
        return new ClipImportResult(categories, clips);
    }

    public Task<MovieDetails?> GetDetailsAsync(ProfileSource profile, Movie clip, CancellationToken cancellationToken = default) =>
        JellyfinMovieSource.GetItemDetailsAsync(_client, profile, clip, cancellationToken);
}
