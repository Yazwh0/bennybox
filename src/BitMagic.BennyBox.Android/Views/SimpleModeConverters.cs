using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BitMagic.BennyBox.Android.Views;

public static class SimpleModeConverters
{
    // The word under the simple player's play/pause button: what pressing it will do.
    public static readonly IValueConverter PlayPauseLabel = new FuncValueConverter<bool, string>(paused => paused ? "Play" : "Pause");

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
