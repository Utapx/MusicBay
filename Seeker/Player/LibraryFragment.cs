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

using Android;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using AndroidX.RecyclerView.Widget;
using Google.Android.Material.BottomSheet;
using Google.Android.Material.Dialog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Seeker.Player
{
    /// <summary>The library: songs A-Z, albums, artists and playlists, all searchable.</summary>
    public class LibraryFragment : Fragment
    {
        const int PermissionRequest = 9101;
        const int ImagesPermissionRequest = 9102;
        const string PrefsName = "musicbay_player";
        const string AskedImagesKey = "asked_images_permission";
        const int AlbumArtDp = 200;
        const int MinAlbumColumnDp = 160;
        const int ArtistArtDp = 48;

        enum Section { Songs, Albums, Artists, Playlists }

        /// <summary>Set before switching to this tab to open it on Playlists.</summary>
        public static bool OpenOnPlaylists;

        RecyclerView list;
        TextView message, count;
        Button messageAction;
        ImageButton action, clearSearch;
        EditText search;
        RadioGroup sections;
        Action onMessageAction;
        Section section = Section.Songs;
        List<Song> allSongs = new List<Song>();
        List<MediaLibrary.Album> allAlbums = new List<MediaLibrary.Album>();
        List<MediaLibrary.ArtistEntry> allArtists = new List<MediaLibrary.ArtistEntry>();
        List<Song> songs = new List<Song>(); // what the current search leaves; tapping and shuffling act on exactly this
        long highlightedSongId = -1;

        internal static string AudioPermission =>
            OperatingSystem.IsAndroidVersionAtLeast(33) ? Manifest.Permission.ReadMediaAudio : Manifest.Permission.ReadExternalStorage;

        string Query => MediaLibrary.SearchKey(search.Text.Trim());

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            var view = inflater.Inflate(Resource.Layout.fragment_library, container, false);
            list = view.FindViewById<RecyclerView>(Resource.Id.library_list);
            message = view.FindViewById<TextView>(Resource.Id.library_message);
            count = view.FindViewById<TextView>(Resource.Id.library_count);
            messageAction = view.FindViewById<Button>(Resource.Id.library_grant);
            messageAction.Click += (s, e) => onMessageAction?.Invoke();
            action = view.FindViewById<ImageButton>(Resource.Id.library_action);
            action.Click += (s, e) => { if (section == Section.Playlists) { NewPlaylist(); } else { ShuffleAll(); } };
            view.FindViewById(Resource.Id.library_folders).Click += (s, e) => ShowFolders();

            search = view.FindViewById<EditText>(Resource.Id.library_search);
            clearSearch = view.FindViewById<ImageButton>(Resource.Id.library_search_clear);
            clearSearch.Click += (s, e) => search.Text = "";
            search.TextChanged += (s, e) =>
            {
                clearSearch.Visibility = search.Text.Length == 0 ? ViewStates.Gone : ViewStates.Visible;
                Render(false);
            };
            search.EditorAction += (s, e) => HideKeyboard();
            list.AddOnScrollListener(new HideKeyboardOnDrag(this));

            list.AddItemDecoration(new InsetDivider(Context));
            sections = view.FindViewById<RadioGroup>(Resource.Id.library_sections);
            sections.CheckedChange += (s, e) =>
            {
                int id = e.CheckedId;
                section = id ==Resource.Id.library_chip_albums ? Section.Albums
                    : id == Resource.Id.library_chip_artists ? Section.Artists
                    : id == Resource.Id.library_chip_playlists ? Section.Playlists
                    : Section.Songs;
                if (section == Section.Albums) { AskForCoverAccessOnce(); }
                Load(false);
            };
            return view;
        }

        public override void OnResume()
        {
            base.OnResume();
            if (OpenOnPlaylists)
            {
                OpenOnPlaylists = false;
                sections.Check(Resource.Id.library_chip_playlists);
            }
            Playback.Changed += OnPlaybackChanged;
            Load(true);
        }

        public override void OnPause()
        {
            Playback.Changed -= OnPlaybackChanged;
            base.OnPause();
        }

        void OnPlaybackChanged()
        {
            long current = Playback.Current?.Id ?? -1;
            if (current == highlightedSongId) { return; }
            highlightedSongId = current;
            if (section == Section.Songs) { list.GetAdapter()?.NotifyDataSetChanged(); }
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            if (requestCode == PermissionRequest || requestCode == ImagesPermissionRequest) { Load(false); }
        }

        void HideKeyboard()
        {
            var keyboard = (InputMethodManager)Context?.GetSystemService(Context.InputMethodService);
            keyboard?.HideSoftInputFromWindow(search.WindowToken, HideSoftInputFlags.None);
            search.ClearFocus();
        }

        // Photos access is only for cover images, and Android's own prompt does not say that, so explain it first.
        void AskForCoverAccessOnce()
        {
            var context = Context;
            if (ContextCompat.CheckSelfPermission(context, MediaLibrary.ImagesPermission) == Permission.Granted) { return; }
            var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            if (prefs.GetBoolean(AskedImagesKey, false)) { return; }
            prefs.Edit().PutBoolean(AskedImagesKey, true).Apply();
            new MaterialAlertDialogBuilder(context)
                .SetTitle(Resource.String.covers_title)
                .SetMessage(Resource.String.covers_message)
                .SetPositiveButton(Resource.String.okay, (s, e) => RequestPermissions(new[] { MediaLibrary.ImagesPermission }, ImagesPermissionRequest))
                .SetNegativeButton(Resource.String.not_now, (s, e) => { })
                .Show();
        }

        void Load(bool keepScroll)
        {
            var context = Context;
            if (context == null) { return; }
            if (section == Section.Playlists) { Render(keepScroll); return; }
            if (ContextCompat.CheckSelfPermission(context, AudioPermission) != Permission.Granted)
            {
                UpdateAction();
                Show(GetString(Resource.String.library_no_permission), GetString(Resource.String.library_grant),
                    () => RequestPermissions(new[] { AudioPermission }, PermissionRequest));
                return;
            }
            Task.Run(() =>
            {
                MediaLibrary.ReloadCovers(context);
                var result = MediaLibrary.Query(context, "is_music != 0", "title COLLATE NOCASE ASC");
                var albums = MediaLibrary.Albums(context, result);
                var artists = MediaLibrary.Artists(result);
                Activity?.RunOnUiThread(() =>
                {
                    if (!IsAdded) { return; }
                    allSongs = result;
                    allAlbums = albums;
                    allArtists = artists;
                    Render(keepScroll);
                });
            });
        }

        void UpdateAction()
        {
            bool playlists = section == Section.Playlists;
            action.SetImageResource(playlists ? Resource.Drawable.ic_add_24 : Resource.Drawable.ic_shuffle_24);
            action.ContentDescription = GetString(playlists ? Resource.String.playlist_new : Resource.String.player_shuffle);
        }

        void Render(bool keepScroll)
        {
            if (Context == null) { return; }
            UpdateAction();
            string query = Query;
            bool Matches(params string[] fields) => query.Length == 0 || fields.Any(f => MediaLibrary.SearchKey(f).Contains(query));

            songs = allSongs.Where(s => Matches(s.Title, s.Artist, s.Album)).ToList();
            var scroll = keepScroll ? list.GetLayoutManager()?.OnSaveInstanceState() : null;
            string countText;
            int shown;
            switch (section)
            {
                case Section.Albums:
                    var albums = allAlbums.Where(a => Matches(a.Title, a.Artist)).ToList();
                    UseGrid();
                    list.SetAdapter(new CoverCardAdapter(albums.Count, i => albums[i].Title, i => albums[i].Artist,
                        (art, i) => ArtLoader.LoadAlbum(art, albums[i], AlbumArtDp),
                        i => StartActivity(new Intent(Context, typeof(SongListActivity)).PutExtra(SongListActivity.ExtraFolder, albums[i].Folder)), 0));
                    shown = albums.Count;
                    countText = Resources.GetQuantityString(Resource.Plurals.album_count, shown, shown);
                    break;
                case Section.Artists:
                    var artists = allArtists.Where(a => Matches(a.Name)).ToList();
                    UseRows();
                    list.SetAdapter(new CollectionAdapter(artists.Count, i => artists[i].Name,
                        i => Resources.GetQuantityString(Resource.Plurals.song_count, artists[i].Songs.Count, artists[i].Songs.Count),
                        Resource.Drawable.ic_person_24,
                        i => StartActivity(new Intent(Context, typeof(SongListActivity)).PutExtra(SongListActivity.ExtraArtist, artists[i].Name)),
                        i => artists[i].Songs[0]));
                    shown = artists.Count;
                    countText = Resources.GetQuantityString(Resource.Plurals.artist_count, shown, shown);
                    break;
                case Section.Playlists:
                    var playlists = Playlists.All(Context).Where(p => Matches(p.Name)).ToList();
                    UseRows();
                    list.SetAdapter(new CollectionAdapter(playlists.Count, i => playlists[i].Name,
                        i => Resources.GetQuantityString(Resource.Plurals.song_count, playlists[i].SongIds.Count, playlists[i].SongIds.Count),
                        Resource.Drawable.ic_queue_music_24,
                        i => Open(playlists[i])));
                    shown = playlists.Count;
                    countText = Resources.GetQuantityString(Resource.Plurals.playlist_count, shown, shown);
                    break;
                default:
                    UseRows();
                    var visible = songs;
                    list.SetAdapter(new SongAdapter(visible, position => Playback.Play(Context, visible, position), position => visible[position].Id == Playback.Current?.Id,
                        Resource.Layout.row_song, (anchor, position) => SongActions.ShowMenu(Context, anchor, visible[position])));
                    shown = visible.Count;
                    countText = Resources.GetQuantityString(Resource.Plurals.song_count, shown, shown);
                    break;
            }
            if (scroll != null) { list.GetLayoutManager().OnRestoreInstanceState(scroll); }

            if (shown > 0) { ShowList(countText); }
            else if (query.Length > 0) { Show(GetString(Resource.String.library_no_results, search.Text.Trim()), null, null); }
            else if (section == Section.Playlists) { Show(GetString(Resource.String.playlists_empty), GetString(Resource.String.playlist_new), NewPlaylist); }
            else if (MediaLibrary.HiddenFolders(Context).Count > 0) { Show(GetString(Resource.String.library_all_hidden), GetString(Resource.String.library_folders_action), ShowFolders); }
            else { Show(GetString(Resource.String.library_empty), null, null); }
        }

        void UseRows()
        {
            if (list.GetLayoutManager() is GridLayoutManager || list.GetLayoutManager() == null) { list.SetLayoutManager(new LinearLayoutManager(Context)); }
            list.SetPadding(0, 0, 0, list.PaddingBottom);
        }

        void UseGrid()
        {
            if (list.GetLayoutManager() is GridLayoutManager) { return; }
            var metrics = Resources.DisplayMetrics;
            int edge = (int)(8 * metrics.Density); // with each card's own 8dp padding this lines the grid up with the 16dp page edge
            list.SetPadding(edge, 0, edge, list.PaddingBottom);
            list.SetLayoutManager(new GridLayoutManager(Context, Math.Max(2, (int)(metrics.WidthPixels / metrics.Density) / MinAlbumColumnDp)));
        }

        void NewPlaylist() => SongActions.PromptName(Context, Resource.String.playlist_new, "", name => Open(Playlists.Create(Context, name)));

        void Open(Playlist playlist) => StartActivity(new Intent(Context, typeof(PlaylistActivity)).PutExtra(PlaylistActivity.ExtraId, playlist.Id));

        void ShowList(string countText)
        {
            message.Visibility = ViewStates.Gone;
            messageAction.Visibility = ViewStates.Gone;
            list.Visibility = ViewStates.Visible;
            count.Text = countText;
            action.Visibility = ViewStates.Visible;
        }

        void Show(string text, string actionText, Action onAction)
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

        void ShowFolders()
        {
            var context = Context;
            if (ContextCompat.CheckSelfPermission(context, AudioPermission) != Permission.Granted)
            {
                RequestPermissions(new[] { AudioPermission }, PermissionRequest);
                return;
            }
            var sheet = new BottomSheetDialog(context, Resource.Style.SeekerBottomSheetDialog);
            var view = LayoutInflater.Inflate(Resource.Layout.sheet_queue, null);
            view.FindViewById<TextView>(Resource.Id.queue_heading).Text = GetString(Resource.String.library_folders_title);
            var folderList = view.FindViewById<RecyclerView>(Resource.Id.queue_list);
            folderList.SetLayoutManager(new LinearLayoutManager(context));
            folderList.SetAdapter(new FolderAdapter(context, MediaLibrary.Folders(context)));
            sheet.SetContentView(view);
            sheet.DismissEvent += (s, e) => Load(true);
            sheet.Show();
        }

        class HideKeyboardOnDrag : RecyclerView.OnScrollListener
        {
            readonly LibraryFragment fragment;
            public HideKeyboardOnDrag(LibraryFragment fragment) { this.fragment = fragment; }

            public override void OnScrollStateChanged(RecyclerView recyclerView, int newState)
            {
                if (newState == RecyclerView.ScrollStateDragging) { fragment.HideKeyboard(); }
            }
        }

        class FolderAdapter : RecyclerView.Adapter
        {
            readonly Context context;
            readonly List<MediaLibrary.Folder> folders;

            public FolderAdapter(Context context, List<MediaLibrary.Folder> folders)
            {
                this.context = context;
                this.folders = folders;
            }

            public override int ItemCount => folders.Count;

            public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
            {
                var row = LayoutInflater.From(parent.Context).Inflate(Resource.Layout.row_folder, parent, false);
                var holder = new FolderHolder(row);
                row.Click += (s, e) =>
                {
                    if (holder.BindingAdapterPosition < 0) { return; }
                    holder.Check.Checked = !holder.Check.Checked;
                    MediaLibrary.SetFolderHidden(context, folders[holder.BindingAdapterPosition].Path, !holder.Check.Checked);
                };
                return holder;
            }

            public override void OnBindViewHolder(RecyclerView.ViewHolder viewHolder, int position)
            {
                var holder = (FolderHolder)viewHolder;
                var folder = folders[position];
                holder.Name.Text = System.IO.Path.GetFileName(folder.Path);
                holder.Path.Text = context.Resources.GetQuantityString(Resource.Plurals.song_count, folder.SongCount, folder.SongCount) + " · " + folder.Path;
                holder.Check.Checked = !MediaLibrary.HiddenFolders(context).Contains(folder.Path);
            }

            class FolderHolder : RecyclerView.ViewHolder
            {
                public TextView Name, Path;
                public CheckBox Check;
                public FolderHolder(View v) : base(v)
                {
                    Name = v.FindViewById<TextView>(Resource.Id.folder_name);
                    Path = v.FindViewById<TextView>(Resource.Id.folder_path);
                    Check = v.FindViewById<CheckBox>(Resource.Id.folder_check);
                }
            }
        }

        /// <summary>Artist and playlist rows: an icon, a name and a song count.</summary>
        internal class CollectionAdapter : RecyclerView.Adapter
        {
            readonly int itemCount;
            readonly Func<int, string> name, detail;
            readonly int icon;
            readonly Action<int> onClick;
            readonly Func<int, Song> artOf;

            public CollectionAdapter(int itemCount, Func<int, string> name, Func<int, string> detail, int icon, Action<int> onClick, Func<int, Song> artOf = null)
            {
                this.artOf = artOf;
                this.itemCount = itemCount;
                this.name = name;
                this.detail = detail;
                this.icon = icon;
                this.onClick = onClick;
            }

            public override int ItemCount => itemCount;

            public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
            {
                var row = LayoutInflater.From(parent.Context).Inflate(Resource.Layout.row_song, parent, false);
                var holder = new CollectionHolder(row);
                row.Click += (s, e) => { if (holder.BindingAdapterPosition >= 0) { onClick(holder.BindingAdapterPosition); } };
                return holder;
            }

            public override void OnBindViewHolder(RecyclerView.ViewHolder viewHolder, int position)
            {
                var holder = (CollectionHolder)viewHolder;
                var context = holder.ItemView.Context;
                holder.Name.Text = name(position);
                holder.Detail.Text = detail(position);
                if (artOf != null)
                {
                    // iOS draws people round and albums square
                    holder.Art.SetBackgroundResource(Resource.Drawable.bg_art_round);
                    ArtLoader.Load(holder.Art, artOf(position), ArtistArtDp);
                    return;
                }
                holder.Art.SetImageResource(icon);
                holder.Art.SetColorFilter(new Android.Graphics.Color(PlayerFormat.ThemeColor(context, Resource.Attribute.cellTextColorSubdued)));
                int pad = (int)(14 * context.Resources.DisplayMetrics.Density);
                holder.Art.SetPadding(pad, pad, pad, pad);
            }

            class CollectionHolder : RecyclerView.ViewHolder
            {
                public TextView Name, Detail;
                public ImageView Art;
                public CollectionHolder(View v) : base(v)
                {
                    Name = v.FindViewById<TextView>(Resource.Id.song_title);
                    Detail = v.FindViewById<TextView>(Resource.Id.song_subtitle);
                    Art = v.FindViewById<ImageView>(Resource.Id.song_art);
                    v.FindViewById(Resource.Id.song_duration).Visibility = ViewStates.Gone;
                    // the iOS disclosure chevron: it only says "this opens", the whole row takes the tap
                    var chevron = v.FindViewById<ImageButton>(Resource.Id.song_more);
                    chevron.Visibility = ViewStates.Visible;
                    chevron.SetImageResource(Resource.Drawable.chevron_right);
                    chevron.Clickable = false;
                    chevron.Focusable = false;
                    chevron.ImportantForAccessibility = ImportantForAccessibility.No;
                }
            }
        }
    }
}
