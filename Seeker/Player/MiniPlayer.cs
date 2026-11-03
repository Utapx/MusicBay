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
using Android.OS;
using Android.Views;
using Android.Widget;
using System;

namespace Seeker.Player
{
    /// <summary>Binds the mini player strip in activity_main to Playback state.</summary>
    public static class MiniPlayer
    {
        const int ArtDp = 40;
        static Action detach;

        public static void Attach(Activity activity)
        {
            detach?.Invoke(); // the activity may be recreated (rotation) while a previous binding is still subscribed

            var root = activity.FindViewById(Resource.Id.mini_player);
            var title = activity.FindViewById<TextView>(Resource.Id.mini_title);
            var artist = activity.FindViewById<TextView>(Resource.Id.mini_artist);
            var progress = activity.FindViewById<ProgressBar>(Resource.Id.mini_progress);
            var play = activity.FindViewById<ImageButton>(Resource.Id.mini_play);
            var next = activity.FindViewById<ImageButton>(Resource.Id.mini_next);
            var art = activity.FindViewById<ImageView>(Resource.Id.mini_art);
            var output = activity.FindViewById<TextView>(Resource.Id.mini_output);
            var handler = new Handler(Looper.MainLooper);
            bool alive = true;
            long shownSongId = -1;

            void RefreshOutput()
            {
                var info = AudioOutput.Current(activity);
                output.Text = info.Label;
                output.Visibility = info.External ? ViewStates.Visible : ViewStates.Gone;
            }

            void Refresh()
            {
                var song = Playback.Current;
                root.Visibility = song == null ? ViewStates.Gone : ViewStates.Visible;
                if (song == null) { return; }
                if (song.Id != shownSongId)
                {
                    shownSongId = song.Id;
                    ArtLoader.Load(art, song, ArtDp);
                }
                RefreshOutput();
                title.Text = song.Title;
                artist.Text = song.Artist;
                progress.Max = (int)Math.Max(song.DurationMs, 1);
                play.SetImageResource(Playback.IsPlaying ? Resource.Drawable.ic_pause_24 : Resource.Drawable.ic_play_24);
                play.ContentDescription = activity.GetString(Playback.IsPlaying ? Resource.String.player_pause : Resource.String.player_play);
            }

            void Tick()
            {
                if (!alive) { return; }
                if (Playback.IsPlaying) { progress.Progress = Playback.PositionMs; }
                if (Playback.Current != null) { RefreshOutput(); }
                handler.PostDelayed(Tick, 500);
            }

            root.Click += (s, e) => activity.StartActivity(new Android.Content.Intent(activity, typeof(NowPlayingActivity)));
            play.Click += (s, e) => Playback.Send(activity, Playback.ActionToggle);
            next.Click += (s, e) => Playback.Send(activity, Playback.ActionNext);
            Playback.Changed += Refresh;
            detach = () => { alive = false; Playback.Changed -= Refresh; };

            Refresh();
            Tick();
        }
    }
}
