using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BitMagic.BennyBox.Android.Views;

// AndroidShellView-only equivalent of desktop's ActiveTagToBackgroundConverter/
// ActiveTagToFontWeightConverter (BitMagic.BennyBox.Converters.FullscreenConverters, desktop-only
// project) - same string-tag-vs-ConverterParameter comparison, just against AndroidShellViewModel.
// CurrentPageTag instead of MainWindowViewModel's, and with flat opaque colors instead of desktop's
// translucent glass-panel brushes (Android has no glass theme - see App.axaml).
public sealed class ActiveTabToBackgroundConverter : IValueConverter
{
    public static readonly ActiveTabToBackgroundConverter Instance = new();

    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x99, 0x5A), 0.35);
    private static readonly IBrush InactiveBrush = Brushes.Transparent;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string tag && parameter is string target && tag == target ? ActiveBrush : InactiveBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ActiveTabToFontWeightConverter : IValueConverter
{
    public static readonly ActiveTabToFontWeightConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string tag && parameter is string target && tag == target ? FontWeight.Bold : FontWeight.Normal;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
