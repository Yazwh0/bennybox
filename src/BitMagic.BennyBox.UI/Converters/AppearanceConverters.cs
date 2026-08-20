using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BitMagic.BennyBox.Converters;

// Turns SettingsViewModel.WindowBackgroundOpacityPercent (0-100) into the actual brush MainWindow
// paints as its Background. Color matches App.axaml's SmokedBgColor - kept as a plain hex literal
// here since a converter can't easily pull a StaticResource (same tradeoff as
// ActiveTagToBackgroundConverter, see that comment), so keep the two in sync if the palette changes.
public class WindowBackgroundOpacityToBrushConverter : IValueConverter
{
    private static readonly Color SmokedBgColor = Color.Parse("#120E0B");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new SolidColorBrush(SmokedBgColor, (value is int percent ? percent : 55) / 100.0);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
