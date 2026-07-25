namespace BitMagic.BennyBox.ViewModels;

// A row marker for the flattened, virtualized channel list - lets one ItemsControl render both
// category headers and channel rows via per-type DataTemplates instead of nesting a nested,
// non-virtualized ItemsControl per category (which was realizing all 15k+ channel rows at once).
public sealed class CategoryHeaderRow
{
    public string Name { get; }

    public CategoryHeaderRow(string name)
    {
        Name = name;
    }
}
