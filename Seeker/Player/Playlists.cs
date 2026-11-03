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
using Org.Json;
using Seeker.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Seeker.Player
{
    public class Playlist
    {
        public long Id;
        public string Name;
        public List<long> SongIds = new List<long>();
    }

    /// <summary>User playlists, kept as one JSON file in app storage. UI thread only.</summary>
    internal static class Playlists
    {
        static List<Playlist> all;

        public static List<Playlist> All(Context context)
        {
            if (all == null) { all = Read(FilePath(context)); }
            return all;
        }

        public static Playlist Find(Context context, long id) => All(context).FirstOrDefault(p => p.Id == id);

        public static Playlist Create(Context context, string name)
        {
            var playlist = new Playlist { Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Name = name };
            All(context).Add(playlist);
            Save(context);
            return playlist;
        }

        public static void Rename(Context context, Playlist playlist, string name)
        {
            playlist.Name = name;
            Save(context);
        }

        public static void Delete(Context context, Playlist playlist)
        {
            All(context).Remove(playlist);
            Save(context);
        }

        /// <returns>How many songs were new to the playlist.</returns>
        public static int Add(Context context, Playlist playlist, IEnumerable<long> songIds)
        {
            int added = 0;
            foreach (long id in songIds)
            {
                if (playlist.SongIds.Contains(id)) { continue; }
                playlist.SongIds.Add(id);
                added++;
            }
            if (added > 0) { Save(context); }
            return added;
        }

        public static void Remove(Context context, Playlist playlist, long songId)
        {
            playlist.SongIds.Remove(songId);
            Save(context);
        }

        // by id, not position: ids of songs on an unmounted SD card are kept but not shown, so positions differ.
        public static void Move(Context context, Playlist playlist, long songId, long toPositionOfSongId)
        {
            int target = playlist.SongIds.IndexOf(toPositionOfSongId);
            if (target < 0 || !playlist.SongIds.Remove(songId)) { return; }
            playlist.SongIds.Insert(target, songId);
            Save(context);
        }

        /// <summary>The playlist's songs that are on the device now, in playlist order.</summary>
        public static List<Song> Songs(Context context, Playlist playlist)
        {
            if (playlist.SongIds.Count == 0) { return new List<Song>(); }
            // SongIds are longs, so interpolating them into the selection is safe
            var found = MediaLibrary.Query(context, $"_id IN ({string.Join(",", playlist.SongIds)})", null, allFolders: true);
            return found.OrderBy(s => playlist.SongIds.IndexOf(s.Id)).ToList();
        }

        static string FilePath(Context context) => Path.Combine(context.FilesDir.AbsolutePath, "playlists.json");

        static List<Playlist> Read(string path)
        {
            var result = new List<Playlist>();
            if (!File.Exists(path)) { return result; }
            try
            {
                var array = new JSONArray(File.ReadAllText(path));
                for (int i = 0; i < array.Length(); i++)
                {
                    var item = array.GetJSONObject(i);
                    var playlist = new Playlist { Id = item.GetLong("id"), Name = item.GetString("name") };
                    var ids = item.GetJSONArray("songs");
                    for (int j = 0; j < ids.Length(); j++) { playlist.SongIds.Add(ids.GetLong(j)); }
                    result.Add(playlist);
                }
            }
            catch (Exception ex)
            {
                // keep the unreadable file: the next save would otherwise overwrite the user's playlists for good
                Logger.Debug("playlists unreadable: " + ex.Message);
                File.Move(path, path + ".bad", true);
                result.Clear();
            }
            return result;
        }

        static void Save(Context context)
        {
            var array = new JSONArray();
            foreach (var playlist in All(context))
            {
                var ids = new JSONArray();
                foreach (long id in playlist.SongIds) { ids.Put(id); }
                array.Put(new JSONObject().Put("id", playlist.Id).Put("name", playlist.Name).Put("songs", ids));
            }
            // write then rename, so a crash mid-write cannot lose the existing playlists
            string path = FilePath(context);
            File.WriteAllText(path + ".tmp", array.ToString());
            File.Move(path + ".tmp", path, true);
        }
    }
}
