using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Messages;

// Sent when a series is selected from a page other than Series itself (currently: Favorites) - lets
// MainWindowViewModel both switch to the Series page and open that series' episode list, without
// FavoritesViewModel needing a direct reference to SeriesViewModel or MainWindowViewModel.
public sealed class OpenSeriesMessage
{
    public Series Series { get; }
    public string SourceName { get; }

    public OpenSeriesMessage(Series series, string sourceName)
    {
        Series = series;
        SourceName = sourceName;
    }
}
