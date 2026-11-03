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
using Android.Content.PM;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Content;
using AndroidX.RecyclerView.Widget;
using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Seeker.Player
{
    /// <summary>Home: an avatar and filters on top, then quick tiles, recent songs, albums and playlists.</summary>
    internal static class HomeMusicSection
    {
        const int TabLibrary = 4;
        const int QuickTileCount = 6;
        const int RowCardWidthDp = 150;
        const int CardArtDp = 200;

        static List<MediaLibrary.Album> albums = new List<MediaLibrary.Album>();

        public static void Bind(View root, Activity activity)
        {
            var filters = root.FindViewById<RadioGroup>(Resource.Id.home_filters);
            var empty = root.FindViewById<TextView>(Resource.Id.home_music_empty);
            if (filters.Tag == null) // Bind runs on every resume; wire the clicks once
            {
                filters.Tag = "bound";
                filters.CheckedChange += (s, e) => ShowFilter(root, activity, e.CheckedId);
                empty.Click += (s, e) => SeekerState.MainActivityRef.GoToTab(TabLibrary);
                root.FindViewById(Resource.Id.home_avatar).Click += (s, e) => SeekerState.LoginFragmentRef?.OpenAccountPanel();
                root.FindViewById(Resource.Id.home_signin).Click += (s, e) => SeekerState.LoginFragmentRef?.OpenAccountPanel();
            }
            UpdateAccount(root);

            if (ContextCompat.CheckSelfPermission(activity, LibraryFragment.AudioPermission) != Permission.Granted)
            {
                ShowEmpty(root, empty, activity.GetString(Resource.String.library_no_permission));
                return;
            }
            Task.Run(() =>
            {
                MediaLibrary.ReloadCovers(activity);
                var all = MediaLibrary.Query(activity, "is_music != 0", "title COLLATE NOCASE ASC");
                var added = MediaLibrary.Query(activity, "is_music != 0", "date_added DESC", 10);
                var played = MediaLibrary.Recent(activity);
                var allAlbums = MediaLibrary.Albums(activity, all);
                activity.RunOnUiThread(() =>
                {
                    albums = allAlbums;
                    if (added.Count == 0) { ShowEmpty(root, empty, activity.GetString(Resource.String.library_empty)); return; }
                    empty.Visibility = ViewStates.Gone;
                    Fill(root, activity, added, played);
                    ShowFilter(root, activity, filters.CheckedRadioButtonId);
                });
            });
        }

        public static void UpdateAccount(View root)
        {
            bool signedIn = !Seeker.Services.SessionService.Instance.IsNotLoggedIn();
            string name = signedIn ? PreferencesState.Username : null;
            bool hasInitial = !string.IsNullOrEmpty(name);
            var initial = root.FindViewById<TextView>(Resource.Id.home_avatar_initial);
            initial.Text = hasInitial ? name.Substring(0, 1).ToUpperInvariant() : "";
            initial.Visibility = hasInitial ? ViewStates.Visible : ViewStates.Gone;
            root.FindViewById(Resource.Id.home_avatar_icon).Visibility = hasInitial ? ViewStates.Gone : ViewStates.Visible;
            root.FindViewById(Resource.Id.home_signin).Visibility = signedIn ? ViewStates.Gone : ViewStates.Visible;
        }

        static void ShowEmpty(View root, TextView empty, string text)
        {
            root.FindViewById(Resource.Id.home_sections).Visibility = ViewStates.Gone;
            root.FindViewById(Resource.Id.home_filtered_list).Visibility = ViewStates.Gone;
            empty.Text = text;
            empty.Visibility = ViewStates.Visible;
        }

        static void Fill(View root, Activity activity, List<Song> added, List<Song> played)
        {
            var playlists = Playlists.All(activity);
            // adapters are only replaced when something changed, so returning to Home keeps the row positions
            var quickList = root.FindViewById<RecyclerView>(Resource.Id.home_quick_list);
            string signature = string.Join(",", played.Select(s => s.Id)) + "|" + string.Join(",", added.Select(s => s.Id))
                + "|" + albums.Count + "|" + string.Join(",", playlists.Select(p => p.Name + p.SongIds.Count));
            if (quickList.Tag?.ToString() == signature) { return; }
            quickList.Tag = signature;

            var quick = played.Concat(added).GroupBy(s => s.Id).Select(g => g.First()).Take(QuickTileCount).ToList();
            quickList.NestedScrollingEnabled = false;
            quickList.SetLayoutManager(new GridLayoutManager(activity, 2));
            quickList.SetAdapter(new SongAdapter(quick, position => Playback.Play(activity, quick, position), null, Resource.Layout.card_quick));

            FillSongs(root, activity, Resource.Id.home_played_group, Resource.Id.home_played_list, played);
            FillSongs(root, activity, Resource.Id.home_added_group, Resource.Id.home_added_list, added);
            FillRow(root, activity, Resource.Id.home_albums_group, Resource.Id.home_albums_list, albums.Count, AlbumCards(activity, albums, RowCardWidthDp));
            FillRow(root, activity, Resource.Id.home_playlists_group, Resource.Id.home_playlists_list, playlists.Count, PlaylistCards(activity, playlists, RowCardWidthDp));
        }

        static void ShowFilter(View root, Activity activity, int checkedId)
        {
            var sections = root.FindViewById(Resource.Id.home_sections);
            var filtered = root.FindViewById<RecyclerView>(Resource.Id.home_filtered_list);
            var empty = root.FindViewById<TextView>(Resource.Id.home_music_empty);
            empty.Visibility = ViewStates.Gone;
            if (checkedId == Resource.Id.home_filter_all)
            {
                sections.Visibility = ViewStates.Visible;
                filtered.Visibility = ViewStates.Gone;
                return;
            }
            var playlists = Playlists.All(activity);
            bool showAlbums = checkedId == Resource.Id.home_filter_albums;
            sections.Visibility = ViewStates.Gone;
            filtered.NestedScrollingEnabled = false;
            filtered.SetLayoutManager(new GridLayoutManager(activity, 2));
            filtered.SetAdapter(showAlbums ? AlbumCards(activity, albums, 0) : PlaylistCards(activity, playlists, 0));
            bool nothing = showAlbums ? albums.Count == 0 : playlists.Count == 0;
            filtered.Visibility = nothing ? ViewStates.Gone : ViewStates.Visible;
            if (nothing)
            {
                empty.Text = activity.GetString(showAlbums ? Resource.String.library_empty : Resource.String.playlists_empty);
                empty.Visibility = ViewStates.Visible;
            }
        }

        static CoverCardAdapter AlbumCards(Context context, List<MediaLibrary.Album> items, int widthDp) =>
            new CoverCardAdapter(items.Count, i => items[i].Title, i => items[i].Artist,
                (art, i) => ArtLoader.LoadAlbum(art, items[i], CardArtDp),
                i => context.StartActivity(new Intent(context, typeof(SongListActivity)).PutExtra(SongListActivity.ExtraFolder, items[i].Folder)),
                widthDp);

        static CoverCardAdapter PlaylistCards(Context context, List<Playlist> items, int widthDp)
        {
            var firstSongs = items.Select(p => Playlists.Songs(context, p).FirstOrDefault()).ToList();
            return new CoverCardAdapter(items.Count, i => items[i].Name,
                i => context.Resources.GetQuantityString(Resource.Plurals.song_count, items[i].SongIds.Count, items[i].SongIds.Count),
                (art, i) =>
                {
                    if (firstSongs[i] != null) { ArtLoader.Load(art, firstSongs[i], CardArtDp); return; }
                    int pad = (int)(40 * context.Resources.DisplayMetrics.Density);
                    art.SetPadding(pad, pad, pad, pad);
                    art.SetImageResource(Resource.Drawable.ic_queue_music_24);
                },
                i => context.StartActivity(new Intent(context, typeof(PlaylistActivity)).PutExtra(PlaylistActivity.ExtraId, items[i].Id)),
                widthDp);
        }

        static void FillSongs(View root, Activity activity, int groupId, int listId, List<Song> songs)
        {
            FillRow(root, activity, groupId, listId, songs.Count,
                new SongAdapter(songs, position => Playback.Play(activity, songs, position), null, Resource.Layout.card_song));
        }

        static void FillRow(View root, Activity activity, int groupId, int listId, int count, RecyclerView.Adapter adapter)
        {
            root.FindViewById(groupId).Visibility = count == 0 ? ViewStates.Gone : ViewStates.Visible;
            if (count == 0) { return; }
            var list = root.FindViewById<RecyclerView>(listId);
            list.SetLayoutManager(new LinearLayoutManager(activity, LinearLayoutManager.Horizontal, false));
            list.SetAdapter(adapter);
        }
    }

    /// <summary>A square cover with two lines of text: albums and playlists, in rows (fixed width) or grids (widthDp 0).</summary>
    internal class CoverCardAdapter : RecyclerView.Adapter
    {
        const int RowGapDp = 12;

        readonly int count;
        readonly Func<int, string> title, subtitle;
        readonly Action<ImageView, int> bindArt;
        readonly Action<int> onClick;
        readonly int widthDp;

        public CoverCardAdapter(int count, Func<int, string> title, Func<int, string> subtitle, Action<ImageView, int> bindArt, Action<int> onClick, int widthDp)
        {
            this.count = count;
            this.title = title;
            this.subtitle = subtitle;
            this.bindArt = bindArt;
            this.onClick = onClick;
            this.widthDp = widthDp;
        }

        public override int ItemCount => count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var card = LayoutInflater.From(parent.Context).Inflate(Resource.Layout.card_album, parent, false);
            if (widthDp > 0)
            {
                // in a row the card lines up with the song cards: no inner padding, only the gap to the next one
                float density = parent.Resources.DisplayMetrics.Density;
                int gap = (int)(RowGapDp * density);
                card.SetPadding(0, 0, gap, 0);
                card.LayoutParameters.Width = (int)(widthDp * density) + gap;
            }
            var holder = new CardHolder(card);
            card.Click += (s, e) => { if (holder.BindingAdapterPosition >= 0) { onClick(holder.BindingAdapterPosition); } };
            return holder;
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder viewHolder, int position)
        {
            var holder = (CardHolder)viewHolder;
            holder.Title.Text = title(position);
            holder.Subtitle.Text = subtitle(position);
            bindArt(holder.Art, position);
        }

        class CardHolder : RecyclerView.ViewHolder
        {
            public TextView Title, Subtitle;
            public ImageView Art;
            public CardHolder(View v) : base(v)
            {
                Title = v.FindViewById<TextView>(Resource.Id.album_title);
                Subtitle = v.FindViewById<TextView>(Resource.Id.album_artist);
                Art = v.FindViewById<ImageView>(Resource.Id.album_art);
            }
        }
    }
}
