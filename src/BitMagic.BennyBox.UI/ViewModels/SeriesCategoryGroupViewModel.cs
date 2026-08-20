namespace BitMagic.BennyBox.ViewModels;

public class SeriesCategoryGroupViewModel
{
    public string Name { get; }
    public IReadOnlyList<SeriesListItemViewModel> Series { get; }

    public SeriesCategoryGroupViewModel(string name, IReadOnlyList<SeriesListItemViewModel> series)
    {
        Name = name;
        Series = series;
    }
}
