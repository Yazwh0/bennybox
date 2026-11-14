using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAvalonia.Styling;

namespace BitMagic.BennyBox.Android.Theming;

// Material Design 3 colour roles (https://m3.material.io/styles/color/roles) - only the ones the app
// uses. Surface containers are the M3 tonal surfaces for cards, bars and dialogs.
public sealed record MaterialScheme(
    Color Primary, Color OnPrimary,
    Color PrimaryContainer, Color OnPrimaryContainer,
    Color SecondaryContainer, Color OnSecondaryContainer,
    Color Surface, Color SurfaceContainerLow, Color SurfaceContainer, Color SurfaceContainerHigh, Color SurfaceContainerHighest,
    Color OnSurface, Color OnSurfaceVariant,
    Color Outline, Color OutlineVariant);

// Makes the Android app look like a native Material 3 app on top of FluentAvalonia: rather than
// restyling every control template, it fills in the theme resource keys FluentAvalonia's templates
// already read (the WinUI names: ButtonBackground, TextControlBorderBrushFocused and so on) from the
// M3 colour scheme, per light/dark variant. Application.Resources is looked up before the theme's own
// resources, so these win. Shapes (corner radii, sizes) live in Theming/Material3.axaml.
//
// On Android 12+ the scheme comes from the wallpaper (Material You, via the system_accent/neutral
// tonal palettes); older versions get M3's baseline purple scheme, which also suits the app icon.
public static class MaterialTheme
{
    public static void Apply(Avalonia.Application app, global::Android.Content.Context context)
    {
        var (light, dark) = DynamicColours.TryRead(context) ?? (Baseline.Light, Baseline.Dark);

        Fill(GetThemeDictionary(app, ThemeVariant.Light), light);
        Fill(GetThemeDictionary(app, ThemeVariant.Dark), dark);

        // Checkboxes, radio buttons, progress rings and text selection use FluentAvalonia's accent;
        // point it at the scheme's primary so they match everything else.
        if (app.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault() is { } fluent)
        {
            fluent.PreferUserAccentColor = false;
            fluent.CustomAccentColor = light.Primary;
        }
    }

    private static ResourceDictionary GetThemeDictionary(Avalonia.Application app, ThemeVariant variant)
    {
        if (app.Resources.ThemeDictionaries.TryGetValue(variant, out var existing) && existing is ResourceDictionary dictionary)
        {
            return dictionary;
        }

        dictionary = new ResourceDictionary();
        app.Resources.ThemeDictionaries[variant] = dictionary;
        return dictionary;
    }

