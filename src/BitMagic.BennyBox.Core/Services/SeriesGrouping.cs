using BitMagic.BennyBox.Core.Models;

namespace BitMagic.BennyBox.Core.Services;

// Some sources list one show several times: a Jellyfin library with a folder per season can come back
// as one "series" per season, all with the same name, description and date. Shown as-is that's a row
// of identical entries with no way to tell which season each is. This folds those into one show, whose
// episodes are then gathered from every part (SeriesViewModel.SelectSeriesAsync).
//
// Only entries that are clearly the same show are merged: same name and either the same release date
// or the same description. Two different shows that happen to share a name (The Twilight Zone 1959
// and 2019) stay separate, and get their year added (Label) so they can be told apart.
public static class SeriesGrouping
{
    public sealed record Group(IReadOnlyList<Series> Parts, string Label)
    {
        public Series Primary => Parts[0];
    }

    // Input order is kept: each group sits where its first part was.
    public static IReadOnlyList<Group> Merge(IEnumerable<Series> series)
    {
        var clusters = new List<List<Series>>();
        var byName = new Dictionary<string, List<List<Series>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in series)
        {
            var key = item.Name.Trim();
            if (!byName.TryGetValue(key, out var sameName))
            {
                sameName = [];
                byName[key] = sameName;
            }

            var cluster = sameName.FirstOrDefault(c => c.Any(existing => IsSameShow(existing, item)));
            if (cluster is null)
            {
                cluster = [];
                sameName.Add(cluster);
                clusters.Add(cluster);
            }

            cluster.Add(item);
        }

        return clusters
            .Select(parts => new Group(parts, Label(parts[0], byName[parts[0].Name.Trim()].Count > 1)))
            .ToList();
    }

    private static bool IsSameShow(Series a, Series b) =>
        (!string.IsNullOrWhiteSpace(a.ReleaseDate) && a.ReleaseDate == b.ReleaseDate)
        || (!string.IsNullOrWhiteSpace(a.Plot) && a.Plot.Trim() == b.Plot?.Trim());

    private static string Label(Series series, bool nameIsShared)
    {
        var year = Year(series.ReleaseDate);
        return nameIsShared && year is not null && !series.Name.Contains($"({year})")
            ? $"{series.Name} ({year})"
            : series.Name;
    }

    private static string? Year(string? releaseDate) =>
        releaseDate is { Length: >= 4 } && int.TryParse(releaseDate[..4], out var year) && year > 1800
            ? year.ToString()
            : null;
}
