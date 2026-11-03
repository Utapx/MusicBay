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
using Android.Content.PM;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Content;
using AndroidX.RecyclerView.Widget;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Seeker.Player
{
    /// <summary>Fills the music block at the top of Home: greeting, shortcuts, recently played and recently added.</summary>
    internal static class HomeMusicSection
    {
        const int TabBrowse = 3, TabLibrary = 4;

        public static void Bind(View root, Activity activity)
        {
            var greeting = root.FindViewById<TextView>(Resource.Id.home_greeting);
            if (greeting == null) { return; }
            greeting.Text = activity.GetString(GreetingFor(DateTime.Now.Hour));

            var empty = root.FindViewById<TextView>(Resource.Id.home_music_empty);
            if (greeting.Tag == null) // Bind runs on every resume; wire the clicks once
            {
                greeting.Tag = "bound";
                root.FindViewById(Resource.Id.home_tile_playlists).Click += (s, e) =>
                {
                    LibraryFragment.OpenOnPlaylists = true;
                    SeekerState.MainActivityRef.GoToTab(TabLibrary);
                };
                root.FindViewById(Resource.Id.home_tile_sound).Click += (s, e) => activity.StartActivity(new Android.Content.Intent(activity, typeof(SoundActivity)));
                root.FindViewById(Resource.Id.home_tile_browse).Click += (s, e) => SeekerState.MainActivityRef.GoToTab(TabBrowse);
                empty.Click += (s, e) => SeekerState.MainActivityRef.GoToTab(TabLibrary);
            }

            if (ContextCompat.CheckSelfPermission(activity, LibraryFragment.AudioPermission) != Permission.Granted)
            {
                ShowEmpty(root, empty, activity.GetString(Resource.String.library_no_permission));
                return;
            }
            Task.Run(() =>
            {
                MediaLibrary.ReloadCovers(activity);
                var added = MediaLibrary.Query(activity, "is_music != 0", "date_added DESC", 10);
                var played = MediaLibrary.Recent(activity);
                activity.RunOnUiThread(() =>
                {
                    if (added.Count == 0) { ShowEmpty(root, empty, activity.GetString(Resource.String.library_empty)); return; }
                    empty.Visibility = ViewStates.Gone;
                    Fill(root, activity, Resource.Id.home_played_group, Resource.Id.home_played_list, played);
                    Fill(root, activity, Resource.Id.home_added_group, Resource.Id.home_added_list, added);
                });
            });
        }

        static int GreetingFor(int hour) =>
            hour >= 5 && hour < 12 ? Resource.String.greeting_morning :
            hour >= 12 && hour < 18 ? Resource.String.greeting_afternoon :
            hour >= 18 && hour < 22 ? Resource.String.greeting_evening :
            Resource.String.greeting_night;

        static void ShowEmpty(View root, TextView empty, string text)
        {
            root.FindViewById(Resource.Id.home_played_group).Visibility = ViewStates.Gone;
            root.FindViewById(Resource.Id.home_added_group).Visibility = ViewStates.Gone;
            empty.Text = text;
            empty.Visibility = ViewStates.Visible;
        }

        static void Fill(View root, Activity activity, int groupId, int listId, List<Song> songs)
        {
            var group = root.FindViewById(groupId);
            group.Visibility = songs.Count == 0 ? ViewStates.Gone : ViewStates.Visible;
            if (songs.Count == 0) { return; }
            var list = root.FindViewById<RecyclerView>(listId);
            list.SetLayoutManager(new LinearLayoutManager(activity, LinearLayoutManager.Horizontal, false));
            list.SetAdapter(new SongAdapter(songs, position => Playback.Play(activity, songs, position), null, Resource.Layout.card_song));
        }
    }
}
