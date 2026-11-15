namespace BitMagic.BennyBox.Android.Views;

// Sizing for the normal-mode poster grids (Shows, Films): as many columns as fit at least MinTile
// wide, never fewer than 3, so a phone shows 3 across and a tablet 6 or more. Posters are 2:3.
public static class PosterGrid
{
    public const double Gap = 8;
    private const double MinTile = 110;
    private const int MinColumns = 3;

    public readonly record struct Layout(int Columns, double TileWidth, double PosterHeight);

    // For lists of rows (Live TV): as many columns as fit at least minWidth each, at least one.
    public static (int Columns, double ColumnWidth) FitRows(double width, double minWidth)
    {
        width = Math.Max(width, 0);
        var columns = Math.Max(1, (int)((width + Gap) / (minWidth + Gap)));
        return (columns, Math.Floor((width - Gap * (columns - 1)) / columns));
    }

    public static Layout Fit(double width)
    {
        width = Math.Max(width, MinTile * MinColumns);
        var columns = Math.Max(MinColumns, (int)((width + Gap) / (MinTile + Gap)));
        var tile = Math.Floor((width - Gap * (columns - 1)) / columns);
        return new Layout(columns, tile, Math.Floor(tile * 1.5));
    }
}
