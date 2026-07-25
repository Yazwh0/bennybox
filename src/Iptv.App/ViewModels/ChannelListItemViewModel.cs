using CommunityToolkit.Mvvm.ComponentModel;
using Iptv.Core.Models;

namespace Iptv.App.ViewModels;

public partial class ChannelListItemViewModel : ObservableObject
{
    public Channel Channel { get; }
    public string? NowTitle { get; }
    public bool HasNowTitle => !string.IsNullOrEmpty(NowTitle);
    public string Name => Channel.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteIcon))]
    private bool _isFavorite;

    public string FavoriteIcon => IsFavorite ? "★" : "☆";

    public ChannelListItemViewModel(Channel channel, string? nowTitle, bool isFavorite = false)
    {
        Channel = channel;
        NowTitle = nowTitle;
        _isFavorite = isFavorite;
    }
}
