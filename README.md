# Benny Box

A Windows desktop IPTV player. Point it at an Xtream Codes or M3U/XMLTV source and browse Live TV, a full EPG guide, Series, and Movies from one app, with resumable playback, catch-up TV, reminders, and a phone remote control.

## Features

### Sources
- **Xtream Codes** or **M3U playlist** (+ optional separate XMLTV EPG URL) profiles, added/edited from Settings.
- **Local Folder** or **SFTP** profiles for movies/TV shows already organized on disk or on a remote server - each can point at a Movies path, a TV Shows path, or both. Metadata comes from Kodi/Jellyfin-style NFO sidecars when present, filename parsing as a fallback identifier (including grouping season-per-folder scene releases like `Show.Name.S01...`/`...S02...` back into one show), and [themoviedb.org](https://www.themoviedb.org/) automatically fills in whatever synopsis, genre, or poster is still missing.
- "Test Connection" on Xtream profiles authenticates and shows account status/expiry before saving.
- **Refresh** re-imports a profile's content. Xtream/M3U use conditional requests (ETag/Last-Modified) to skip the re-download entirely if nothing changed on the server; Local Folder/SFTP profiles cache everything they find - including every show's full episode list - so browsing never touches the source again until the next Refresh.
- Multiple profiles can be configured at once - Live TV, Guide, Series, and Movies merge content across all of them. When more than one source contributes to the same list, each title shows a small badge naming which profile it came from, and a Source filter can narrow the list down to just one.

### Live TV
- Category-grouped, searchable channel list (matches channel name or the current programme title).
- Favorite any channel; shows what's currently playing per channel when EPG data is available.

### Guide
- Full EPG timeline grid - channels down the side, scrolling programme blocks across, with Today/Previous/Next day navigation.
- Click a live block to tune in, a future block to set a **reminder** (a banner pops up app-wide when it's due, with a Watch button), or a past block within the provider's catch-up window to play **catch-up/timeshift** of that exact programme.
- Search, category filter, and a "hide channels with no EPG info" toggle.

### Series & Movies
- Category-grouped, searchable browsing with a detail view per title (poster, metadata, plot/genre fetched on demand for movies).
- Favorite and mark watched/unwatched independently of actual playback.
- Long titles/descriptions are capped with a "Read full description" flyout rather than squeezing episode lists.

### Favorites
- One page aggregating **Continue Watching** (in-progress movies/episodes with a resume-position progress bar), favorited channels, series, and movies.

### Playback
- Resumable movies/episodes - progress is saved every 15 seconds and on pause/stop, and auto-clears once a title is finished (95%+ watched or reaches the end).
- Automatic freeze/stall recovery: if a stream silently dies mid-playback (a dropped connection reported as a clean end-of-stream, or a genuinely frozen frame with no error at all), the app catches it and offers a one-click **Retry** that reconnects at the exact position it froze - instead of a dead, frozen picture with no way out.
- If you pause for long enough that the connection likely dropped, resuming reconnects from scratch automatically rather than sitting on a dead connection.
- Skip interval, connect timeout, stall-detection sensitivity, and pause-reconnect threshold are all tunable in Settings.
- Optional preferred audio/subtitle language, matched loosely against each stream's own track names and applied automatically when playback starts.

### Remote control
- Settings → **Remote** shows a QR code (and fallback URL) for a small mobile web page, gated by a random per-session code so it can't be guessed.
- From a phone on the same network: play/pause, stop, skip forward/back, and adjust volume, with the page staying in sync with what the app is actually doing.
- Only listens once you open the Remote panel, and the code can be regenerated at any time to invalidate the old one.

### Other
- Light/Dark/System theme.
- The app reopens on whichever page you last had open.

## Getting started

1. Open **Settings** → **Add Profile**, pick Xtream Codes, M3U, Local Folder, or SFTP, and fill in the details.
2. Once imported, browse Live TV/Guide/Series/Movies from the nav bar.
3. To control playback from your phone, open the **Remote** panel and scan the QR code.

## Building from source

Requires the .NET 10 SDK.

```
dotnet build BitMagic.BennyBox.slnx
```

A Windows installer is built via `installer/BennyBox.iss` (Inno Setup) from a self-contained `dotnet publish -r win-x64` output.

Local Folder/SFTP metadata enrichment works out of the box in official release builds, which embed a TMDb API key at publish time via the `TMDB_API_KEY_ENCODED` GitHub Actions secret (obfuscated in the binary, never committed in the clear). A local dev build has no embedded key and simply falls back to whatever key you enter in Settings → Metadata enrichment - get a free one at [themoviedb.org](https://www.themoviedb.org/settings/api).

## Tech stack

Avalonia UI (desktop), LibVLCSharp/libVLC for playback, SQLite for local storage, SSH.NET for SFTP, CommunityToolkit.Mvvm. Movie/TV metadata enrichment courtesy of [TMDb](https://www.themoviedb.org/) (this product uses the TMDb API but is not endorsed or certified by TMDb).
