using System;
using System.Globalization;
using Avalonia.Data.Converters;
using FluentAvalonia.UI.Controls;

namespace BitMagic.BennyBox.Android.Views;

// AndroidShellView-only equivalents of desktop's PausedToPlaySymbolConverter/MutedToSymbolConverter
// (BitMagic.BennyBox.Converters.FullscreenConverters, desktop-only project) - same FASymbol choices,
// so the transport bar reads as icon-only buttons matching the PC app instead of the wide text-label
// buttons it started with.
public sealed class PausedToPlaySymbolConverter : IValueConverter
{
    public static readonly PausedToPlaySymbolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FASymbol.PlayFilled : FASymbol.PauseFilled;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class MutedToSymbolConverter : IValueConverter
{
    public static readonly MutedToSymbolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FASymbol.SpeakerMute : FASymbol.Speaker2;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
