/*
 * Copyright (c) 2026 Arsen
 *
 * This file is part of MusicBay
 *
 * MusicBay is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * MusicBay is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with MusicBay. If not, see <http://www.gnu.org/licenses/>.
 */

using Android.App;
using Android.Content;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Android.Provider;
using Android.Widget;
using AndroidX.Core.Content;
using Seeker.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Seeker.Player
{
    public class Song
    {
        public long Id;
        public string Title;
        public string Artist;
        public string Album;
        public long DurationMs;
        public long AlbumId;
        public string Folder;
        public int Track;
        /// <summary>YouTube video ID for a song streamed from YouTube Music; null for a file on the phone.</summary>
        public string StreamId;
        public string ArtUrl;
        /// <summary>Codec and bitrate of the stream, known once it has been opened.</summary>
        public string StreamFormat;
        public Android.Net.Uri Uri => ContentUris.WithAppendedId(MediaStore.Audio.Media.ExternalContentUri, Id);

        // negative so it never matches a MediaStore ID; screens compare songs by ID
        public static Song FromStream(Common.YtItem item) => new Song
        {
            Id = -1 - (long)(uint)item.VideoId.GetHashCode(),
            StreamId = item.VideoId,
            Title = item.Title,
            Artist = item.Artist,
            Album = item.Album,
            DurationMs = item.DurationMs,
            ArtUrl = item.ThumbUrl,
        };
    }

    /// <summary>Queue + state shared between the UI and PlayerService. The service is the only writer of IsPlaying.</summary>
    public static class Playback
    {
        public const string ActionPlay = "app.musicbay.player.PLAY";
        public const string ActionToggle = "app.musicbay.player.TOGGLE";
        public const string ActionNext = "app.musicbay.player.NEXT";
        public const string ActionPrevious = "app.musicbay.player.PREVIOUS";

        public enum RepeatMode { Off, All, One }

        public static List<Song> Queue = new List<Song>();
        public static int Index = -1;
        public static bool IsPlaying;
        // shortcut: session-only (not persisted), and shuffle picks a random next song, so a song can repeat before all were played. Build a shuffled order if that matters.
        public static bool Shuffle;
        public static RepeatMode Repeat = RepeatMode.Off;

        public static void SeekTo(int ms) => PlayerService.Instance?.SeekTo(ms);

        public static Song Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;
        public static int PositionMs => PlayerService.Instance?.Position ?? 0;

        /// <summary>Raised on the main thread whenever the current song or play state changes.</summary>
        public static event Action Changed;
        internal static void RaiseChanged() => Changed?.Invoke();

        // copied: the queue changes independently of the list the screen that started it is showing
        public static void Play(Context c, List<Song> queue, int index)
        {
            Queue = new List<Song>(queue);
            Index = index;
            Send(c, ActionPlay);
        }

        public static void PlayNext(Context c, Song song)
        {
            if (Current == null) { Play(c, new List<Song> { song }, 0); return; }
            Queue.Insert(Index + 1, song);
        }

        public static void AddToQueue(Context c, Song song)
        {
            if (Current == null) { Play(c, new List<Song> { song }, 0); return; }
            Queue.Add(song);
        }

        public static void Send(Context c, string action)
        {
            ContextCompat.StartForegroundService(c, new Intent(c, typeof(PlayerService)).SetAction(action));
        }
    }

    [Service(Name = "app.musicbay.PlayerService", Exported = false, ForegroundServiceType = Android.Content.PM.ForegroundService.TypeMediaPlayback)]
    public class PlayerService : Service, AudioManager.IOnAudioFocusChangeListener
    {
        const int NotifId = 112;
        const string ChannelId = "musicbay_playback";

        public static PlayerService Instance;

        static readonly Random random = new Random();

        MediaPlayer player;
        bool prepared;
        bool pausedByFocusLoss;
        int resumeAtMs;
        NoisyReceiver noisyReceiver;
        MediaSession session;
        Android.Graphics.Bitmap currentArt;

        public int Position => prepared && player != null ? player.CurrentPosition : 0;

        /// <summary>The output the system is really using, or null while nothing is playing.</summary>
        public AudioDeviceInfo RoutedDevice => OperatingSystem.IsAndroidVersionAtLeast(28) && prepared && Playback.IsPlaying ? player?.RoutedDevice : null;

        public override IBinder OnBind(Intent intent) => null;

        public override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            CommonHelpers.CreateNotificationChannel(this, ChannelId, GetString(Resource.String.player_channel_name));
            noisyReceiver = new NoisyReceiver();
            RegisterReceiver(noisyReceiver, new IntentFilter(AudioManager.ActionAudioBecomingNoisy));
            session = new MediaSession(this, "MusicBay");
            session.SetCallback(new SessionCallback());
            session.Active = true;
        }

        // lock screen, notification, headset buttons and Bluetooth remote controls all arrive here
        class SessionCallback : MediaSession.Callback
        {
            public override void OnPlay() { if (!Playback.IsPlaying) { Instance?.Toggle(); } }
            public override void OnPause() { if (Playback.IsPlaying) { Instance?.Toggle(); } }
            public override void OnSkipToNext() => Instance?.Advance(1);
            public override void OnSkipToPrevious() => Instance?.Previous();
            public override void OnSeekTo(long pos) => Instance?.SeekTo((int)pos);
        }

        // headphones unplugged or Bluetooth dropped: never carry on through the speaker
        class NoisyReceiver : BroadcastReceiver
        {
            public override void OnReceive(Context context, Intent intent)
            {
                if (Playback.IsPlaying) { Instance?.Toggle(); }
            }
        }

        /// <summary>Reopens the current song where it is, so a new mixer or equalizer state takes effect now.</summary>
        public void ReopenCurrent()
        {
            if (player == null || !prepared || !Playback.IsPlaying) { return; }
            resumeAtMs = Position;
            StartCurrent();
        }

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            // Must promote to foreground right away: this service is started with startForegroundService.
            Promote();
            switch (intent?.Action)
            {
                case Playback.ActionPlay: StartCurrent(); break;
                case Playback.ActionToggle: Toggle(); break;
                case Playback.ActionNext: Advance(1); break;
                case Playback.ActionPrevious: Previous(); break;
            }
            return StartCommandResult.NotSticky;
        }

        void Previous()
        {
            if (Position > 3000) { SeekTo(0); } else { Advance(-1); }
        }

        void StartCurrent()
        {
            var song = Playback.Current;
            if (song == null) { Stop(); return; }
            if (song.StreamId == null) { MediaLibrary.AddRecent(this, song.Id); } else { StreamHistory.Add(this, song); }
            LoadSessionArt(song);

            player?.Release();
            prepared = false;
            player = new MediaPlayer();
            var attributes = new AudioAttributes.Builder()
                .SetContentType(AudioContentType.Music).SetUsage(AudioUsageKind.Media).Build();
            player.SetAudioAttributes(attributes);
            BitPerfect.Apply(this, attributes, song);
            Eq.Attach(this, player.AudioSessionId);
            player.SetWakeMode(this, WakeLockFlags.Partial);
            player.Prepared += (s, e) =>
            {
                prepared = true;
                if (resumeAtMs > 0) { player.SeekTo(resumeAtMs); resumeAtMs = 0; }
                if (RequestFocus()) { player.Start(); Playback.IsPlaying = true; }
                Refresh();
            };
            player.Completion += (s, e) => { if (Playback.Repeat == Playback.RepeatMode.One) { StartCurrent(); } else { Advance(1); } };
            // an unplayable file must not stall the queue
            player.Error += (s, e) => { e.Handled = true; Advance(1, skipUnplayable: true); };
            if (song.StreamId != null)
            {
                OpenStream(player, song);
                Refresh();
                return;
            }
            try
            {
                player.SetDataSource(this, song.Uri);
                player.PrepareAsync();
            }
            catch (Exception ex)
            {
                Logger.Debug("player could not open " + song.Title + ": " + ex.Message);
                Advance(1, skipUnplayable: true);
                return;
            }
            Refresh();
        }

        static readonly YoutubeExplode.YoutubeClient youtube = new YoutubeExplode.YoutubeClient(new System.Net.Http.HttpClient(new System.Net.Http.SocketsHttpHandler()));

        async void OpenStream(MediaPlayer target, Song song)
        {
            try
            {
                // off the main thread: Android's HTTP handler connects on the calling thread
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var manifest = await Task.Run(async () => await youtube.Videos.Streams.GetManifestAsync(song.StreamId));
                Logger.Debug("stream manifest in " + watch.ElapsedMilliseconds + " ms");
                // AAC in MP4 plays on every Android version; opus in WebM only when no MP4 stream is offered
                var audio = manifest.GetAudioOnlyStreams()
                    .OrderByDescending(s => s.Container == YoutubeExplode.Videos.Streams.Container.Mp4)
                    .ThenByDescending(s => s.Bitrate.BitsPerSecond)
                    .First();
                if (player != target) { return; } // skipped to another song meanwhile
                song.StreamFormat = (audio.Container == YoutubeExplode.Videos.Streams.Container.Mp4 ? "AAC" : "Opus") + " · " + (int)audio.Bitrate.KiloBitsPerSecond + " kbps";
                target.SetDataSource(audio.Url);
                target.PrepareAsync();
            }
            catch (Exception ex)
            {
                if (player != target) { return; }
                Logger.Debug("stream could not open " + song.Title + ": " + ex.Message);
                Toast.MakeText(this, GetString(Resource.String.stream_failed, song.Title), ToastLength.Short).Show();
                Advance(1, skipUnplayable: true);
            }
        }

        void Toggle()
        {
            if (player == null) { StartCurrent(); return; }
            if (Playback.IsPlaying)
            {
                player.Pause();
                Playback.IsPlaying = false;
                pausedByFocusLoss = false;
            }
            else if (prepared && RequestFocus())
            {
                player.Start();
                Playback.IsPlaying = true;
            }
            Refresh();
        }

        // skipUnplayable walks straight to the end: otherwise shuffle / repeat-all could loop forever over files that all fail.
        void Advance(int delta, bool skipUnplayable = false)
        {
            int count = Playback.Queue.Count;
            int next = Playback.Index + delta;
            if (delta > 0 && Playback.Shuffle && !skipUnplayable && count > 1)
            {
                do { next = random.Next(count); } while (next == Playback.Index);
            }
            if (next < 0) { next = 0; }
            if (next >= count)
            {
                if (Playback.Repeat == Playback.RepeatMode.All && !skipUnplayable) { next = 0; }
                else { Stop(); return; }
            }
            Playback.Index = next;
            StartCurrent();
        }

        public void SeekTo(int ms)
        {
            if (prepared)
            {
                player.SeekTo(ms);
                UpdateSessionState();
            }
        }

        void Stop()
        {
            Playback.IsPlaying = false;
            Playback.RaiseChanged();
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
        }

        AudioManager Audio => (AudioManager)GetSystemService(AudioService);

        bool RequestFocus() =>
