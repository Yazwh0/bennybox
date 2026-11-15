using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BitMagic.BennyBox.Android.Views;

public static class SimpleModeConverters
{
    // The word under the simple player's play/pause button: what pressing it will do.
    public static readonly IValueConverter PlayPauseLabel = new FuncValueConverter<bool, string>(paused => paused ? "Play" : "Pause");

    // The simple player's fullscreen button: "Big" to go fullscreen, "Small" to come back out.
    public static readonly IValueConverter FullscreenLabel = new FuncValueConverter<bool, string>(full => full ? "Small" : "Big");

    public static readonly IValueConverter FullscreenIcon = new FuncValueConverter<bool, FluentAvalonia.UI.Controls.FASymbol>(full =>
        full ? FluentAvalonia.UI.Controls.FASymbol.FullScreenMinimize : FluentAvalonia.UI.Controls.FASymbol.FullScreenMaximize);

    public static readonly IValueConverter PlayPausePicture = new FuncValueConverter<bool, SimplePicture>(paused => paused ? SimplePicture.Play : SimplePicture.Pause);

    public static readonly IValueConverter FullscreenPicture = new FuncValueConverter<bool, SimplePicture>(full => full ? SimplePicture.Small : SimplePicture.Big);

    // The favourite button on the hold panel: a yellow star to add, an empty one to take it off.
    public static readonly IValueConverter FavouritePicture = new FuncValueConverter<bool, SimplePicture>(favourite => favourite ? SimplePicture.NotFavourite : SimplePicture.Favourite);

    public static readonly IValueConverter FavouriteLabel = new FuncValueConverter<bool, string>(favourite => favourite ? "Remove" : "Favourite");

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
