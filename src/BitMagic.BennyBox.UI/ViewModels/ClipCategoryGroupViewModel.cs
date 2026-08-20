namespace BitMagic.BennyBox.ViewModels;

public class ClipCategoryGroupViewModel
{
    public string Name { get; }
    public IReadOnlyList<ClipListItemViewModel> Clips { get; }

    public ClipCategoryGroupViewModel(string name, IReadOnlyList<ClipListItemViewModel> clips)
    {
        Name = name;
        Clips = clips;
    }
}
