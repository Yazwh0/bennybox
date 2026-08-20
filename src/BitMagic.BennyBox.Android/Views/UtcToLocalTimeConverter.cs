using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BitMagic.BennyBox.Android.Views;

// GuideView-only: EpgProgramme.StartUtc/EndUtc are UTC, but a phone guide should show local
// programme times, not a raw UTC stamp - unlike desktop's EpgRowControl, which never renders start
// times as text at all (it positions blocks by pixel offset, so there's no existing "format a
// programme time" convention to reuse here).
public sealed class UtcToLocalTimeConverter : IValueConverter
{
    public static readonly UtcToLocalTimeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime utc ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("t", culture) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