#pragma warning disable CS0618 // the AudioFocusRequest builder needs API 26; minSdk is 23
            Audio.RequestAudioFocus(this, Android.Media.Stream.Music, AudioFocus.Gain) == AudioFocusRequest.Granted;
#pragma warning restore CS0618

        public void OnAudioFocusChange(AudioFocus focus)
        {
            if (player == null || !prepared) { return; }
            switch (focus)
            {
                case AudioFocus.Loss:
                case AudioFocus.LossTransient:
                    if (Playback.IsPlaying)
                    {
                        player.Pause();
                        Playback.IsPlaying = false;
                        pausedByFocusLoss = focus == AudioFocus.LossTransient;
                        Refresh();
                    }
                    break;
                case AudioFocus.LossTransientCanDuck:
                    player.SetVolume(0.2f, 0.2f);
                    break;
                case AudioFocus.Gain:
                    player.SetVolume(1f, 1f);
                    if (pausedByFocusLoss)
                    {
                        pausedByFocusLoss = false;
                        player.Start();
                        Playback.IsPlaying = true;
                        Refresh();
                    }
                    break;
            }
        }

        void Refresh()
        {
            UpdateSessionMetadata();
            UpdateSessionState();
            Promote();
            Playback.RaiseChanged();
        }

        void LoadSessionArt(Song song)
        {
            currentArt = null;
            int size = (int)(320 * Resources.DisplayMetrics.Density);
            Task.Run(() =>
            {
                var art = ArtLoader.ReadForSong(this, song, size);
                if (art == null) { return; }
                new Handler(Looper.MainLooper).Post(() =>
                {
                    if (Instance != this || Playback.Current?.Id != song.Id) { return; } // skipped to another song meanwhile
                    currentArt = art;
                    Refresh();
                });
            });
        }

        void UpdateSessionMetadata()
        {
            var song = Playback.Current;
            if (song == null) { return; }
            session.SetMetadata(new MediaMetadata.Builder()
                .PutString(MediaMetadata.MetadataKeyTitle, song.Title)
                .PutString(MediaMetadata.MetadataKeyArtist, song.Artist)
                .PutString(MediaMetadata.MetadataKeyAlbum, song.Album)
                .PutLong(MediaMetadata.MetadataKeyDuration, song.DurationMs)
                .PutBitmap(MediaMetadata.MetadataKeyAlbumArt, currentArt)
                .Build());
        }

        void UpdateSessionState()
        {
            session.SetPlaybackState(new PlaybackState.Builder()
                .SetActions(PlaybackState.ActionPlay | PlaybackState.ActionPause | PlaybackState.ActionPlayPause
                    | PlaybackState.ActionSkipToNext | PlaybackState.ActionSkipToPrevious | PlaybackState.ActionSeekTo)
                .SetState(Playback.IsPlaying ? PlaybackStateCode.Playing : PlaybackStateCode.Paused, Position, Playback.IsPlaying ? 1f : 0f)
                .Build());
        }

        void Promote()
        {
            var notification = BuildNotification();
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                StartForeground(NotifId, notification, Android.Content.PM.ForegroundService.TypeMediaPlayback);
            }
            else
            {
                StartForeground(NotifId, notification);
            }
        }

        Notification BuildNotification()
        {
            var song = Playback.Current;
            var open = PendingIntent.GetActivity(this, 0,
                new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop), PendingIntentFlags.Immutable);
