using CommunityToolkit.Mvvm.ComponentModel;
using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.ViewModels;

public partial class ChannelListItemViewModel : ObservableObject
{
    public Channel Channel { get; }

    // True when this stream didn't play the last time it was tried (see StreamHealth) - shown as a
    // warning so dead streams can be skipped without tapping them. RefreshHealth re-reads it after
    // a StreamHealthChangedMessage.
    public bool DidNotWork => BitMagic.BennyBox.UI.Services.StreamHealth.Current?.DidNotWork(Channel.StreamUrl) ?? false;

    public void RefreshHealth() => OnPropertyChanged(nameof(DidNotWork));

    public string? NowTitle { get; }
    public bool HasNowTitle => !string.IsNullOrEmpty(NowTitle);
    public string Name => Channel.Name;

    // A same-named Series' CoverUrl, if any - see ChannelLogoFallbackSupport. ChannelLogoImage tries
    // Channel.LogoUrl first and only falls back to this if that one's missing/dead.
    public string? FallbackLogoUrl { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteIcon))]
    private bool _isFavorite;

    public string FavoriteIcon => IsFavorite ? "★" : "☆";

    public ChannelListItemViewModel(Channel channel, string? nowTitle, bool isFavorite = false, string? fallbackLogoUrl = null)
    {
        Channel = channel;
        NowTitle = nowTitle;
        _isFavorite = isFavorite;
        FallbackLogoUrl = fallbackLogoUrl;
    }
}
