<div align="center">

<img src="Seeker/Resources/mipmap-xxxhdpi/ic_launcher_round.png" width="112" alt="MusicBay icon">

<h1>MusicBay</h1>

<p><strong>A free, open source music player and Soulseek client for Android.</strong></p>

<p>
  <img src="https://img.shields.io/badge/platform-Android%206.0%2B-3DDC84?logo=android&logoColor=white" alt="Android 6.0 or newer">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/license-GPL--3.0--only-blue" alt="License: GPL-3.0-only">
</p>

<p>
  <a href="#features">Features</a> &nbsp;&middot;&nbsp;
  <a href="#screenshots">Screenshots</a> &nbsp;&middot;&nbsp;
  <a href="#build-from-source">Build</a> &nbsp;&middot;&nbsp;
  <a href="#credits">Credits</a> &nbsp;&middot;&nbsp;
  <a href="#license">License</a>
</p>

</div>

<hr>

<p>
  MusicBay brings your local music library and Soulseek discovery together in one app.
  It has no ads, no account requirement for local playback, and no paid features.
</p>

<blockquote>
  <p>
    <strong>Project status:</strong> MusicBay is an early preview. Download the latest APK from <a href="https://github.com/ssenwako/MusicBay/releases/latest">Releases</a>, for 64-bit Android phones. The app offers new versions when it opens.
  </p>
</blockquote>

<h2 id="features">Features</h2>

<table>
  <tr>
    <th align="left" width="180">Area</th>
    <th align="left">What it does</th>
  </tr>
  <tr>
    <td><strong>Library</strong></td>
    <td>Browse by songs A&ndash;Z, albums, artists, and playlists. Search across all of them, with accents ignored. Choose which folders appear.</td>
  </tr>
  <tr>
    <td><strong>Albums and covers</strong></td>
    <td>Each music folder is an album. An image such as <code>cover.jpg</code> or <code>folder.jpg</code> becomes its cover, with embedded artwork as the fallback.</td>
  </tr>
  <tr>
    <td><strong>Playlists and queue</strong></td>
    <td>Create, rename, reorder, and delete playlists. Add a song or a whole album, or use Play next and Add to queue.</td>
  </tr>
  <tr>
    <td><strong>Now Playing</strong></td>
    <td>Shows the file format, such as FLAC &middot; 44.1 kHz &middot; 16-bit, with a background tinted by the artwork. Controls also appear on the lock screen and respond to headset buttons.</td>
  </tr>
  <tr>
    <td><strong>Audio output</strong></td>
    <td>Shows where sound is going: phone speaker, wired, USB, or the name of the Bluetooth device. Playback pauses when headphones disconnect.</td>
  </tr>
  <tr>
    <td><strong>Equalizer</strong></td>
    <td>15 bands from 25 Hz to 16 kHz, &plusmn;15 dB, with a preamp and twelve presets. Boosts lower the overall level instead of clipping. Requires Android 9 or newer.</td>
  </tr>
  <tr>
    <td><strong>Streaming mode</strong></td>
    <td>An extra, not a main feature: open it from the &#8942; menu on the Search tab (<em>Enter streaming mode</em>), and leave it with the back arrow; it stays on while you switch tabs. Before a search it reads like Home: recently played, albums you opened, and your playlists. A search shows songs and albums together, or albums only. Search YouTube Music for songs and albums, open albums and artists, and stream them through MusicBay's own player, with no ads and with the equalizer. Nothing is downloaded. Optionally sign in to see your YouTube Music playlists and Liked Music. Use a spare Google account for this, not your main one: Google does not allow third-party apps to use YouTube Music this way and may suspend the account. The session is stored encrypted on the phone.</td>
  </tr>
  <tr>
    <td><strong>Bit-perfect</strong></td>
    <td>Sends audio to a USB DAC with no mixing or resampling on Android 14 or newer. Files up to 16-bit only for now. Bluetooth and the phone speaker always use the system mixer.</td>
  </tr>
  <tr>
    <td><strong>First-run tour</strong></td>
    <td>On a new install, a short walkthrough lights up each part of the app in turn: Home, Search, Library, Downloads, and Account. People who update from an older version don't see it.</td>
  </tr>
  <tr>
    <td><strong>Updates</strong></td>
    <td>When a new version is out, the app offers it on launch. You choose Update or Later, and Android asks before installing.</td>
  </tr>
  <tr>
    <td><strong>Soulseek</strong></td>
    <td>Search with wishlist and filters, download, share, browse users, send messages, join chatrooms, and forward ports.</td>
  </tr>
