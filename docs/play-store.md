# Publishing to Google Play

Notes for putting the Android app on Google Play alongside the existing GitHub APK. Nothing in the
repo submits anything to Google; every step below is done by hand in Play Console.

## What the build already does

- **Release builds produce both an APK and an AAB**, signed with the same persistent keystore (see
  `BitMagic.BennyBox.Android.csproj` and `.github/workflows/release.yml`). The APK still goes on the
  GitHub Release for direct installs. The AAB is uploaded as a separate workflow artifact
  (`BennyBox-<version>-android-play`) for uploading to Play by hand.
- **versionCode** comes from the tag (`major*10000 + minor*100 + patch`), so every release is
  higher than the last, as Play requires.
- **Target SDK 36**, min SDK 23. All native libraries are 16 KB page aligned (checked on 6 Nov 2026).
- **Update check:** a Play install hides the GitHub update check and says updates come from Play
  (`PlayAwareUpdateCheckService`); a sideloaded APK keeps it. Same build, decided at runtime.
- **Permissions:** only INTERNET, ACCESS_NETWORK_STATE and WAKE_LOCK. The QR scanner uses Google
  Play services' own scanner UI, so no CAMERA permission is needed.

## 1. Signing: keep one key for both channels

By default Play re-signs apps with a key Google generates. If that happened, nobody with the GitHub
APK could update from Play (or the other way round) without uninstalling and losing their data.

When creating the app in Play Console, under **App signing**, choose **Use a different key** →
**Export and upload a key from Java keystore**, and give Play the existing keystore
(`bennybox-release.keystore`, alias `bennybox`):

1. Download `pepk.jar` and the encryption public key from that Play Console page.
2. Run (JDK 17 is at `C:\Program Files (x86)\Android\openjdk\jdk-17.0.14`):
   ```
   java -jar pepk.jar --keystore=bennybox-release.keystore --alias=bennybox \
     --output=bennybox-play-key.zip --include-cert --rsa-aes-encryption \
     --encryption-key-path=encryption_public_key.pem
   ```
3. Upload the zip. Delete it afterwards.

The same keystore can stay the upload key, so CI needs no changes. Do this **before the first
upload**: once an app is on Play, its app signing key can't be swapped for this one.

## 2. Account

- Personal developer accounts made after November 2023 need a **closed test with at least 12
  testers for 14 days in a row** before production access. Organisation accounts skip this.
- Identity verification has to be finished in Play Console first.

## 3. Store listing

**App name:** Benny Box

**Short description (80 chars max):**
> Player for your own IPTV playlists, Xtream accounts and Jellyfin servers.

**Full description:**
> Benny Box is a player for media you already have access to. It doesn't include any channels,
> streams or content: add your own sources and it organises them into Live TV, a TV guide, series,
> movies and clips.
>
> • Add M3U playlists with XMLTV guides, Xtream Codes accounts, Jellyfin servers or SFTP folders
> • Live TV with a programme guide, catch-up where your provider supports it, and reminders
> • Series and movies with posters, descriptions and episode lists
> • Favourites, watched markers and resume from where you left off
> • Download films and episodes to watch offline
> • Set up in seconds by scanning a QR code from Benny Box on Windows
>
> You need your own playlist, account or server to use Benny Box.
>
> This product uses the TMDb API but is not endorsed or certified by TMDb.

**Graphics:** the 512×512 icon and the 1024×500 feature graphic are in `docs/play-store/`
(`icon-512.png`, `feature-graphic.png`, with their SVG sources; re-render with
`magick icon-512.svg -alpha remove icon-512.png`). Phone screenshots are in `docs/play-store/screenshots/`,
taken from the emulator with the demo playlist below in dark theme, then cropped/padded to 1101×2202
(Play rejects screenshots whose long side is more than twice the short side). The player isn't shown:
the emulator's video surface captures as black.

**Tablet screenshots** (7-inch and 10-inch slots; the same images work for both) are in
`docs/play-store/screenshots/tablet/`: 2560×1600 landscape from a Pixel Tablet emulator
(`BennyBoxTablet` AVD), demo playlist, dark theme. No cropping needed at 16:10.

**Demo playlist:** `docs/play-store/demo/demo.m3u` has 12 made-up channels with generated logos,
pointing at openly licensed test streams (Blender's Big Buck Bunny and Tears of Steel, Apple's and
Akamai's HLS test streams). Once pushed it's reachable at
<https://raw.githubusercontent.com/Yazwh0/bennybox/main/docs/play-store/demo/demo.m3u>.

**Screenshots:** use a test playlist with neutral channel names. No real broadcaster logos, film
posters or channel brands. Google pulls IPTV apps that look like they provide content.

**Category:** Video Players & Editors.

**Privacy policy URL:** <https://github.com/Yazwh0/bennybox/blob/main/PRIVACY.md>

## 4. App content forms

- **Data safety:** no data collected, no data shared. Credentials are stored on the device and
  encrypted, never sent to the developer. Data is encrypted in transit only where the user's own
  server uses HTTPS, so answer the encryption question accordingly.
- **Ads:** no ads.
- **Content rating:** answer as a media player with no content of its own; user-provided content
  isn't rated by the app.
- **Target audience:** 18+ (avoids the Families policy, since the app can play anything the user
  adds).
- **App access:** reviewers need a working source to see past the empty state. In the review
  instructions, tell them to open Settings → Add Profile → M3U and enter the demo playlist URL above.
  Never give them your own provider's login.
- **News app / government app / financial features:** no.

## 5. Uploading

1. Run the Release workflow (tag push, or workflow_dispatch with a version).
2. Download the `BennyBox-<version>-android-play` artifact and upload the `.aab` to a testing track.
3. Promote to production when ready.
