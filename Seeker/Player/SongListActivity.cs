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
using Seeker.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Seeker.Player
{
    /// <summary>The songs of one album (a folder) or one artist.</summary>
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", Exported = false, ParentActivity = typeof(MainActivity))]
    public class SongListActivity : ThemeableActivity
    {
        public const string ExtraFolder = "album_folder";
        public const string ExtraArtist = "artist_name";
        public const string ExtraStreamBrowseId = "stream_browse_id";
        public const string ExtraStreamTitle = "stream_title";
        public const string ExtraStreamArtist = "stream_artist";
        public const string ExtraStreamArt = "stream_art";
        const int CoverDp = 240;

        List<Song> songs = new List<Song>();
        RecyclerView list;
        TextView name, detail, empty;
        ImageView cover;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_playlist);
            name = FindViewById<TextView>(Resource.Id.playlist_name);
            detail = FindViewById<TextView>(Resource.Id.playlist_detail);
            empty = FindViewById<TextView>(Resource.Id.playlist_empty);
            cover = FindViewById<ImageView>(Resource.Id.playlist_cover);
            list = FindViewById<RecyclerView>(Resource.Id.playlist_list);
            list.SetLayoutManager(new LinearLayoutManager(this));
            list.AddItemDecoration(new InsetDivider(this));

            FindViewById(Resource.Id.playlist_back).Click += (s, e) => Finish();
            FindViewById(Resource.Id.playlist_more).Click += (s, e) => { if (songs.Count > 0) { SongActions.AddToPlaylist(this, songs); } };
            FindViewById<ImageButton>(Resource.Id.playlist_more).SetImageResource(Resource.Drawable.ic_playlist_add_24);
            FindViewById(Resource.Id.playlist_more).ContentDescription = GetString(Resource.String.playlist_add_to);
            FindViewById(Resource.Id.playlist_play).Click += (s, e) => { if (songs.Count > 0) { Playback.Shuffle = false; PlayFrom(0); } };
            FindViewById(Resource.Id.playlist_shuffle).Click += (s, e) => { if (songs.Count > 0) { Playback.Shuffle = true; PlayFrom(new Random().Next(songs.Count)); } };
        }

        protected override void OnResume()
        {
            base.OnResume();
            if (Intent.GetStringExtra(ExtraStreamBrowseId) != null) { LoadStream(); return; }
            string folder = Intent.GetStringExtra(ExtraFolder);
            string artist = Intent.GetStringExtra(ExtraArtist);
            Task.Run(() =>
            {
                var all = MediaLibrary.Query(this, "is_music != 0", "title COLLATE NOCASE ASC");
                var album = folder == null ? null : MediaLibrary.Albums(this, all.Where(s => s.Folder == folder).ToList()).FirstOrDefault();
                var found = album != null ? album.Songs : all.Where(s => s.Artist == artist).ToList();
                RunOnUiThread(() =>
                {
                    if (IsFinishing || IsDestroyed) { return; }
                    songs = found;
                    Show(album, artist);
                });
            });
        }

        void Show(MediaLibrary.Album album, string artist)
        {
            name.Text = album?.Title ?? artist;
            string count = Resources.GetQuantityString(Resource.Plurals.song_count, songs.Count, songs.Count) + " · " + PlayerFormat.Duration(songs.Sum(s => s.DurationMs));
            detail.Text = album == null ? count : album.Artist + " · " + count;
            empty.Visibility = songs.Count == 0 ? ViewStates.Visible : ViewStates.Gone;
            empty.Text = GetString(Resource.String.library_empty);
            if (album != null)
            {
                cover.Visibility = ViewStates.Visible;
                ArtLoader.LoadAlbum(cover, album, CoverDp);
            }
            list.SetAdapter(new SongAdapter(songs, PlayFrom, position => songs[position].Id == Playback.Current?.Id, Resource.Layout.row_song,
                (anchor, position) => SongActions.ShowMenu(this, anchor, songs[position])));
        }

        async void LoadStream()
        {
            if (songs.Count > 0) { return; } // already loaded; coming back from Now Playing should not fetch again
            YtAccount.Load(this); // the app may have been restarted straight onto this page
            FindViewById(Resource.Id.playlist_more).Visibility = ViewStates.Gone; // playlists hold phone songs only
            name.Text = Intent.GetStringExtra(ExtraStreamTitle);
            string art = Intent.GetStringExtra(ExtraStreamArt);
            if (art != null)
            {
                cover.Visibility = ViewStates.Visible;
                ArtLoader.LoadUrl(cover, art, CoverDp);
            }
            try
            {
                var found = await Common.YtMusic.SongsAsync(Intent.GetStringExtra(ExtraStreamBrowseId));
                if (IsFinishing || IsDestroyed) { return; }
                songs = found.Select(Song.FromStream).ToList();
                string artist = Intent.GetStringExtra(ExtraStreamArtist);
                string count = Resources.GetQuantityString(Resource.Plurals.song_count, songs.Count, songs.Count);
                detail.Text = string.IsNullOrEmpty(artist) ? count : artist + " · " + count;
                empty.Visibility = songs.Count == 0 ? ViewStates.Visible : ViewStates.Gone;
                empty.Text = GetString(Resource.String.library_empty);
                list.SetAdapter(new SongAdapter(songs, PlayFrom, position => songs[position].Id == Playback.Current?.Id, Resource.Layout.row_song,
                    (anchor, position) => SongActions.ShowMenu(this, anchor, songs[position])));
            }
            catch (Exception ex)
            {
                if (IsFinishing || IsDestroyed) { return; }
                Logger.Debug("YouTube Music page failed: " + ex.Message);
                empty.Visibility = ViewStates.Visible;
                empty.Text = GetString(Resource.String.stream_search_failed);
            }
        }

        void PlayFrom(int position)
        {
            Playback.Play(this, songs, position);
            StartActivity(new Intent(this, typeof(NowPlayingActivity)));
        }
    }
}
