using CommunityToolkit.Mvvm.ComponentModel;
using Iptv.Core.Models;

namespace Iptv.App.ViewModels;

public partial class SeriesListItemViewModel : ObservableObject
{
    public Series Series { get; }
    public string Name => Series.Name;
    public string? CoverUrl => Series.CoverUrl;
    public string? Plot => Series.Plot;
    public bool HasPlot => !string.IsNullOrWhiteSpace(Plot);

    // Genre/year/rating condensed into a single "Genre  •  Year  •  ★ Rating" line for the detail
    // header - null (and hidden) if the provider gave us none of these.
    public string? MetaLine { get; }
    public bool HasMetaLine => !string.IsNullOrEmpty(MetaLine);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteIcon))]
    private bool _isFavorite;

    public string FavoriteIcon => IsFavorite ? "★" : "☆";

    public SeriesListItemViewModel(Series series, bool isFavorite = false)
    {
        Series = series;
        MetaLine = BuildMetaLine(series);
        _isFavorite = isFavorite;
    }

    private static string? BuildMetaLine(Series series)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(series.Genre))
        {
            parts.Add(series.Genre);
        }
        if (!string.IsNullOrWhiteSpace(series.ReleaseDate))
        {
            parts.Add(series.ReleaseDate);
        }
        if (series.Rating is > 0)
        {
            parts.Add($"★ {series.Rating:0.0}");
        }

        return parts.Count > 0 ? string.Join("   •   ", parts) : null;
    }
}
