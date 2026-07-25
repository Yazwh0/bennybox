using CommunityToolkit.Mvvm.ComponentModel;
using Iptv.Core.Models;

namespace Iptv.App.ViewModels;

public partial class GuideRowViewModel : ObservableObject
{
    public Channel Channel { get; }
    public IReadOnlyList<EpgProgramme> Programmes { get; }
    public DateTime WindowStart { get; }
    public DateTime WindowEnd { get; }
    public DateTime NowUtc { get; }
    public double PixelsPerMinute { get; }
    public Action<Channel>? TuneRequested { get; set; }
    public Action<Channel, EpgProgramme>? CatchupRequested { get; set; }

    public string ChannelName => Channel.Name;
    public bool HasProgrammes => Programmes.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteIcon))]
    private bool _isFavorite;

    public string FavoriteIcon => IsFavorite ? "★" : "☆";

    public GuideRowViewModel(
        Channel channel,
        IReadOnlyList<EpgProgramme> programmes,
        DateTime windowStart,
        DateTime windowEnd,
        DateTime nowUtc,
        double pixelsPerMinute,
        bool isFavorite = false)
    {
        Channel = channel;
        Programmes = programmes;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
        NowUtc = nowUtc;
        PixelsPerMinute = pixelsPerMinute;
        _isFavorite = isFavorite;
    }
}
