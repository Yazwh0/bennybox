using Avalonia.Media.Imaging;

namespace BitMagic.BennyBox.Services;

public interface IChannelLogoCache
{
    // Returns null if logoUrl is null/empty, or if the download/decode fails - callers should treat
    // null as "show no logo" rather than an error. If logoUrl fails (missing, broken link, or a
    // known dead-image placeholder) and fallbackLogoUrl is given, that's tried next. If that also
    // fails and tmdbFallbackTitle is given, a TMDb TV search for that title is tried last - see
    // ChannelLogoCache for why this one's deliberately lazy (per-row, only for what's actually
    // rendered) rather than a batch pass over the whole channel list.
    Task<Bitmap?> GetLogoAsync(string? logoUrl, string? fallbackLogoUrl = null, string? tmdbFallbackTitle = null);
}