    private static void Fill(ResourceDictionary r, MaterialScheme s)
    {
        var transparent = Brushes.Transparent;
        var disabledContainer = Brush(s.OnSurface, 0.12);
        var disabledContent = Brush(s.OnSurface, 0.38);

        // The app's own keys, for cards, bars and the like (see Material3.axaml).
        r["M3PrimaryBrush"] = Brush(s.Primary);
        r["M3OnPrimaryBrush"] = Brush(s.OnPrimary);
        r["M3PrimaryContainerBrush"] = Brush(s.PrimaryContainer);
        r["M3OnPrimaryContainerBrush"] = Brush(s.OnPrimaryContainer);
        r["M3SecondaryContainerBrush"] = Brush(s.SecondaryContainer);
        r["M3OnSecondaryContainerBrush"] = Brush(s.OnSecondaryContainer);
        r["M3SurfaceBrush"] = Brush(s.Surface);
        r["M3SurfaceContainerLowBrush"] = Brush(s.SurfaceContainerLow);
        r["M3SurfaceContainerBrush"] = Brush(s.SurfaceContainer);
        r["M3SurfaceContainerHighBrush"] = Brush(s.SurfaceContainerHigh);
        r["M3SurfaceContainerHighestBrush"] = Brush(s.SurfaceContainerHighest);
        r["M3OnSurfaceBrush"] = Brush(s.OnSurface);
        r["M3OnSurfaceVariantBrush"] = Brush(s.OnSurfaceVariant);
        r["M3OutlineBrush"] = Brush(s.Outline);
        r["M3OutlineVariantBrush"] = Brush(s.OutlineVariant);
        r["M3PressedOverlayBrush"] = Brush(s.OnSurface, 0.12);

        // Text and page surfaces.
        r["TextFillColorPrimaryBrush"] = Brush(s.OnSurface);
        r["TextFillColorSecondaryBrush"] = Brush(s.OnSurfaceVariant);
        r["TextFillColorTertiaryBrush"] = Brush(s.OnSurfaceVariant);
        r["TextFillColorDisabledBrush"] = disabledContent;
        r["SolidBackgroundFillColorBaseBrush"] = Brush(s.Surface);
        r["SolidBackgroundFillColorSecondaryBrush"] = Brush(s.SurfaceContainer);
        r["SolidBackgroundFillColorTertiaryBrush"] = Brush(s.SurfaceContainerHigh);
        r["ApplicationPageBackgroundThemeBrush"] = Brush(s.Surface);
        r["DividerStrokeColorDefaultBrush"] = Brush(s.OutlineVariant);

        // Buttons: plain Buttons are M3 "filled tonal"; Classes="accent" is M3 "filled" (primary).
        // State layers are the M3 8% (hover) / 12% (pressed) tints of the content colour.
        StateSet(r, "Button", "Background", s.SecondaryContainer, s.OnSecondaryContainer, disabledContainer);
        StateSet(r, "Button", "Foreground", s.OnSecondaryContainer, null, disabledContent);
        StateSet(r, "Button", "BorderBrush", Colors.Transparent, null, Colors.Transparent);
        StateSet(r, "AccentButton", "Background", s.Primary, s.OnPrimary, disabledContainer);
        StateSet(r, "AccentButton", "Foreground", s.OnPrimary, null, disabledContent);
        StateSet(r, "AccentButton", "BorderBrush", Colors.Transparent, null, Colors.Transparent);
        StateSet(r, "RepeatButton", "Background", Colors.Transparent, s.OnSurface, Colors.Transparent);
        StateSet(r, "RepeatButton", "Foreground", s.OnSurfaceVariant, null, disabledContent);
        StateSet(r, "RepeatButton", "BorderBrush", Colors.Transparent, null, Colors.Transparent);

        // Text fields and dropdowns: M3 "outlined" fields - no fill, outline that darkens on hover and
        // turns primary when focused.
        foreach (var state in new[] { "", "PointerOver", "Focused" })
        {
            r[$"TextControlBackground{state}"] = transparent;
            r[$"TextControlForeground{state}"] = Brush(s.OnSurface);
            r[$"TextControlPlaceholderForeground{state}"] = Brush(s.OnSurfaceVariant);
        }

        r["TextControlBackgroundDisabled"] = transparent;
        r["TextControlForegroundDisabled"] = disabledContent;
        r["TextControlPlaceholderForegroundDisabled"] = disabledContent;
        r["TextControlBorderBrush"] = Brush(s.Outline);
        r["TextControlBorderBrushPointerOver"] = Brush(s.OnSurface);
        r["TextControlBorderBrushFocused"] = Brush(s.Primary);
        r["TextControlBorderBrushDisabled"] = disabledContainer;
        r["TextControlSelectionHighlightColor"] = Brush(s.Primary, 0.4);

        foreach (var state in new[] { "", "PointerOver", "Pressed", "Focused", "Disabled" })
        {
            r[$"ComboBoxBackground{state}"] = transparent;
        }

        r["ComboBoxForeground"] = Brush(s.OnSurface);
        r["ComboBoxForegroundDisabled"] = disabledContent;
        r["ComboBoxBorderBrush"] = Brush(s.Outline);
        r["ComboBoxBorderBrushPointerOver"] = Brush(s.OnSurface);
        r["ComboBoxBorderBrushPressed"] = Brush(s.Primary);
        r["ComboBoxBorderBrushFocused"] = Brush(s.Primary);
        r["ComboBoxBorderBrushDisabled"] = disabledContainer;
        r["ComboBoxDropDownBackground"] = Brush(s.SurfaceContainer);
        r["ComboBoxDropDownBorderBrush"] = transparent;
        r["ComboBoxItemBackgroundSelected"] = Brush(s.SecondaryContainer);
        r["ComboBoxItemBackgroundSelectedPointerOver"] = Brush(s.SecondaryContainer);
        r["ComboBoxItemBackgroundSelectedPressed"] = Brush(s.SecondaryContainer);
        r["ComboBoxItemForegroundSelected"] = Brush(s.OnSecondaryContainer);

        // Menus, flyouts and dialogs sit on the higher surface containers.
        r["FlyoutPresenterBackground"] = Brush(s.SurfaceContainer);
        r["FlyoutBorderThemeBrush"] = transparent;
        r["MenuFlyoutPresenterBackground"] = Brush(s.SurfaceContainer);
        r["MenuFlyoutPresenterBorderBrush"] = transparent;
        r["ContentDialogBackground"] = Brush(s.SurfaceContainerHigh);
        r["ContentDialogBorderBrush"] = transparent;
        r["ContentDialogTopOverlay"] = Brush(s.SurfaceContainerHigh);

        r["ExpanderHeaderBackground"] = Brush(s.SurfaceContainer);
        r["ExpanderHeaderBackgroundPointerOver"] = Brush(s.SurfaceContainer);
        r["ExpanderHeaderBackgroundPressed"] = Brush(s.SurfaceContainerHigh);
        r["ExpanderHeaderBorderBrush"] = transparent;
        r["ExpanderHeaderBorderBrushPointerOver"] = transparent;
        r["ExpanderHeaderBorderBrushPressed"] = transparent;
        r["ExpanderContentBackground"] = Brush(s.SurfaceContainerLow);
        r["ExpanderContentBorderBrush"] = transparent;

        // M3 switch: primary track with an on-primary handle when on; outlined, surface-filled track
        // with an outline-coloured handle when off.
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            r[$"ToggleSwitchFillOn{state}"] = Brush(s.Primary);
            r[$"ToggleSwitchStrokeOn{state}"] = Brush(s.Primary);
            r[$"ToggleSwitchKnobFillOn{state}"] = Brush(s.OnPrimary);
            r[$"ToggleSwitchFillOff{state}"] = Brush(s.SurfaceContainerHighest);
            r[$"ToggleSwitchStrokeOff{state}"] = Brush(s.Outline);
            r[$"ToggleSwitchKnobFillOff{state}"] = Brush(s.Outline);
        }

