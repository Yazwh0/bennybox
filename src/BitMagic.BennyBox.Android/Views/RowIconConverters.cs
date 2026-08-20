using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BitMagic.BennyBox.ViewModels;
using FluentAvalonia.UI.Controls;

namespace BitMagic.BennyBox.Android.Views;

// Android-only: the shared row ViewModels (ChannelListItemViewModel, SeriesListItemViewModel, etc.)
// expose FavoriteIcon/WatchedIcon/DownloadIcon as plain emoji strings ("★"/"☆", "✓"/"👁", "⬇"/"⏳"/...)
// which desktop binds to Button.Content directly - that's fine on desktop (consistent with its own
// emoji-based look elsewhere), but on Android it read as visibly inconsistent against the
// FASymbolIcon-based tab bar/transport bar (see ActiveTabConverters.cs/PlayerIconConverters.cs) - most
// visibly the watched-eye emoji, which also renders completely differently per-platform since it's
// just whatever the OS's own emoji font draws. These convert the underlying bool/enum state (IsFavorite,
// IsWatched, DownloadState) straight to a FASymbol instead, bypassing those shared string properties
// entirely, so every icon-only button in the Android app - tab bar, transport bar, and every row's
// favorite/watched/download toggle - is the same vector icon family throughout.
public sealed class FavoriteToSymbolConverter : IValueConverter
{
    public static readonly FavoriteToSymbolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FASymbol.StarFilled : FASymbol.Star;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// No dedicated "eye" glyph exists in FluentAvalonia's bundled icon set (confirmed by decompiling
// FASymbol - Fluent System Icons normally has one, this is a reduced subset) - View is the closest
// available stand-in for "not watched yet, tap to mark watched".
public sealed class WatchedToSymbolConverter : IValueConverter
{
    public static readonly WatchedToSymbolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FASymbol.Checkmark : FASymbol.View;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DownloadStateToSymbolConverter : IValueConverter
{
    public static readonly DownloadStateToSymbolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DownloadUiState state
            ? state switch
            {
                DownloadUiState.Queued => FASymbol.Clock,
                DownloadUiState.Downloading => FASymbol.CloudDownload,
                DownloadUiState.Completed => FASymbol.Checkmark,
                _ => FASymbol.Download
            }
            : FASymbol.Download;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
