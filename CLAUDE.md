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
- `GET /screenshot` - PNG of the main window, rendered off-screen in-process (see below)
- `POST /navigate` body `{"page":"Series"}` - drive `MainWindowViewModel.NavigateCommand`
- `POST /vm/{vm}/set` body `{"property":"X","value":"Y"}` - set a writable property
- `POST /vm/{vm}/invoke` body `{"command":"X","parameter":"Y"?}` - execute an `ICommand` property
  (`parameter` is a plain string; for commands wanting a row ViewModel - e.g. `SelectSeriesCommand`
  wants a `SeriesListItemViewModel` - use `{"command":"X","parameterFromCollection":{"property":
  "Rows","match":"Name","equals":"Y"}}` instead, which looks the object up from another collection
  property on the same vm by matching a named property's stringified value)

View model keys: MainWindow, Search, LiveTv, Guide, Series, Movies, Clips, Downloads, Favorites,
Settings, Player, RemoteControl.

**Always use `GET /screenshot` for visual confirmation, never Win32 screen capture
(`SetForegroundWindow`/mouse events + `CopyFromScreen`).** `/screenshot` renders the live window via
Avalonia's own compositor, so it works regardless of whether the window is actually focused,
minimized, occluded, or on another monitor/virtual desktop - a physical screen-capture has to
first bring the real window to the foreground (which can silently fail - Windows restricts
`SetForegroundWindow` from background processes) and then trusts whatever pixels are actually
on screen at those coordinates, which can capture a completely unrelated window if focus didn't
actually change (this has happened - it silently captured an unrelated app's window twice in a
row despite `GetWindowText` confirming the right HWND immediately beforehand). If the thing you
actually need to verify is backend/ViewModel state (a computed string, a persisted setting, a
command's effect) and `/state` already confirms it, that's the verification - don't also reach for
a screenshot "for completeness." Real mouse/keyboard input (not just screenshotting) is still
sometimes unavoidable for the rare state that's only reachable by an actual click (e.g. a
`Button.Flyout`, which opens on the click event itself, not on its bound `Command`) - but even
then, capture the *result* via `/screenshot`, not `CopyFromScreen`.

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
