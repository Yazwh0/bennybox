namespace BitMagic.BennyBox.ViewModels;

public class CategoryGroupViewModel
{
    public string Name { get; }
    public IReadOnlyList<ChannelListItemViewModel> Channels { get; }

    public CategoryGroupViewModel(string name, IReadOnlyList<ChannelListItemViewModel> channels)
    {
        Name = name;
        Channels = channels;
    }
}
