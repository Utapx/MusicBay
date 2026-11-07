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

using Android.Content;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.Fragment.App;
using AndroidX.RecyclerView.Widget;
using Google.Android.Material.Dialog;
using Common;
using Seeker.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Seeker.Player
{
    /// <summary>
    /// Streaming mode: search YouTube Music and stream from it. An extra reached from the Search menu rather than a tab.
    /// Before a search it reads like Home (recently played, albums, playlists); a search shows everything, or just albums.
    /// </summary>
    public class StreamFragment : Fragment
    {
        /// <summary>Pager page; it has no tab and shows under Search.</summary>
        public const int Page = 5;
        /// <summary>While on, the Search tab opens streaming mode instead of Soulseek search.</summary>
        public static bool Active;
        const int SoulseekSearchPage = 1;
        const int AlbumArtDp = 200;
        const int RowCardWidthDp = 150; // as Home's rows
        const int MinAlbumColumnDp = 160;
        const int SignInRequest = 9201;

        // The first segment is "Recently played" before a search and "All" during one; Playlists only exists before.
        enum Section { First, Albums, Playlists }

        RecyclerView list;
        TextView message, count;
        Button messageAction;
        ImageButton action, clearSearch, account;
        RadioButton firstChip;
        RadioGroup sections;
        View playlistsChip;
        EditText search;
        Section section = Section.First;
        string shownQuery; // the search being shown, or null before one
        Action onMessageAction;
        List<Song> songs = new List<Song>();
        CancellationTokenSource pending;
        long highlightedSongId = -1;

        bool Searching => shownQuery != null;

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            var view = inflater.Inflate(Resource.Layout.fragment_library, container, false);
            list = view.FindViewById<RecyclerView>(Resource.Id.library_list);
            message = view.FindViewById<TextView>(Resource.Id.library_message);
            count = view.FindViewById<TextView>(Resource.Id.library_count);
            messageAction = view.FindViewById<Button>(Resource.Id.library_grant);
            messageAction.Click += (s, e) => onMessageAction?.Invoke();
            action = view.FindViewById<ImageButton>(Resource.Id.library_action);
            action.Click += (s, e) => ShuffleAll();
            YtAccount.Load(Context);
            AddBackButton(view);
            account = view.FindViewById<ImageButton>(Resource.Id.library_folders);
            account.SetImageResource(Resource.Drawable.ic_person_24);
            account.ContentDescription = GetString(Resource.String.stream_account);
            account.Click += (s, e) => { if (YtAccount.SignedIn) { ConfirmSignOut(); } else { WarnBeforeSignIn(); } };

            search = view.FindViewById<EditText>(Resource.Id.library_search);
            search.Hint = GetString(Resource.String.stream_search_hint);
            clearSearch = view.FindViewById<ImageButton>(Resource.Id.library_search_clear);
            clearSearch.Click += (s, e) => search.Text = "";
            search.TextChanged += (s, e) =>
            {
                clearSearch.Visibility = search.Text.Length == 0 ? ViewStates.Gone : ViewStates.Visible;
                if (search.Text.Length == 0 && Searching) { shownQuery = null; UpdateSections(); Load(); }
            };
            // each search is a request to YouTube, so it runs on the keyboard's search key rather than per letter
            search.EditorAction += (s, e) => { HideKeyboard(); SubmitSearch(); };

            list.AddItemDecoration(new InsetDivider(Context));
            firstChip = view.FindViewById<RadioButton>(Resource.Id.library_chip_songs);
            playlistsChip = view.FindViewById(Resource.Id.library_chip_playlists);
            view.FindViewById(Resource.Id.library_chip_artists).Visibility = ViewStates.Gone;
            sections = view.FindViewById<RadioGroup>(Resource.Id.library_sections);
            sections.CheckedChange += (s, e) =>
            {
                var picked = e.CheckedId == Resource.Id.library_chip_albums ? Section.Albums
                    : e.CheckedId == Resource.Id.library_chip_playlists ? Section.Playlists
                    : Section.First;
                if (picked == section) { return; }
                section = picked;
                Load();
            };
            UpdateSections();
            Load();
            return view;
        }

        public override void OnResume()
        {
            base.OnResume();
            Playback.Changed += OnPlaybackChanged;
            if (!Searching && section != Section.Playlists) { Load(); } // picks up songs played and albums opened since
        }

        public override void OnPause()
        {
            Playback.Changed -= OnPlaybackChanged;
            base.OnPause();
        }

        /// <summary>Explains streaming mode, then switches to it.</summary>
        public static void Enter(Android.App.Activity activity)
        {
            new MaterialAlertDialogBuilder(activity)
                .SetTitle(Resource.String.stream_mode_title)
                .SetMessage(Resource.String.stream_mode_message)
                .SetPositiveButton(Resource.String.stream_enter, (s, e) => Pager(activity)?.SetCurrentItem(Page, false))
                .SetNegativeButton(Android.Resource.String.Cancel, (s, e) => { })
                .Show();
        }

        static AndroidX.ViewPager2.Widget.ViewPager2 Pager(Android.App.Activity activity) => activity?.FindViewById<AndroidX.ViewPager2.Widget.ViewPager2>(Resource.Id.pager);

        // the layout is the library's, whose search row starts at the field; streaming mode needs a way back first
        void AddBackButton(View view)
        {
            var row = (LinearLayout)view.FindViewById(Resource.Id.library_search).Parent.Parent;
            float density = Resources.DisplayMetrics.Density;
            var back = new ImageButton(Context) { ContentDescription = GetString(Resource.String.stream_back) };
            back.SetImageResource(Resource.Drawable.ic_arrow_back_24);
            back.SetBackgroundResource(Resource.Drawable.bg_circle_button);
            back.ImageTintList = Android.Content.Res.ColorStateList.ValueOf(new Android.Graphics.Color(PlayerFormat.ThemeColor(Context, Resource.Attribute.cellTextColor)));
            back.Click += (s, e) => { HideKeyboard(); Pager(Activity)?.SetCurrentItem(SoulseekSearchPage, false); };
            var size = new LinearLayout.LayoutParams((int)(44 * density), (int)(44 * density)) { MarginEnd = (int)(8 * density) };
            row.AddView(back, 0, size);
        }

        void UpdateSections()
        {
            firstChip.SetText(Searching ? Resource.String.stream_all : Resource.String.home_recently_played);
            playlistsChip.Visibility = Searching ? ViewStates.Gone : ViewStates.Visible;
        }

        void SubmitSearch()
        {
            if (search.Text.Trim().Length == 0) { return; }
            shownQuery = search.Text.Trim();
            UpdateSections();
            if (section == Section.Playlists) { sections.Check(Resource.Id.library_chip_songs); } // loads through CheckedChange
            else { Load(); }
        }

        // Shown before every sign-in, not only the first: the risk is to the account, and the account can differ each time.
        void WarnBeforeSignIn()
        {
            new MaterialAlertDialogBuilder(Context)
                .SetTitle(Resource.String.stream_signin_title)
                .SetMessage(Resource.String.stream_signin_message)
                .SetPositiveButton(Resource.String.stream_signin_continue, (s, e) => StartActivityForResult(new Intent(Context, typeof(YtLoginActivity)), SignInRequest))
                .SetNegativeButton(Android.Resource.String.Cancel, (s, e) => { })
                .Show();
        }

        void ConfirmSignOut()
        {
            new MaterialAlertDialogBuilder(Context)
                .SetTitle(Resource.String.stream_source)
                .SetMessage(Resource.String.stream_signed_in_message)
                .SetPositiveButton(Resource.String.stream_sign_out, (s, e) => { YtAccount.SignOut(Context); if (section == Section.Playlists) { Load(); } })
                .SetNegativeButton(Android.Resource.String.Cancel, (s, e) => { })
                .Show();
        }

        public override void OnActivityResult(int requestCode, int resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode != SignInRequest || !YtAccount.SignedIn) { return; }
            Toast.MakeText(Context, Resource.String.stream_signed_in, ToastLength.Short).Show();
            search.Text = "";
            if (section == Section.Playlists) { Load(); } else { sections.Check(Resource.Id.library_chip_playlists); }
        }

        void OnPlaybackChanged()
        {
            long current = Playback.Current?.Id ?? -1;
            if (current == highlightedSongId) { return; }
            highlightedSongId = current;
            if (section == Section.First) { list.GetAdapter()?.NotifyDataSetChanged(); }
        }

        void HideKeyboard()
        {
            var keyboard = (InputMethodManager)Context?.GetSystemService(Context.InputMethodService);
            keyboard?.HideSoftInputFromWindow(search.WindowToken, HideSoftInputFlags.None);
            search.ClearFocus();
        }

        async void Load()
        {
            pending?.Cancel();
            if (!Searching && section == Section.First) { ShowRecent(); return; }
            if (!Searching && section == Section.Albums) { ShowCards(StreamHistory.Albums(Context), GetString(Resource.String.stream_albums_empty)); return; }
            if (!Searching && !YtAccount.SignedIn)
            {
                Show(GetString(Resource.String.stream_playlists_signin), GetString(Resource.String.stream_sign_in), WarnBeforeSignIn);
                return;
            }

            var token = (pending = new CancellationTokenSource()).Token;
            string query = shownQuery;
            var loading = section;
            Show(GetString(Resource.String.stream_searching));
            try
            {
                if (loading == Section.Playlists)
                {
                    var playlists = await YtMusic.LibraryPlaylistsAsync(token);
                    if (!token.IsCancellationRequested && IsAdded) { ShowCards(playlists, null); }
                }
                else if (loading == Section.Albums)
                {
                    var albums = await YtMusic.SearchAsync(query, YtKind.Album, token);
                    if (!token.IsCancellationRequested && IsAdded) { ShowCards(albums, GetString(Resource.String.library_no_results, query)); }
                }
                else
                {
                    // both at once: songs and albums are separate searches on YouTube Music
                    var songsTask = YtMusic.SearchAsync(query, YtKind.Song, token);
                    var albumsTask = YtMusic.SearchAsync(query, YtKind.Album, token);
                    await Task.WhenAll(songsTask, albumsTask);
                    if (!token.IsCancellationRequested && IsAdded) { ShowAll(query, songsTask.Result, albumsTask.Result); }
                }
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested || !IsAdded) { return; }
                Logger.Debug("YouTube Music request failed: " + ex.Message);
                Show(GetString(Resource.String.stream_search_failed));
            }
        }

        // Before a search: what was streamed lately, in Home's tiles, so a song is one tap away again.
        void ShowRecent()
        {
            var recent = songs = StreamHistory.All(Context);
            if (recent.Count == 0) { Show(GetString(Resource.String.stream_intro)); return; }
            int edge = (int)(12 * Resources.DisplayMetrics.Density); // as Home's tiles
            list.SetPadding(edge, 0, edge, list.PaddingBottom);
            list.SetLayoutManager(new GridLayoutManager(Context, 2));
            list.SetAdapter(new SongAdapter(recent, position => Playback.Play(Context, recent, position), null, Resource.Layout.card_quick));
            ShowList(GetString(Resource.String.home_recently_played), true);
        }

        // Albums, then songs, the way Home stacks a row of covers above a list.
        void ShowAll(string query, List<YtItem> found, List<YtItem> albums)
        {
            var visible = songs = found.Select(Song.FromStream).ToList();
            if (visible.Count == 0 && albums.Count == 0) { Show(GetString(Resource.String.library_no_results, query)); return; }
            var parts = new List<RecyclerView.Adapter>();
            if (albums.Count > 0)
            {
                parts.Add(new SingleViewAdapter(Resource.Layout.row_section_title, v => ((TextView)v).SetText(Resource.String.library_albums)));
                parts.Add(new SingleViewAdapter(Resource.Layout.row_horizontal_list, v =>
                {
                    var row = (RecyclerView)v;
                    row.SetLayoutManager(new LinearLayoutManager(Context, LinearLayoutManager.Horizontal, false));
                    row.SetAdapter(Cards(albums, RowCardWidthDp));
                }));
            }
            if (visible.Count > 0)
            {
                parts.Add(new SingleViewAdapter(Resource.Layout.row_section_title, v => ((TextView)v).SetText(Resource.String.library_songs)));
                parts.Add(new SongAdapter(visible, position => Playback.Play(Context, visible, position), position => visible[position].Id == Playback.Current?.Id,
                    Resource.Layout.row_song, (anchor, position) => SongActions.ShowMenu(Context, anchor, visible[position])));
            }
            list.SetPadding(0, 0, 0, list.PaddingBottom);
            list.SetLayoutManager(new LinearLayoutManager(Context));
            list.SetAdapter(new ConcatAdapter(parts.ToArray()));
            ShowList(Resources.GetQuantityString(Resource.Plurals.song_count, visible.Count, visible.Count), visible.Count > 0);
        }

        void ShowCards(List<YtItem> items, string emptyText)
        {
            if (items.Count == 0) { Show(emptyText); return; }
            var metrics = Resources.DisplayMetrics;
            int edge = (int)(8 * metrics.Density); // with each card's own 8dp padding this lines the grid up with the 16dp page edge
            list.SetPadding(edge, 0, edge, list.PaddingBottom);
            list.SetLayoutManager(new GridLayoutManager(Context, Math.Max(2, (int)(metrics.WidthPixels / metrics.Density) / MinAlbumColumnDp)));
            list.SetAdapter(Cards(items, 0));
            bool playlists = items[0].Kind == YtKind.Playlist;
            ShowList(Resources.GetQuantityString(playlists ? Resource.Plurals.playlist_count : Resource.Plurals.album_count, items.Count, items.Count), false);
        }

        CoverCardAdapter Cards(List<YtItem> items, int widthDp) => new CoverCardAdapter(items.Count, i => items[i].Title, i => items[i].Artist,
            (art, i) => ArtLoader.LoadUrl(art, items[i].ThumbUrl, AlbumArtDp), i => Open(items[i]), widthDp);

        void Open(YtItem item)
        {
            if (item.Kind == YtKind.Album) { StreamHistory.AddAlbum(Context, item); }
            StartActivity(new Intent(Context, typeof(SongListActivity))
                .PutExtra(SongListActivity.ExtraStreamBrowseId, item.BrowseId)
                .PutExtra(SongListActivity.ExtraStreamTitle, item.Title)
                .PutExtra(SongListActivity.ExtraStreamArtist, item.Artist)
                .PutExtra(SongListActivity.ExtraStreamArt, item.ThumbUrl));
        }

        void ShowList(string countText, bool canShuffle)
        {
            message.Visibility = ViewStates.Gone;
            messageAction.Visibility = ViewStates.Gone;
            list.Visibility = ViewStates.Visible;
            count.Text = countText;
            action.Visibility = canShuffle ? ViewStates.Visible : ViewStates.Invisible;
        }

        void Show(string text, string actionText = null, Action onAction = null)
        {
            list.Visibility = ViewStates.Gone;
            count.Text = "";
            action.Visibility = ViewStates.Invisible;
            message.Text = text;
            message.Visibility = ViewStates.Visible;
            messageAction.Visibility = actionText == null ? ViewStates.Gone : ViewStates.Visible;
            messageAction.Text = actionText;
            onMessageAction = onAction;
        }

        void ShuffleAll()
        {
            if (songs.Count == 0) { return; }
            Playback.Shuffle = true;
            Playback.Play(Context, songs, new Random().Next(songs.Count));
        }

        /// <summary>One view from a layout, for section titles and the album row inside a <see cref="ConcatAdapter"/>.</summary>
        class SingleViewAdapter : RecyclerView.Adapter
        {
            readonly int layout;
            readonly Action<View> bind;

            public SingleViewAdapter(int layout, Action<View> bind)
            {
                this.layout = layout;
                this.bind = bind;
            }

            public override int ItemCount => 1;

            // the layout doubles as the view type, so title rows are only recycled as title rows
            public override int GetItemViewType(int position) => layout;

            public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType) =>
                new Holder(LayoutInflater.From(parent.Context).Inflate(layout, parent, false));

            public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position) => bind(holder.ItemView);

            class Holder : RecyclerView.ViewHolder
            {
                public Holder(View view) : base(view) { }
            }
        }
    }
}
