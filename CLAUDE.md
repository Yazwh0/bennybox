# Benny Box

A Windows desktop IPTV player. Avalonia UI (desktop) + FluentAvaloniaTheme, LibVLCSharp for
playback, SQLite for storage, CommunityToolkit.Mvvm for MVVM. See README.md for the full feature
list and `dotnet build BitMagic.BennyBox.slnx` to build.

## Building and running

```
dotnet build BitMagic.BennyBox.slnx -c Debug
cd src/BitMagic.BennyBox && dotnet run -c Debug --no-build
```

Always start (and leave running) the app after a change so it can be tested by hand, rather than
killing it once automated verification looks right - see "Debug automation bridge" below for how
to drive/inspect it without stealing window focus.

## Debug automation bridge

DEBUG builds only (`src/BitMagic.BennyBox/Debug/DebugRemoteControlServer.cs`) expose an
HTTP API on `http://127.0.0.1:47811/` for driving the running app directly by reading/writing
ViewModel state and invoking commands - use this instead of simulating mouse clicks with
screen-pixel coordinates, which is fragile (window position/size shifts between screenshots).

- `GET /` - lists view model names and routes
- `GET /state` / `GET /state/{vm}` - dump a view model's public properties
- `POST /navigate` body `{"page":"Series"}` - drive `MainWindowViewModel.NavigateCommand`
- `POST /vm/{vm}/set` body `{"property":"X","value":"Y"}` - set a writable property
- `POST /vm/{vm}/invoke` body `{"command":"X","parameter":"Y"?}` - execute an `ICommand` property

View model keys: MainWindow, Search, LiveTv, Guide, Series, Movies, Clips, Downloads, Favorites,
Settings, Player, RemoteControl.

Reserve real screen automation (Win32 `SetForegroundWindow`/mouse events + `CopyFromScreen`) for
the final visual confirmation once the app is already in the right state via the bridge, not for
navigating there.

## XAML/Avalonia styling conventions

The "smoked glass" button classes and their pointerover behavior are defined once in
`src/BitMagic.BennyBox/App.axaml` (`Button.glass-nav`, `Button.glass-ctrl`,
`Button.glass-ctrl.accent`, `Button.glass-ctrl.flat`) - read the comments there before adding a
new button variant, and reuse an existing class instead of styling a button ad hoc with local
`Background`/`BorderBrush` attributes. The two biggest footguns, both learned the hard way:

**FluentAvaloniaTheme's default Button hover/pressed states will "flash" through any custom
styling that doesn't explicitly cancel them.** The theme recolors the templated `ContentPresenter`
on `:pointerover`/`:pressed` with its own grey palette, and that firing is independent of whatever
your own style class sets as the base `Background`. This means:
- Any custom glass-* class needs its own `:pointerover /template/ ContentPresenter` and
  `:pressed /template/ ContentPresenter` selectors that *explicitly* set `Background` (even if the
  value is `Transparent`, matching idle) - omitting the setter is not the same as leaving it alone,
  the theme's own value wins and flashes grey.
- Keep the hover treatment as either a *background* wash (`glass-nav`/`glass-ctrl`, when the
  button already has a filled idle background) or a *border-only* recolor (`glass-ctrl.flat`, for
  buttons with no idle fill, e.g. the clickable title area inside a row/tile card) - never
  introduce a background fill on hover for a button whose idle background is fully transparent,
  the contrast reads as a jarring pop/flash rather than a highlight.

**Icon-only row/tile action buttons (favorite star, watched eye, download arrow, etc.) need a
fixed `Width`/`Height`, not `MinWidth`.** `MinWidth` lets the button grow to fit whatever glyph is
currently bound (e.g. `WatchedIcon` toggles between "✓" and "👁", which render at different
advance widths), so sibling buttons in the same row visibly shift as state changes. Use explicit
`Width`/`Height` (currently `34`) with `Padding="0"` and
`HorizontalContentAlignment="Center" VerticalContentAlignment="Center"` instead - and watch for
clipping if a glyph is wider than the box (emoji in particular render larger than plain Unicode
symbols like `★`/`✓`).

**Row/tile card pattern** (Series, Movies, Clips, Live TV, Favorites list rows - see
`SeriesView.axaml`/`MoviesView.axaml`/`ClipsView.axaml` for the canonical shape): a single
`Border Classes="glass-card"` wraps the whole row, containing one `Button Classes="glass-ctrl
flat"` for the primary click target (select/play) sized `HorizontalAlignment="Stretch"
Padding="0"`, and one or more fixed-size `Button Classes="glass-nav"` icon buttons pinned to the
right via `Grid.Column`. All buttons live *inside* the one card - don't give the action buttons
their own separate bordered box next to the card, and don't nest a second bordered `glass-ctrl`
button inside the card for the select area (use the borderless `flat` variant).
