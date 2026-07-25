using Iptv.Core.Models;

namespace Iptv.App.ViewModels;

public class GuideRowViewModel
{
    public Channel Channel { get; }
    public IReadOnlyList<EpgProgramme> Programmes { get; }
    public DateTime WindowStart { get; }
    public DateTime WindowEnd { get; }
    public DateTime NowUtc { get; }
    public double PixelsPerMinute { get; }
    public Action<Channel>? TuneRequested { get; set; }

    public string ChannelName => Channel.Name;

    public GuideRowViewModel(
        Channel channel,
        IReadOnlyList<EpgProgramme> programmes,
        DateTime windowStart,
        DateTime windowEnd,
        DateTime nowUtc,
        double pixelsPerMinute)
    {
        Channel = channel;
        Programmes = programmes;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
        NowUtc = nowUtc;
        PixelsPerMinute = pixelsPerMinute;
    }
}
