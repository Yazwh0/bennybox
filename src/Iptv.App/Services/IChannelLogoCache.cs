using Avalonia.Media.Imaging;

namespace Iptv.App.Services;

public interface IChannelLogoCache
{
    // Returns null if logoUrl is null/empty, or if the download/decode fails - callers should treat
    // null as "show no logo" rather than an error.
    Task<Bitmap?> GetLogoAsync(string? logoUrl);
}
