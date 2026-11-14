using System;
using System.Globalization;
using Avalonia.Data.Converters;
using FluentAvalonia.UI.Controls;

namespace BitMagic.BennyBox.Android.Views;

// Helpers for SimpleHomeView (simple picture mode). Section names are FavoritesViewModel's own
// CategoryHeaderRow names, swapped for the plainest everyday word with an icon to match.
public static class SimpleModeConverters
{
    public static readonly IValueConverter SectionLabel = new FuncValueConverter<string?, string>(name => name switch
    {
        "Continue Watching" => "Keep watching",
        "Channels" => "TV",
        "Series" => "Shows",
        "Movies" => "Films",
        "Clips" => "Videos",
        _ => name ?? "",
    });

    public static readonly IValueConverter SectionIcon = new FuncValueConverter<string?, FASymbol>(name => name switch
    {
        "Continue Watching" => FASymbol.PlayFilled,
        "Channels" => FASymbol.Video,
        "Series" => FASymbol.List,
        "Movies" => FASymbol.Library,
        "Clips" => FASymbol.Flag,
        _ => FASymbol.Star,
    });

    // Up to two letters, from the first two words ("BBC One" -> "BO", "Alias" -> "A"), shown on a
    // coloured tile when something has no picture, so there's never a blank square.
    public static readonly IValueConverter Initials = new FuncValueConverter<string?, string>(name =>
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var letters = words.Take(2).Select(w => char.ToUpper(w[0], CultureInfo.CurrentCulture));
        return string.Concat(letters);
    });
}
