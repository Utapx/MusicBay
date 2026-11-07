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
using Android.Views;
using Android.Widget;
using Google.Android.Material.Dialog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Seeker.Player
{
    /// <summary>The row menu and the small dialogs behind playlists.</summary>
    internal static class SongActions
    {
        const int MenuPlayNext = 1;
        const int MenuAddToQueue = 2;
        const int MenuAddToPlaylist = 3;
        const int MenuRemove = 4;

        /// <param name="onRemove">When set, the menu also offers removing the song from the open playlist.</param>
        public static void ShowMenu(Context context, View anchor, Song song, Action onRemove = null)
        {
            var popup = new PopupMenu(context, anchor);
            popup.Menu.Add(0, MenuPlayNext, 0, Resource.String.queue_play_next);
            popup.Menu.Add(0, MenuAddToQueue, 1, Resource.String.queue_add);
            // shortcut: playlists hold MediaStore IDs only, so streamed songs cannot join them yet
            if (song.StreamId == null) { popup.Menu.Add(0, MenuAddToPlaylist, 2, Resource.String.playlist_add_to); }
            if (onRemove != null) { popup.Menu.Add(0, MenuRemove, 3, Resource.String.playlist_remove_song); }
            popup.MenuItemClick += (s, e) =>
            {
                switch (e.Item.ItemId)
                {
                    case MenuPlayNext:
                        Playback.PlayNext(context, song);
                        Toast.MakeText(context, Resource.String.queue_added_next, ToastLength.Short).Show();
                        break;
                    case MenuAddToQueue:
                        Playback.AddToQueue(context, song);
                        Toast.MakeText(context, Resource.String.queue_added, ToastLength.Short).Show();
                        break;
                    case MenuAddToPlaylist:
                        AddToPlaylist(context, new[] { song });
                        break;
                    default:
                        onRemove();
                        break;
                }
            };
            popup.Show();
        }

        public static void AddToPlaylist(Context context, IReadOnlyCollection<Song> songs)
        {
            var playlists = Playlists.All(context);
            var names = new[] { context.GetString(Resource.String.playlist_new) }.Concat(playlists.Select(p => p.Name)).ToArray();
            new MaterialAlertDialogBuilder(context)
                .SetTitle(Resource.String.playlist_add_to)
                .SetItems(names, (s, e) =>
                {
                    if (e.Which == 0)
                    {
                        PromptName(context, Resource.String.playlist_new, "", name => Add(context, Playlists.Create(context, name), songs));
                    }
                    else
                    {
                        Add(context, playlists[e.Which - 1], songs);
                    }
                })
                .Show();
        }

        static void Add(Context context, Playlist playlist, IReadOnlyCollection<Song> songs)
        {
            int added = Playlists.Add(context, playlist, songs.Select(s => s.Id));
            Toast.MakeText(context, context.GetString(added > 0 ? Resource.String.playlist_added : Resource.String.playlist_already, playlist.Name), ToastLength.Short).Show();
        }

        public static void PromptName(Context context, int title, string initial, Action<string> onName)
        {
            var input = new EditText(context) { Text = initial, Hint = context.GetString(Resource.String.playlist_name_hint) };
            input.SetSingleLine(true);
            input.SetSelection(initial.Length);
            int margin = (int)(24 * context.Resources.DisplayMetrics.Density);
            var holder = new FrameLayout(context);
            holder.SetPadding(margin, margin / 2, margin, 0);
            holder.AddView(input);
            var dialog = new MaterialAlertDialogBuilder(context)
                .SetTitle(title)
                .SetView(holder)
                .SetPositiveButton(Resource.String.playlist_save, (s, e) => onName(input.Text.Trim()))
                .SetNegativeButton(Android.Resource.String.Cancel, (s, e) => { })
                .Show();
            var save = dialog.GetButton((int)DialogButtonType.Positive);
            save.Enabled = !string.IsNullOrWhiteSpace(initial);
            input.TextChanged += (s, e) => save.Enabled = !string.IsNullOrWhiteSpace(input.Text);
        }
    }
}
