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
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Seeker.Player
{
    // shortcut: no mini player on this screen, playing opens Now Playing instead. Share the strip across activities if it gets in the way.
    /// <summary>One playlist: play or shuffle it, reorder by long-press drag, remove songs, rename or delete.</summary>
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", Exported = false, ParentActivity = typeof(MainActivity))]
    public class PlaylistActivity : ThemeableActivity
    {
        public const string ExtraId = "playlist_id";
        const int MenuRename = 1;
        const int MenuDelete = 2;

        Playlist playlist;
        List<Song> songs = new List<Song>();
        RecyclerView list;
        TextView name, detail, empty;
        View playButton, shuffleButton;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_playlist);
            playlist = Playlists.Find(this, Intent.GetLongExtra(ExtraId, -1));
            if (playlist == null) { Finish(); return; }

            name = FindViewById<TextView>(Resource.Id.playlist_name);
            detail = FindViewById<TextView>(Resource.Id.playlist_detail);
            empty = FindViewById<TextView>(Resource.Id.playlist_empty);
            list = FindViewById<RecyclerView>(Resource.Id.playlist_list);
            list.SetLayoutManager(new LinearLayoutManager(this));
            list.AddItemDecoration(new InsetDivider(this));
            new ItemTouchHelper(new Reorder(this)).AttachToRecyclerView(list);

            playButton = FindViewById(Resource.Id.playlist_play);
            shuffleButton = FindViewById(Resource.Id.playlist_shuffle);
            playButton.Click += (s, e) => { Playback.Shuffle = false; PlayFrom(0); };
            shuffleButton.Click += (s, e) => { Playback.Shuffle = true; PlayFrom(new Random().Next(songs.Count)); };
            FindViewById(Resource.Id.playlist_back).Click += (s, e) => Finish();
            FindViewById(Resource.Id.playlist_more).Click += (s, e) => ShowMenu((View)s);
        }

        protected override void OnResume()
        {
            base.OnResume();
            if (playlist == null) { return; }
            songs = Playlists.Songs(this, playlist);
            list.SetAdapter(new SongAdapter(songs, PlayFrom, position => songs[position].Id == Playback.Current?.Id, Resource.Layout.row_song,
                (anchor, position) => SongActions.ShowMenu(this, anchor, songs[position], () => Remove(position))));
            RefreshHeader();
        }

        void RefreshHeader()
        {
            name.Text = playlist.Name;
            long totalMs = songs.Sum(s => s.DurationMs);
            detail.Text = Resources.GetQuantityString(Resource.Plurals.song_count, songs.Count, songs.Count) + " · " + PlayerFormat.Duration(totalMs);
            bool hasSongs = songs.Count > 0;
            empty.Visibility = hasSongs ? ViewStates.Gone : ViewStates.Visible;
            playButton.Enabled = hasSongs;
            shuffleButton.Enabled = hasSongs;
        }

        void PlayFrom(int position)
        {
            Playback.Play(this, new List<Song>(songs), position);
            StartActivity(new Intent(this, typeof(NowPlayingActivity)));
        }

        void Remove(int position)
        {
            Playlists.Remove(this, playlist, songs[position].Id);
            songs.RemoveAt(position);
            list.GetAdapter().NotifyItemRemoved(position);
            RefreshHeader();
        }

        void ShowMenu(View anchor)
        {
            var popup = new PopupMenu(this, anchor);
            popup.Menu.Add(0, MenuRename, 0, Resource.String.playlist_rename);
            popup.Menu.Add(0, MenuDelete, 1, Resource.String.playlist_delete);
            popup.MenuItemClick += (s, e) =>
            {
                if (e.Item.ItemId == MenuRename)
                {
                    SongActions.PromptName(this, Resource.String.playlist_rename, playlist.Name, newName =>
                    {
                        Playlists.Rename(this, playlist, newName);
                        RefreshHeader();
                    });
                }
                else
                {
                    new Google.Android.Material.Dialog.MaterialAlertDialogBuilder(this)
                        .SetMessage(GetString(Resource.String.playlist_delete_confirm, playlist.Name))
                        .SetPositiveButton(Resource.String.playlist_delete, (a, b) => { Playlists.Delete(this, playlist); Finish(); })
                        .SetNegativeButton(Android.Resource.String.Cancel, (a, b) => { })
                        .Show();
                }
            };
            popup.Show();
        }

        class Reorder : ItemTouchHelper.SimpleCallback
        {
            readonly PlaylistActivity activity;

            public Reorder(PlaylistActivity activity) : base(ItemTouchHelper.Up | ItemTouchHelper.Down, 0)
            {
                this.activity = activity;
            }

            public override bool OnMove(RecyclerView recyclerView, RecyclerView.ViewHolder from, RecyclerView.ViewHolder to)
            {
                int a = from.BindingAdapterPosition, b = to.BindingAdapterPosition;
                if (a < 0 || b < 0) { return false; }
                var songs = activity.songs;
                Playlists.Move(activity, activity.playlist, songs[a].Id, songs[b].Id);
                var moved = songs[a];
                songs.RemoveAt(a);
                songs.Insert(b, moved);
                recyclerView.GetAdapter().NotifyItemMoved(a, b);
                return true;
            }

            public override void OnSwiped(RecyclerView.ViewHolder viewHolder, int direction) { }
        }
    }
}
