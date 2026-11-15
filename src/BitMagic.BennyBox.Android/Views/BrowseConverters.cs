using Avalonia.Data.Converters;
using FluentAvalonia.UI.Controls;

namespace BitMagic.BennyBox.Android.Views;

// Small converters for the normal-mode browse pages (Shows, Films): the filter chips and posters.
public static class BrowseConverters
{
    // A filter chip shows as "selected" (filled) when it's narrowing the list - anything other than
    // the "All Categories" / "All Sources" choice.
    public static readonly IValueConverter IsFilterActive = new FuncValueConverter<string?, bool>(value =>
        value is not null && !value.StartsWith("All ", StringComparison.Ordinal));

    // The same picture a category gets in simple mode (SimplePictures.ForCategory), so the two modes
    // look related; "All ..." gets Home, anything unmatched the Shows picture.
    public static readonly IValueConverter CategoryPicture = new FuncValueConverter<string?, SimplePicture>(name =>
        name is null || name.StartsWith("All ", StringComparison.Ordinal) ? SimplePicture.Home
        : SimplePictures.ForCategory(name) ?? SimplePicture.Shows);

    // The detail header's "Genre • Year • ★ 7.0" line: the phone's font has no ★, which showed as a box.
    public static readonly IValueConverter MetaLine = new FuncValueConverter<string?, string?>(line =>
        line?.Replace("★ ", "Rated ", StringComparison.Ordinal));

    public static readonly IValueConverter FavouriteMenuText = new FuncValueConverter<bool, string>(favourite =>
        favourite ? "Remove from favourites" : "Add to favourites");

    public static readonly IValueConverter FavouriteMenuIcon = new FuncValueConverter<bool, FASymbol>(favourite =>
        favourite ? FASymbol.Star : FASymbol.StarFilled);
}
