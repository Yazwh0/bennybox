# Benny Box

A Windows desktop and Android IPTV player. Point it at an Xtream Codes or M3U/XMLTV source and browse Live TV, a full EPG guide, Series, and Movies from one app, with resumable playback, catch-up TV, reminders, and (on Windows) a phone remote control.

## Features

### Sources
- **Xtream Codes** or **M3U playlist** (+ optional separate XMLTV EPG URL) profiles, added/edited from Settings.
- **Local Folder** or **SFTP** profiles for movies/TV shows/one-off clips already organised on disk or on a remote server - each can point at a Movies path, a TV Shows path, a Clips path, or any combination. Metadata comes from Kodi/Jellyfin-style NFO sidecars when present, filename parsing as a fallback identifier (including grouping season-per-folder scene releases like `Show.Name.S01...`/`...S02...` back into one show), and [themoviedb.org](https://www.themoviedb.org/) automatically fills in whatever synopsis, genre, or poster is still missing for Movies/TV - Clips (sports broadcasts, specials, anything that isn't a movie or an episode) deliberately skip this, since a metadata-matching guess is more likely to be wrong than helpful for one-off content.
- "Test Connection" on Xtream profiles authenticates and shows account status/expiry before saving.
- **Refresh** re-imports a profile's content. Xtream/M3U use conditional requests (ETag/Last-Modified) to skip the re-download entirely if nothing changed on the server; Local Folder/SFTP profiles cache everything they find - including every show's full episode list - so browsing never touches the source again until the next Refresh.
- Multiple profiles can be configured at once - Live TV, Guide, Series, and Movies merge content across all of them. When more than one source contributes to the same list, each title shows a small badge naming which profile it came from, and a Source filter can narrow the list down to just one.

### Live TV
- Category-grouped, searchable channel list (matches channel name or the current programme title).
- Favourite any channel; shows what's currently playing per channel when EPG data is available.
- Missing/broken channel logos fall back automatically: first to a same-named Series' poster if one's in your library, then to a live [themoviedb.org](https://www.themoviedb.org/) search - resolved once per channel name and cached permanently, so it's instant on every later view or launch.

### Guide
- Full EPG timeline grid - channels down the side, scrolling programme blocks across, with Today/Previous/Next day navigation.
- Click a live block to tune in, a future block to set a **reminder** (a banner pops up app-wide when it's due, with a Watch button), or a past block within the provider's catch-up window to play **catch-up/timeshift** of that exact programme.
- Search, category filter, and a "hide channels with no EPG info" toggle.

### Series & Movies
- Category-grouped, searchable browsing with a detail view per title (poster, metadata, plot/genre fetched on demand for movies).
- Favourite and mark watched/unwatched independently of actual playback.
- Long titles/descriptions are capped with a "Read full description" flyout rather than squeezing episode lists.

### Clips
- A dedicated section for media that isn't a movie or a TV episode - sports broadcasts, one-off specials, anything with no useful season/episode number. Same category/source browsing as Series & Movies, just without the metadata-lookup step.

### Downloads
- A download button on any movie, episode, or clip pulls a copy onto this machine for offline/faster playback - once downloaded, the app automatically plays that local copy instead of re-streaming the original, without losing your watch progress.
- **Download Season** and **Download All** on a series' detail page queue every not-yet-downloaded episode at once (two downloads run at a time; the rest just wait their turn).
- A dedicated **Downloads** page shows progress for everything in flight and everything already downloaded, with Cancel, Retry, and Delete per item - clicking a completed download jumps straight to it in Movies/Series/Clips, ready to play.
- Downloads resume from where they left off after an interruption (a dropped connection, or the app closing mid-download) for Xtream and Local Folder sources. SFTP downloads restart cleanly instead of resuming - the SFTP library this app uses doesn't yet support reading from a mid-file offset, so a retry re-fetches the whole file rather than continuing it.
- An episode's row shows which profile is actually serving it right now - the streaming source, or "Downloads" once a local copy exists - with the file's location available on hover.
- Settings → **Downloads** shows where files are being saved, lets you change that location, and reports disk usage with a "Clear all downloads" option.

### Favorites
- One page aggregating **Continue Watching** (in-progress movies/episodes with a resume-position progress bar), favourited channels, series, movies, and clips.

### Playback
- Resumable movies/episodes - progress is saved every 15 seconds and on pause/stop, and auto-clears once a title is finished (95%+ watched or reaches the end).
- Automatic freeze/stall recovery: if a stream silently dies mid-playback (a dropped connection reported as a clean end-of-stream, or a genuinely frozen frame with no error at all), the app catches it and offers a one-click **Retry** that reconnects at the exact position it froze - instead of a dead, frozen picture with no way out.
- If you pause for long enough that the connection likely dropped, resuming reconnects from scratch automatically rather than sitting on a dead connection.
- Skip interval, connect timeout, stall-detection sensitivity, and pause-reconnect threshold are all tunable in Settings.
- Optional preferred audio/subtitle language, matched loosely against each stream's own track names and applied automatically when playback starts.

### Remote control (Windows only)
- Settings → **Remote** shows a QR code (and fallback URL) for a small mobile web page, gated by a random per-session code so it can't be guessed.
- From a phone on the same network: browse and play Live TV, Guide, Series, Movies, Clips, and Favorites (including Continue Watching), plus play/pause, stop, skip forward/back, and adjust volume for whatever's currently playing - the page stays in sync with what the app is actually doing.
- Only listens once you open the Remote panel, and the code can be regenerated at any time to invalidate the old one.

### Other
- Light/Dark/System theme, plus (Windows only) an adjustable background opacity slider for how see-through the app is against the desktop behind it.
- The app reopens on whichever page you last had open (Windows: also the same window position and size).

### Android
Same app, same account/profile data, same ViewModel and business logic as Windows - just a touch-first interface on top:
- Bottom tab bar instead of a sidebar, with the same 9 sections (Search, Live TV, Guide, Series, Movies, Clips, Downloads, Favorites, Settings).
- Guide is reshaped for a phone: one collapsible section per channel with that day's programmes as a scrollable list, instead of desktop's side-scrolling timeline - same tap-to-tune/catch-up/set-reminder behaviour.
- True fullscreen video: rotates to landscape, hides the system status bar, icon-only transport bar, tap to reveal the exit control.
- No Remote control panel (little point remote-controlling the phone from itself) and no background-opacity setting (no transparent-window concept on mobile) - everything else applies.

## Installing

Every [release](https://github.com/Yazwh0/bennybox/releases) includes a Windows installer and an Android APK.

### Windows
1. Download `BennyBoxSetup-<version>.exe` from the [latest release](https://github.com/Yazwh0/bennybox/releases/latest) and run it.
2. Launch **Benny Box** from the Start menu.

### Android
1. On the phone, download `BennyBox-<version>.apk` from the [latest release](https://github.com/Yazwh0/bennybox/releases/latest).
2. Android blocks the first install - allow "Install unknown apps" for whichever app you downloaded it with (Settings → Apps → *that app* → Install unknown apps), then open the file to install.
3. **Updating loses your data - for now.** Each release is signed with its own build-time key, not a persistent one, so Android won't install a new version over an old one; you have to uninstall first. Uninstalling wipes the app's private storage, which is where profiles, favourites, watch progress, settings, and downloaded files all live, so there's currently no way to update without starting over. Fixing this needs a persistent signing key added to the build, which hasn't happened yet.

No Play Store listing - this is a hobby project, so sideloading the APK is the only route for now.

## Getting started

1. Open **Settings** → **Add Profile**, pick Xtream Codes, M3U, Local Folder, or SFTP, and fill in the details.
2. Once imported, browse Live TV/Guide/Series/Movies/Clips from the nav bar (Windows) or bottom tab bar (Android).
3. On Windows, to control playback from your phone, open the **Remote** panel and scan the QR code.

## Building from source

Requires the .NET 10 SDK.

```
dotnet build BitMagic.BennyBox.slnx
```

`BitMagic.BennyBox.slnx` includes both heads - for Windows only (e.g. no Android workload installed), build the filtered solution instead:

```
dotnet build BitMagic.BennyBox.Desktop.slnf
```

The Windows installer is built via `installer/BennyBox.iss` (Inno Setup) from a self-contained `dotnet publish -r win-x64` output.

The Android head needs the Android workload:

```
dotnet workload install android
dotnet build src/BitMagic.BennyBox.Android/BitMagic.BennyBox.Android.csproj
```

A signed, installable APK needs `dotnet publish`, not `build` - see `.github/workflows/release.yml` for the exact command used for official releases.

Local Folder/SFTP metadata enrichment works out of the box in official release builds (both platforms), which embed a TMDb API key at publish time via the `TMDB_API_KEY_ENCODED` GitHub Actions secret (obfuscated, never committed in the clear). A local dev build has no embedded key and falls back to whatever key you enter in Settings → Metadata enrichment - get a free one at [themoviedb.org](https://www.themoviedb.org/settings/api).

## Tech stack

Avalonia UI (desktop and Android), SQLite for local storage, SSH.NET for SFTP, CommunityToolkit.Mvvm. Playback is LibVLCSharp/libVLC on Windows and Media3/ExoPlayer on Android (LibVLCSharp's video view doesn't work on Android). Movie/TV metadata enrichment courtesy of [TMDb](https://www.themoviedb.org/) (this product uses the TMDb API but is not endorsed or certified by TMDb).
