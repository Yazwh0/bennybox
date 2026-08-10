namespace BitMagic.BennyBox.ViewModels;

// Like CategoryHeaderRow, but for SeriesViewModel.Episodes specifically - carries the season number
// too, so its row template can offer a "download this season" action next to the label.
public sealed class SeasonHeaderRow
{
    public string Name { get; }
    public int Season { get; }

    public SeasonHeaderRow(string name, int season)
    {
        Name = name;
        Season = season;
    }
}
