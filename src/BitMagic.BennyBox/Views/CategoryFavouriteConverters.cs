using System.Globalization;
using Avalonia.Data.Converters;
using BitMagic.BennyBox.UI.Services;

namespace BitMagic.BennyBox.Views;

// For the category drop-downs (see CategoryFavouriteButton). The ConverterParameter is the page's place
// ("Tv", "Films", "Shows", "Videos").
public static class CategoryFavouriteConverters
{
    // A star in front of a favourite category's name. Bound to [the name, the star button's Content]: the
    // second value isn't used, it's only there so the labels update the moment the star is pressed.
    public static readonly IMultiValueConverter Label = new Labelling();

    private sealed class Labelling : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count == 0 || values[0] is not string name)
            {
                return null;
            }

            var place = Enum.TryParse<CategoryPlace>(parameter as string, out var p) ? p : CategoryPlace.Tv;
            return CategoryFavourites.Current?.IsFavourite(place, name) == true ? $"★ {name}" : name;
        }
    }
}