        r["ProgressBarForeground"] = Brush(s.Primary);
        r["ProgressBarBackground"] = Brush(s.SurfaceContainerHighest);
    }

    // Fills {prefix}{property}, ...PointerOver, ...Pressed and ...Disabled. When a state-layer colour
    // is given, hover/pressed are the base blended with it at 8%/12%; otherwise they equal the base.
    private static void StateSet(ResourceDictionary r, string prefix, string property, Color rest, Color? stateLayer, Color disabled) =>
        StateSet(r, prefix, property, rest, stateLayer, new SolidColorBrush(disabled));

    private static void StateSet(ResourceDictionary r, string prefix, string property, Color rest, Color? stateLayer, IBrush disabled)
    {
        r[$"{prefix}{property}"] = Brush(rest);
        r[$"{prefix}{property}PointerOver"] = Brush(stateLayer is { } layer ? Over(rest, layer, 0.08) : rest);
        r[$"{prefix}{property}Pressed"] = Brush(stateLayer is { } pressedLayer ? Over(rest, pressedLayer, 0.12) : rest);
        r[$"{prefix}{property}Disabled"] = disabled;
    }

    private static SolidColorBrush Brush(Color colour, double opacity = 1) => new(colour, opacity);

    // `layer` painted over `baseColour` at `amount` opacity. A transparent base just gets the layer's
    // own colour at that opacity (an icon button's ripple).
    internal static Color Over(Color baseColour, Color layer, double amount)
    {
        if (baseColour.A == 0)
        {
            return Color.FromArgb((byte)(255 * amount), layer.R, layer.G, layer.B);
        }

        return Color.FromRgb(
            (byte)(baseColour.R + (layer.R - baseColour.R) * amount),
            (byte)(baseColour.G + (layer.G - baseColour.G) * amount),
            (byte)(baseColour.B + (layer.B - baseColour.B) * amount));
    }

    // M3's published baseline scheme (seed #6750A4).
    private static class Baseline
    {
        public static readonly MaterialScheme Light = new(
            Primary: Color.Parse("#6750A4"), OnPrimary: Color.Parse("#FFFFFF"),
            PrimaryContainer: Color.Parse("#EADDFF"), OnPrimaryContainer: Color.Parse("#21005D"),
            SecondaryContainer: Color.Parse("#E8DEF8"), OnSecondaryContainer: Color.Parse("#1D192B"),
            Surface: Color.Parse("#FEF7FF"), SurfaceContainerLow: Color.Parse("#F7F2FA"), SurfaceContainer: Color.Parse("#F3EDF7"),
            SurfaceContainerHigh: Color.Parse("#ECE6F0"), SurfaceContainerHighest: Color.Parse("#E6E0E9"),
            OnSurface: Color.Parse("#1D1B20"), OnSurfaceVariant: Color.Parse("#49454F"),
            Outline: Color.Parse("#79747E"), OutlineVariant: Color.Parse("#CAC4D0"));

        public static readonly MaterialScheme Dark = new(
            Primary: Color.Parse("#D0BCFF"), OnPrimary: Color.Parse("#381E72"),
            PrimaryContainer: Color.Parse("#4F378B"), OnPrimaryContainer: Color.Parse("#EADDFF"),
            SecondaryContainer: Color.Parse("#4A4458"), OnSecondaryContainer: Color.Parse("#E8DEF8"),
            Surface: Color.Parse("#141218"), SurfaceContainerLow: Color.Parse("#1D1B20"), SurfaceContainer: Color.Parse("#211F26"),
            SurfaceContainerHigh: Color.Parse("#2B2930"), SurfaceContainerHighest: Color.Parse("#36343B"),
            OnSurface: Color.Parse("#E6E0E9"), OnSurfaceVariant: Color.Parse("#CAC4D0"),
            Outline: Color.Parse("#938F99"), OutlineVariant: Color.Parse("#49454F"));
    }

    // Android 12+'s wallpaper-derived tonal palettes. Each system colour's number maps to an M3 tone:
    // _0 = tone 100, _10 = 99, _50 = 95, _100 = 90, _200 = 80 ... _900 = 10, _1000 = 0. Roles follow
    // M3's tone assignments; the surface containers sit between published tones, so they're blended.
    private static class DynamicColours
    {
        public static (MaterialScheme Light, MaterialScheme Dark)? TryRead(global::Android.Content.Context context)
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                return null;
            }

            try
            {
                Color C(int id)
                {
                    var argb = context.Resources!.GetColor(id, context.Theme);
                    return Color.FromUInt32((uint)argb.ToArgb());
                }

                var a1 = Palette(C, global::Android.Resource.Color.SystemAccent10, global::Android.Resource.Color.SystemAccent1100,
                    global::Android.Resource.Color.SystemAccent1200, global::Android.Resource.Color.SystemAccent1600,
                    global::Android.Resource.Color.SystemAccent1700, global::Android.Resource.Color.SystemAccent1800,
                    global::Android.Resource.Color.SystemAccent1900);
                var a2 = Palette(C, global::Android.Resource.Color.SystemAccent20, global::Android.Resource.Color.SystemAccent2100,
                    global::Android.Resource.Color.SystemAccent2200, global::Android.Resource.Color.SystemAccent2600,
                    global::Android.Resource.Color.SystemAccent2700, global::Android.Resource.Color.SystemAccent2800,
                    global::Android.Resource.Color.SystemAccent2900);

                var n1_10 = C(global::Android.Resource.Color.SystemNeutral110);
                var n1_50 = C(global::Android.Resource.Color.SystemNeutral150);
                var n1_100 = C(global::Android.Resource.Color.SystemNeutral1100);
                var n1_700 = C(global::Android.Resource.Color.SystemNeutral1700);
                var n1_800 = C(global::Android.Resource.Color.SystemNeutral1800);
                var n1_900 = C(global::Android.Resource.Color.SystemNeutral1900);
                var n1_1000 = C(global::Android.Resource.Color.SystemNeutral11000);
                var n2_200 = C(global::Android.Resource.Color.SystemNeutral2200);
                var n2_400 = C(global::Android.Resource.Color.SystemNeutral2400);
                var n2_500 = C(global::Android.Resource.Color.SystemNeutral2500);
                var n2_700 = C(global::Android.Resource.Color.SystemNeutral2700);

                var light = new MaterialScheme(
                    Primary: a1.T40, OnPrimary: a1.T100,
                    PrimaryContainer: a1.T90, OnPrimaryContainer: a1.T10,
                    SecondaryContainer: a2.T90, OnSecondaryContainer: a2.T10,
                    Surface: n1_10, SurfaceContainerLow: n1_50,
                    SurfaceContainer: Over(n1_50, n1_100, 0.25), SurfaceContainerHigh: Over(n1_50, n1_100, 0.6), SurfaceContainerHighest: n1_100,
                    OnSurface: n1_900, OnSurfaceVariant: n2_700,
                    Outline: n2_500, OutlineVariant: n2_200);

                var dark = new MaterialScheme(
                    Primary: a1.T80, OnPrimary: a1.T20,
                    PrimaryContainer: a1.T30, OnPrimaryContainer: a1.T90,
                    SecondaryContainer: a2.T30, OnSecondaryContainer: a2.T90,
                    Surface: Over(n1_900, n1_1000, 0.4), SurfaceContainerLow: n1_900,
                    SurfaceContainer: Over(n1_900, n1_800, 0.2), SurfaceContainerHigh: Over(n1_900, n1_800, 0.7),
                    SurfaceContainerHighest: Over(n1_800, n1_700, 0.2),
                    OnSurface: n1_100, OnSurfaceVariant: n2_200,
                    Outline: n2_400, OutlineVariant: n2_700);

                return (light, dark);
            }
            catch (Exception)
            {
                // Some OEM builds have been known to strip or rename these resources; fall back to the
                // baseline scheme rather than fail start-up over colours.
                return null;
            }
        }

        private static Tones Palette(Func<int, Color> read, int t100, int t90, int t80, int t40, int t30, int t20, int t10) =>
            new(read(t100), read(t90), read(t80), read(t40), read(t30), read(t20), read(t10));

        private sealed record Tones(Color T100, Color T90, Color T80, Color T40, Color T30, Color T20, Color T10);
    }
}
