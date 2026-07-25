using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Iptv.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Iptv.App.Controls;

// A channel's logo needs to load lazily, only for rows the virtualizing panel actually realizes -
// with 15k+ channels, eagerly loading a bitmap per row VM at construction time (before any of them
// are ever scrolled into view) would mean thousands of downloads on every list load. Binding LogoUrl
// here instead works because Avalonia re-evaluates bindings (firing LogoUrlProperty's change
// notification) both when a container is newly realized AND when a virtualizing panel recycles an
// existing container to a different row - so this fires exactly when, and only when, a row becomes
// (or stays) visible.
public class ChannelLogoImage : Image
{
    public static readonly StyledProperty<string?> LogoUrlProperty =
        AvaloniaProperty.Register<ChannelLogoImage, string?>(nameof(LogoUrl));

    public string? LogoUrl
    {
        get => GetValue(LogoUrlProperty);
        set => SetValue(LogoUrlProperty, value);
    }

    // Resolved once from the app's DI container and reused - IChannelLogoCache is registered as a
    // singleton, so there's exactly one instance app-wide regardless of how many rows resolve it.
    private static IChannelLogoCache? _cache;

    // Guards against a stale async continuation setting Source after this container has already been
    // recycled to a different row (or the same row's LogoUrl changed again) while the fetch for the
    // previous URL was still in flight.
    private int _requestVersion;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LogoUrlProperty)
        {
            OnLogoUrlChanged(change.GetNewValue<string?>());
        }
    }

    private void OnLogoUrlChanged(string? url)
    {
        var version = ++_requestVersion;
        Source = null;

        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        var cache = _cache ??= App.Services?.GetService<IChannelLogoCache>();
        if (cache is null)
        {
            return;
        }

        _ = LoadAsync(cache, url, version);
    }

    private async Task LoadAsync(IChannelLogoCache cache, string url, int version)
    {
        var bitmap = await cache.GetLogoAsync(url);

        if (version != _requestVersion)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (version == _requestVersion)
            {
                Source = bitmap;
            }
        });
    }
}
