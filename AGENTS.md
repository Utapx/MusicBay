# MusicBay: notes for AI agents

Android music player plus Soulseek client, in C# on .NET 10 (`net10.0-android36.0`, min SDK 23) with XML views and Material 3. A fork of Seeker, rebranded, package `app.musicbay`. There are no ViewModels or DI: state lives in statics such as `SeekerState`, and C# namespaces still say `Seeker`.

Follow [CONTRIBUTING.md](CONTRIBUTING.md) for branches, PRs, and licensing. Never add AI attribution to commits or PRs. Never change the version, create tags, or publish releases.

## Save tokens

Search before reading. Skip these unless the task is about them:
- `Seeker/Soulseek.NET-master/`, `Seeker/Mono.Nat-master/`: vendored libraries, most of the files in the repo
- `Seeker/Resources/values-*/`: translations
- `UnitTestCommon/TestData/`, `fastlane/`, `apx/`
- `Seeker/Main/MainActivity.cs`, `Seeker/Resources/layout/login_viewflipper.xml`: large, read only the part you need

The "Publishing" section of `DEVELOPMENT.md` and `Misc/package-release.ps1` are inherited from Seeker and not how MusicBay releases.

## Where things are

Everything MusicBay added is in `Seeker/Player/`. The rest is mostly Seeker's Soulseek code in feature folders (`Search/`, `Transfers/`, `Browse/`, `Messages/`, `Chatroom/`, `Settings/`). `Common/` holds logic that can be unit tested without Android.

| File in `Seeker/Player/` | Owns |
|---|---|
| `PlayerService.cs` | `Song`, `Playback` (shared queue and state), foreground service, MediaSession, audio focus |
| `AudioPipeline.cs` | `TrackFormat`, `BitPerfect`, `Eq` (15 bands through DynamicsProcessing) |
| `AudioOutput.cs` | Current output label (speaker, wired, USB, Bluetooth name) |
| `MediaLibrary.cs` | MediaStore queries, hidden folders, albums, cover lookup |
| `Playlists.cs` | Playlists, stored as a JSON file in app storage |
| `ArtLoader.cs` | Async cached artwork: embedded art or a folder image |
| `GlassLayout.cs` | `GlassLayout`, `GlassTabBar`, `GlassBars`, `SidewaysDragGuard`, `InsetDivider` |
| `HomeMusicSection.cs` | Home content, `CoverCardAdapter` |
| `LibraryFragment.cs` | Library tab: search, songs, albums, artists, playlists, folder picker |
| `SongListActivity.cs`, `PlaylistActivity.cs` | Album or artist page, playlist page |
| `NowPlayingActivity.cs`, `MiniPlayer.cs`, `SoundActivity.cs` | Player screen, mini player, output and EQ screen |
| `AppUpdater.cs` | Update prompt |
| `StreamFragment.cs` | Streaming mode (Search menu, no tab): YouTube Music search. Pages come from `Common/YtMusic.cs`; `PlayerService.OpenStream` finds the audio with YoutubeExplode |

The Account panel, the Soulseek login and the Home screen host are in `Seeker/Login/LoginFragment.cs`, with `HomeMusicSection` filling the music part.

## Rules that are easy to break

- **Tab pages are numbered, and Soulseek flows jump by number:** 0 Home (`LoginFragment`), 1 Search, 2 Transfers, 3 Browse, 4 Library, 5 streaming mode. Do not reorder them; add new pages at the end. Browse and streaming mode have no tab and show under Search in `GlassTabBar`; streaming mode opens from the Search menu, stays on across tab changes (`StreamFragment.Active`: the Search tab then opens page 5), and back returns to page 1. The visual tab order differs from the page order.
- **Tabs change by tapping only.** Pager swiping is off on purpose, and `SidewaysDragGuard` stops sideways drags from counting as taps on rows. Do not turn swiping back on.
- **Glass bars float over the pager, so content scrolls behind them.** `GlassBars.Reserve` adds the space. Scroll views get bottom padding on their child, never on themselves: padding on the scroll view makes drags from tappable rows stop scrolling. Wrap-content lists such as the search chips get none, or a gap opens under them.
- **Blur needs Android 12, the lens effect Android 13.** Older phones get a nearly opaque capsule, so keep that fallback.
- **The toolbar is hidden on Home, Library and streaming mode** (`OnPagerPageSelected` in `MainActivity`). Top-bar actions for those screens belong in the Account panel, through `HandleAccountAction`.
- **Only the `*_Modern` themes are in use.** Theme choice was removed: users pick light, dark, or system. Colors come from `mb_*` tokens in `Resources/values/colors.xml`: neutrals plus one accent. The theme attribute `mainPurple` is that accent, despite the name. Test both themes.
- **The Soulseek client ID in `Seeker/SeekerApplication.cs` must stay unique to MusicBay,** as `LICENSE` requires. Do not set it back to Seeker's.
- **An album is one music folder,** not a tag value.
- **Bit-perfect is limited to USB DACs on Android 14+ and files up to 16-bit.** It refuses anything else rather than claiming it. Do not widen this without testing on a real DAC.
- **Streamed songs are `Song`s with `StreamId` set and a negative `Id`.** They never reach MediaStore code: no recents, no playlists, no bit-perfect. `YtMusic` sends a desktop User-Agent on purpose: Android's default gets a layout without durations.
- **The YouTube Music sign-in cookies are account access.** `YtAccount` keeps them only encrypted with an Android keystore key and clears the WebView's copy; never log them or store them elsewhere. The spare-account warning shows before every sign-in on purpose.
- **Updates read `update.json` from the latest GitHub release** (`AppUpdater.cs`). Every failure must throw, so a failed check is never reported as "up to date". Mock builds read `localhost:8765` instead.

## Build and test gotchas

Commands are in `CONTRIBUTING.md`. Beyond those:
- Always `-t:Clean` first. An incremental build can crash at launch with `UnsatisfiedLinkError` in `SeekerApplication.n_onCreate`.
- `-t:Install` installs to every connected device. Use `-t:SignAndroidPackage`, then `adb -s <serial> install -r`.
- `Debug Mock` needs no account and accepts made-up logins. `Debug` uses the real Soulseek server.
- `uiautomator dump` can fail with "could not get idle state" while a song plays, because of the progress ticker. Pause first.
- An emulator with no internet usually needs `-dns-server 8.8.8.8`.
- Screenshots: `adb shell screencap -p /sdcard/s.png`, then `adb pull`. Shell redirection corrupts PNGs on Windows.
- CI skips tests that need the original project's encryption key or data. The filter is in `.github/workflows/unittests.yml`, and those failures locally are expected without it.

## Not verified on real hardware

The Bluetooth output label, bit-perfect on a USB DAC, and completing an install from the update prompt. Say so if your change depends on them, rather than assuming they work.
