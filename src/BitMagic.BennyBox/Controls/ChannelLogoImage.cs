using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BitMagic.BennyBox.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BitMagic.BennyBox.Controls;

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

        if (string.IsNullOrEmpty(url))
        {
            Source = null;
            return;
        }

        var cache = _cache ??= App.Services?.GetService<IChannelLogoCache>();
        if (cache is null)
        {
            Source = null;
            return;
        }

        // A list refresh (see LiveTvViewModel.ApplyFilter et al) rebuilds every row - even ones whose
        // data hasn't changed - which recreates this control and re-triggers this whole lookup for a
        // logo that's almost always already sitting in the cache's in-memory dictionary. Blanking
        // Source and deferring the reload via Dispatcher.Post (even for an already-completed task)
        // meant every refresh flickered every visible logo to blank for a frame. Setting it inline
        // for an already-resolved task avoids that entirely; only a genuine cache miss (a real fetch
        // in flight) still needs the null-while-loading + deferred-set path below.
        var pending = cache.GetLogoAsync(url);
        if (pending.IsCompletedSuccessfully)
        {
            Source = pending.Result;
            return;
        }

        Source = null;
        _ = LoadAsync(pending, version);
    }

    private async Task LoadAsync(Task<Bitmap?> pending, int version)
    {
        var bitmap = await pending;

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
