using Iptv.Core.Models;

namespace Iptv.App.ViewModels;

public class SeriesListItemViewModel
{
    public Series Series { get; }
    public string Name => Series.Name;
    public string? CoverUrl => Series.CoverUrl;

    public SeriesListItemViewModel(Series series)
    {
        Series = series;
    }
}
