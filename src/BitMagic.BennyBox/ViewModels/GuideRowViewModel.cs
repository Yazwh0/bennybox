using CommunityToolkit.Mvvm.ComponentModel;
using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.ViewModels;

public partial class GuideRowViewModel : ObservableObject
{
    public Channel Channel { get; }
    public IReadOnlyList<ProgrammeViewModel> Programmes { get; }
    public DateTime WindowStart { get; }
    public DateTime WindowEnd { get; }
    public DateTime NowUtc { get; }
    public double PixelsPerMinute { get; }
    public Action<Channel>? TuneRequested { get; set; }
    public Action<Channel, EpgProgramme>? CatchupRequested { get; set; }
    public Action<Channel, ProgrammeViewModel>? ReminderToggleRequested { get; set; }

    public string ChannelName => Channel.Name;
    public bool HasProgrammes => Programmes.Count > 0;

    // See ChannelListItemViewModel.FallbackLogoUrl - same rationale.
    public string? FallbackLogoUrl { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteIcon))]
    private bool _isFavorite;

    public string FavoriteIcon => IsFavorite ? "★" : "☆";

    public GuideRowViewModel(
        Channel channel,
        IReadOnlyList<ProgrammeViewModel> programmes,
        DateTime windowStart,
        DateTime windowEnd,
        DateTime nowUtc,
        double pixelsPerMinute,
        bool isFavorite = false,
        string? fallbackLogoUrl = null)
    {
        Channel = channel;
        Programmes = programmes;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
        NowUtc = nowUtc;
        PixelsPerMinute = pixelsPerMinute;
        _isFavorite = isFavorite;
        FallbackLogoUrl = fallbackLogoUrl;
    }
}