#pragma warning disable CS0618 // the channel constructor needs API 26; minSdk is 23
            var builder = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(this, ChannelId) : new Notification.Builder(this);
#pragma warning restore CS0618
            // the platform builder, because only Notification.MediaStyle ties the notification to the session and gives the lock-screen player
            builder.SetSmallIcon(Resource.Drawable.ic_stat_soulseekicontransparent)
                .SetContentTitle(song?.Title ?? GetString(Resource.String.app_name))
                .SetContentText(song?.Artist)
                .SetContentIntent(open)
                .SetOnlyAlertOnce(true)
                .SetVisibility(NotificationVisibility.Public)
                .SetStyle(new Notification.MediaStyle().SetMediaSession(session.SessionToken).SetShowActionsInCompactView(0, 1, 2))
                .AddAction(PlayerAction(Resource.Drawable.ic_skip_previous_24, Resource.String.player_previous, Playback.ActionPrevious))
                .AddAction(PlayerAction(Playback.IsPlaying ? Resource.Drawable.ic_pause_24 : Resource.Drawable.ic_play_24,
                    Playback.IsPlaying ? Resource.String.player_pause : Resource.String.player_play, Playback.ActionToggle))
                .AddAction(PlayerAction(Resource.Drawable.ic_skip_next_24, Resource.String.player_next, Playback.ActionNext));
            if (currentArt != null) { builder.SetLargeIcon(currentArt); }
            return builder.Build();
        }

        Notification.Action PlayerAction(int icon, int title, string action) =>
            new Notification.Action.Builder(Android.Graphics.Drawables.Icon.CreateWithResource(this, icon), GetString(title), ActionIntent(action)).Build();

        PendingIntent ActionIntent(string action)
        {
            var intent = new Intent(this, typeof(PlayerService)).SetAction(action);
            const PendingIntentFlags flags = PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent;
            return OperatingSystem.IsAndroidVersionAtLeast(26)
                ? PendingIntent.GetForegroundService(this, 0, intent, flags)
                : PendingIntent.GetService(this, 0, intent, flags);
        }

        public override void OnDestroy()
        {
            UnregisterReceiver(noisyReceiver);
            session.Release();
            Eq.Release();
            player?.Release();
            player = null;
            Instance = null;
            Playback.IsPlaying = false;
            Playback.RaiseChanged();
            base.OnDestroy();
        }
    }
}
