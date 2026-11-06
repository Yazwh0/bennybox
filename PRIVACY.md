# Benny Box privacy policy

_Last updated: 6 November 2026_

Benny Box is a media player for Windows and Android. It plays content from sources you add yourself
(M3U playlists, Xtream Codes accounts, Jellyfin servers, SFTP servers and local folders). It doesn't
come with any channels or content of its own.

## What the app stores

Everything stays on your device:

- The profiles you add: server addresses, usernames and passwords (or API keys). Passwords and keys
  are encrypted with the operating system's own key store (Windows DPAPI, or the Android Keystore).
- Your channel lists, programme guide, favourites, watch history, playback positions, downloads and
  settings.

None of this is sent to the developer or anyone else. There are no accounts, no analytics, no
advertising and no crash reporting.

## Who the app talks to

The app only connects to:

- **The servers you add**, to fetch playlists, guides and streams. Those servers' own privacy
  policies apply to what they receive.
- **The Movie Database (TMDb)**, to look up posters and descriptions for films and shows that don't
  have their own. Only titles are sent, never your credentials. See
  [TMDb's privacy policy](https://www.themoviedb.org/privacy-policy).
- **GitHub**, to check whether a newer version is available (Windows, and Android when installed
  from GitHub rather than Google Play). This can be switched off in Settings.
- **Channel logo addresses** given in your playlists.

## QR code setup

The Windows app can show a QR code that copies your profiles to the Android app. The QR code
contains your passwords, so treat it like the passwords themselves. It is shown on screen only; it is
never uploaded anywhere.

## Deleting your data

Deleting a profile removes it and its content from the device. Uninstalling the app removes
everything.

## Contact

Questions about this policy: open an issue at <https://github.com/Yazwh0/bennybox/issues>.