</table>

<h2 id="screenshots">Screenshots</h2>

<table>
  <tr>
    <td align="center" width="33%"><img src="docs/screenshots/home.png" alt="Home screen" width="240"><br><sub>Home</sub></td>
    <td align="center" width="33%"><img src="docs/screenshots/albums.png" alt="Library showing albums with folder covers" width="240"><br><sub>Albums</sub></td>
    <td align="center" width="33%"><img src="docs/screenshots/album.png" alt="Album detail page" width="240"><br><sub>Album</sub></td>
  </tr>
  <tr>
    <td align="center"><img src="docs/screenshots/songs.png" alt="Library showing songs A to Z" width="240"><br><sub>Songs</sub></td>
    <td align="center"><img src="docs/screenshots/now-playing.png" alt="Now Playing screen" width="240"><br><sub>Now Playing</sub></td>
    <td align="center"><img src="docs/screenshots/sound.png" alt="Sound screen with the equalizer" width="240"><br><sub>Sound</sub></td>
  </tr>
</table>

<h2 id="build-from-source">Build from source</h2>

<h3>Requirements</h3>

<ul>
  <li><a href="https://dotnet.microsoft.com/download/dotnet/10.0">.NET 10 SDK</a> with the Android workload</li>
  <li>Android SDK with API level 36</li>
  <li>JDK 17 or newer</li>
</ul>

<h3>Steps</h3>

<pre><code>git clone https://github.com/ssenwako/MusicBay.git
cd MusicBay
dotnet workload restore Seeker/Seeker.csproj

dotnet build Seeker/Seeker.csproj -c Release -f net10.0-android36.0 -t:SignAndroidPackage \
  -p:AndroidPackageFormat=apk -p:AndroidKeyStore=false \
  -p:RuntimeIdentifier=android-arm64 -p:RuntimeIdentifiers=android-arm64</code></pre>

<p>
  The APK is written to <code>Seeker/bin/Release/net10.0-android36.0/android-arm64/app.musicbay-Signed.apk</code>.
  It is signed with a debug key, so use it for testing only.
</p>

<p>
  To work without a Soulseek account, build with <code>-c "Debug Mock"</code>. This configuration uses a simulated
  Soulseek server and adds no real network traffic. See <a href="DEVELOPMENT.md">DEVELOPMENT.md</a> for more.
</p>

<h2 id="credits">Credits</h2>

<p>MusicBay is built on the work of these projects:</p>

<ul>
  <li>
    <a href="https://github.com/jackBonadies/SeekerAndroid">Seeker</a> by Jack Bonadies, the Soulseek client for Android
    that MusicBay is based on. MusicBay is not an official Seeker release.
  </li>
  <li>
    A modified version of the <a href="https://github.com/jpdillingham/Soulseek.NET">Soulseek.NET</a> library, used for
    communicating with the Soulseek server and network peers.
  </li>
  <li>
    The unofficial <a href="https://nicotine-plus.github.io/nicotine-plus/doc/SLSKPROTOCOL.html">Soulseek Protocol Documentation</a>
    by the developers of the <a href="https://github.com/nicotine-plus/nicotine-plus">Nicotine+</a> client.
  </li>
  <li>
    <a href="https://github.com/Tyrrrz/YoutubeExplode">YoutubeExplode</a> by Oleksii Holub (LGPL-3.0), used to find the
    audio streams for songs from YouTube Music.
  </li>
</ul>

<p>
  The Soulseek server, which makes the network possible, relies on donations.
  You can <a href="https://www.slsknet.org/donate.php">donate here</a>.
</p>

<h2 id="notice">Notice</h2>

<p>This is a modified version of Soulseek.NET.  It is not maintained by, endorsed by, or affiliated with the Soulseek.NET project or its author(s).</p>

<p>
  MusicBay is a modified version of Seeker. It is not maintained by, endorsed by, or affiliated with the Seeker project or
  its author. You are responsible for following the law and the rules of the Soulseek network when you use it.
</p>

<p>
  MusicBay is not affiliated with, endorsed by, or connected to YouTube or Google. Streaming from YouTube Music does not
  go through YouTube's official app, so it can stop working whenever YouTube changes its service.
</p>

<h2 id="license">License</h2>

<p>
  MusicBay is licensed under the <a href="LICENSE">GNU General Public License v3.0 only</a> (<code>GPL-3.0-only</code>),
  with Additional Terms pursuant to Section 7 of the GPLv3.
</p>
