using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BitMagic.BennyBox.Android.Views;

// AndroidShellView-only equivalent of desktop's ActiveTagToFontWeightConverter (BitMagic.BennyBox.Converters.FullscreenConverters, desktop-only
// project) - same string-tag-vs-ConverterParameter comparison, just against AndroidShellViewModel.
// CurrentPageTag instead of MainWindowViewModel's. The active background is a style class instead
// (ActiveTabToBoolConverter below), so it can use the Material theme's DynamicResources.
public sealed class ActiveTabToFontWeightConverter : IValueConverter
{
    public static readonly ActiveTabToFontWeightConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ActiveTab.Matches(value, parameter) ? FontWeight.Bold : FontWeight.Normal;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// ConverterParameter is one tag, or several separated by '|' - the phone tab bar's "More" button is
// highlighted whenever any of the pages tucked into its menu is the current one.
internal static class ActiveTab
{
    public static bool Matches(object? value, object? parameter) =>
        value is string tag && parameter is string targets &&
        targets.Split('|').Contains(tag, StringComparer.Ordinal);
}

// For Classes.active on the tab bar's pill (see AndroidShellView's Border.pill.active style), so the
// active colour is a DynamicResource that follows theme and wallpaper changes, unlike a brush returned
// from a converter.
public sealed class ActiveTabToBoolConverter : IValueConverter
{
    public static readonly ActiveTabToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ActiveTab.Matches(value, parameter);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
