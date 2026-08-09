using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace BitMagic.BennyBox.Converters;

// Transport bar buttons are icon-only (matching the sidebar nav's icon-only look) but still need
// a symbol that reflects which action a click will perform, not the current state - IsPaused=true
// means playback is paused, so the button should show Play (click to resume).
public class PausedToPlaySymbolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FASymbol.PlayFilled : FASymbol.PauseFilled;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class MutedToSymbolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? FASymbol.SpeakerMute : FASymbol.Speaker2;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Highlights whichever nav button matches MainWindowViewModel.CurrentPageTag - value is the current
// tag, parameter is the specific button's own tag (set per-button in XAML via ConverterParameter).
// Every button gets the base glass tint so the row reads as a strip of glass chips even before
// picking one; the active button additionally gets the accent tint. Colors match App.axaml's
// GlassPanelBrush/AccentGlassBrush tokens - kept as plain ARGB here since a converter can't easily
// pull a DynamicResource, so keep these two in sync if the palette ever changes.
public class ActiveTagToBackgroundConverter : IValueConverter
{
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.FromArgb(51, 0xD9, 0x99, 0x5A));
    private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.FromArgb(148, 0x2E, 0x25, 0x1E));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string tag && parameter is string target && tag == target ? ActiveBrush : InactiveBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class ActiveTagToFontWeightConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string tag && parameter is string target && tag == target ? FontWeight.Bold : FontWeight.Normal;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// MainWindow hosts a single, permanent video area (see MainWindow.axaml) shared by every page -
// LibVLC's video surface is a native child window, and giving each page its own VideoView meant
// navigating between pages destroyed and recreated that native surface each time; the replacement
// didn't reliably reclaim rendering (video went black, audio kept playing since it isn't tied to the
// surface). These converters size the content (sidebar) column around that single shared layout:
// full width for Settings (the one page with no video), 0 while fullscreen, otherwise the user's
// last resized width.
public class ContentAreaWidthConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSettingsActive = values.Count > 0 && values[0] is true;
        var isFullscreen = values.Count > 1 && values[1] is true;
        var width = values.Count > 2 && values[2] is double d ? d : 280;

        if (isSettingsActive)
        {
            return new GridLength(1, GridUnitType.Star);
        }

        return isFullscreen ? new GridLength(0) : new GridLength(width);
    }
}

// Same reasoning as ContentAreaWidthConverter - the MinWidth floor that clamps GridSplitter's drag
// range would otherwise stop the column from ever reaching 0 (fullscreen) or growing to fill the
// window (Settings).
public class ContentAreaMinWidthConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isSettingsActive = values.Count > 0 && values[0] is true;
        var isFullscreen = values.Count > 1 && values[1] is true;
        return isSettingsActive || isFullscreen ? 0.0 : BitMagic.BennyBox.ViewModels.PlayerViewModel.MinSidebarWidth;
    }
}

// The 560 MaxWidth that caps how wide a user can drag the sidebar would otherwise also cap Settings'
// full-width layout to 560px.
public class ContentAreaMaxWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? double.PositiveInfinity : 560.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// The nav button row should stretch to fill the sidebar's actual width on every normal page (so
// buttons resize as the window/sidebar is resized, instead of sitting at a fixed size with dead
// space to the right) - except on Settings, where that same sidebar column widens to the full
// window, and stretching 8 cells across that would maroon each icon in the middle of a huge empty
// cell. These two converters apply the cap/left-alignment only while Settings is actually active.
public class NavBarMaxWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 600.0 : double.PositiveInfinity;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class NavBarHorizontalAlignmentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// The video column collapses to 0 (not just visually covered) while Settings is active - a native
// video surface always paints above Avalonia content regardless of z-order, so simply drawing
// Settings' content "over" a nonzero-width video column wouldn't actually hide the video underneath.
public class VideoColumnWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Removes MainWindow's content margin while fullscreen so video runs edge-to-edge.
public class FullscreenToMarginConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? new Thickness(0) : new Thickness(16);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
