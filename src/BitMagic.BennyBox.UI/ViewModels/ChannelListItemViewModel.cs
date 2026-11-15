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

    // Now and next from the guide, when the channel has guide data: how far through the current
    // programme it is (for a thin bar under it) and what's on after it, so "what's on" is answered
    // on the channel list without opening the Guide.
    public EpgProgramme? Now { get; }
    public EpgProgramme? Next { get; }

    public double? NowProgress => Now is { } now && now.EndUtc > now.StartUtc
        ? Math.Clamp((DateTime.UtcNow - now.StartUtc) / (now.EndUtc - now.StartUtc), 0, 1)
        : null;

    public bool HasNowProgress => NowProgress is not null;

    public string? NextLine => Next is { } next ? $"Next: {next.StartUtc.ToLocalTime():HH:mm} {next.Title}" : null;
    public bool HasNextLine => Next is not null;

    // The guide moves on; a page showing these calls this now and then to move the bars and lines on.
    public void RefreshTimes()
    {
        OnPropertyChanged(nameof(NowProgress));
        OnPropertyChanged(nameof(HasNowProgress));
    }

    public ChannelListItemViewModel(Channel channel, string? nowTitle, bool isFavorite = false, string? fallbackLogoUrl = null,
        EpgProgramme? now = null, EpgProgramme? next = null)
    {
        Channel = channel;
        NowTitle = nowTitle;
        _isFavorite = isFavorite;
        FallbackLogoUrl = fallbackLogoUrl;
        Now = now;
        Next = next;
    }
}
