using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BitMagic.BennyBox.Converters;

// TmdbApiKey's PlaceholderText depends on whether this build has a bundled key (see
// SettingsViewModel.HasEmbeddedTmdbKey) - "Not set" would be misleading on a build where a blank
// field still means enrichment works via the bundled key.
public class HasEmbeddedTmdbKeyToPlaceholderConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Blank = using this build's bundled key" : "Not set";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
