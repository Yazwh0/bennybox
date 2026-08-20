using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.ViewModels;

// Shared by LiveTvViewModel/GuideViewModel/FavoritesViewModel/SearchViewModel - a Live TV channel's
// own logo (from the IPTV provider's channel list) is a completely separate, independently-curated
// piece of data from the VOD Series catalog, even when a channel is just a "24/7 [Show]" rewind of
// that exact series - so a broken/dead channel logo (e.g. an i.imgur.com link Imgur has since
// deleted, which ChannelLogoCache already detects and refuses to show) has no automatic relationship
// to the show's real poster. This gives ChannelListItemViewModel/GuideRowViewModel a same-named
// Series' CoverUrl to fall back to, purely by matching on title.
//
// Series only, deliberately not Movies: channel names match Series titles cleanly with a plain
// case-insensitive/trimmed exact match (e.g. "The Mandalorian" -> Series "The Mandalorian"), but the
// Movies catalog's titles all carry a trailing " - YYYY"/"(YYYY)" release year, and matching after
// stripping that produces real false positives - single-word channel brand names collide with
// unrelated movies of the same title (e.g. channel "Quest" vs. the film "Quest - 2017", channel "MAX"
// vs. "Max - 2015"). Showing a wrong poster is worse than showing none, so Movies are out of scope.
public static class ChannelLogoFallbackSupport
{
    // Builds once per load pass across every profile's Series - a channel and the show it loops
    // aren't guaranteed to share a ProfileId (e.g. one profile's Live TV list, another profile's VOD
    // catalog), so the index isn't scoped per-profile. Keeps the first non-empty CoverUrl seen for a
    // given title if the same show appears more than once (e.g. duplicated across profiles).
    public static Dictionary<string, string?> BuildSeriesCoverIndex(IEnumerable<Series> allSeries)
    {
        var index = new Dictionary<string, string?>();
        foreach (var series in allSeries)
        {
            var key = DownloadRowSupport.Normalize(series.Name);
            if (!index.TryGetValue(key, out var existing) || string.IsNullOrEmpty(existing))
            {
                index[key] = series.CoverUrl;
            }
        }

        return index;
    }

    public static string? FindFallback(IReadOnlyDictionary<string, string?> seriesCoverIndex, string channelName) =>
        seriesCoverIndex.TryGetValue(DownloadRowSupport.Normalize(channelName), out var coverUrl) ? coverUrl : null;
}
