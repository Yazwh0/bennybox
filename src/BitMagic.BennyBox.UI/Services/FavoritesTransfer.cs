using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.UI.Services;

// Turns this device's favourites into device-independent SharedFavorites for a setup code, and back
// again on the receiving device (see ProfileSetupCode). Matching is by account (AccountKey) plus the
// provider's own item id, so it only works for content the receiving device has already imported
// from the same account - favourites for anything else are counted as not found.
public sealed class FavoritesTransfer(
    IProfileRepository profileRepository,
    IChannelRepository channelRepository,
    ISeriesRepository seriesRepository,
    IMovieRepository movieRepository,
    IClipRepository clipRepository,
    IFavoriteRepository favoriteRepository)
{
    public async Task<IReadOnlyList<SharedFavorite>> ExportAsync()
    {
        var channelIds = await favoriteRepository.GetFavoriteChannelIdsAsync();
        var seriesIds = await favoriteRepository.GetFavoriteSeriesIdsAsync();
        var movieIds = await favoriteRepository.GetFavoriteMovieIdsAsync();
        var clipIds = await favoriteRepository.GetFavoriteClipIdsAsync();

        var result = new List<SharedFavorite>();
        foreach (var profile in (await profileRepository.GetAllAsync()).Where(ProfileSetupCode.CanShare))
        {
            var account = ProfileSetupCode.AccountKey(profile);

            if (channelIds.Count > 0)
            {
                result.AddRange((await channelRepository.GetChannelsAsync(profile.Id))
                    .Where(c => channelIds.Contains(c.Id))
                    .Select(c => new SharedFavorite(account, FavoriteKind.Channel, c.SourceChannelId)));
            }

            if (seriesIds.Count > 0)
            {
                result.AddRange((await seriesRepository.GetSeriesAsync(profile.Id))
                    .Where(s => seriesIds.Contains(s.Id))
                    .Select(s => new SharedFavorite(account, FavoriteKind.Series, s.SourceSeriesId)));
            }

            if (movieIds.Count > 0)
            {
                result.AddRange((await movieRepository.GetMoviesAsync(profile.Id))
                    .Where(m => movieIds.Contains(m.Id))
                    .Select(m => new SharedFavorite(account, FavoriteKind.Movie, m.SourceMovieId)));
            }

            if (clipIds.Count > 0)
            {
                result.AddRange((await clipRepository.GetClipsAsync(profile.Id))
                    .Where(c => clipIds.Contains(c.Id))
                    .Select(c => new SharedFavorite(account, FavoriteKind.Clip, c.SourceMovieId)));
            }
        }

        return result;
    }

    // Adds whichever favourites match content on this device. Returns how many were added and how many
    // couldn't be matched (account not set up here, or the item no longer exists).
    public async Task<(int Added, int NotFound)> ImportAsync(IReadOnlyList<SharedFavorite> favorites)
    {
        if (favorites.Count == 0)
        {
            return (0, 0);
        }

        var added = 0;
        var profiles = await profileRepository.GetAllAsync();
        foreach (var group in favorites.GroupBy(f => f.AccountKey))
        {
            var profile = profiles.FirstOrDefault(p => ProfileSetupCode.AccountKey(p) == group.Key);
            if (profile is null)
            {
                continue;
            }

            foreach (var kindGroup in group.GroupBy(f => f.Kind))
            {
                var wanted = kindGroup.Select(f => f.SourceId).ToHashSet(StringComparer.Ordinal);
                switch (kindGroup.Key)
                {
                    case FavoriteKind.Channel:
                        foreach (var channel in (await channelRepository.GetChannelsAsync(profile.Id)).Where(c => wanted.Remove(c.SourceChannelId)))
                        {
                            await favoriteRepository.AddAsync(profile.Id, channel.Id);
                            added++;
                        }
                        break;
                    case FavoriteKind.Series:
                        foreach (var series in (await seriesRepository.GetSeriesAsync(profile.Id)).Where(s => wanted.Remove(s.SourceSeriesId)))
                        {
                            await favoriteRepository.AddSeriesAsync(profile.Id, series.Id);
                            added++;
                        }
                        break;
                    case FavoriteKind.Movie:
                        foreach (var movie in (await movieRepository.GetMoviesAsync(profile.Id)).Where(m => wanted.Remove(m.SourceMovieId)))
                        {
                            await favoriteRepository.AddMovieAsync(profile.Id, movie.Id);
                            added++;
                        }
                        break;
                    case FavoriteKind.Clip:
                        foreach (var clip in (await clipRepository.GetClipsAsync(profile.Id)).Where(c => wanted.Remove(c.SourceMovieId)))
                        {
                            await favoriteRepository.AddClipAsync(profile.Id, clip.Id);
                            added++;
                        }
                        break;
                }
            }
        }

        return (added, favorites.Count - added);
    }
}
