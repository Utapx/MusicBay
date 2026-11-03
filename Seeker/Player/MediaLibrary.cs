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
using Android.Provider;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Seeker.Player
{
    /// <summary>MediaStore reads shared by Library and Home, the folder filter, and the short "recently played" list.</summary>
    internal static class MediaLibrary
    {
        const string PrefsName = "musicbay_player";
        const string RecentKey = "recent_ids";
        const string HiddenFoldersKey = "hidden_folders";
        const int RecentMax = 10;

        // file names that mean "this is the album cover", best first
        static readonly string[] CoverNames = { "cover", "folder", "front", "album", "albumart", "artwork", "albumartsmall" };

        static volatile Dictionary<string, Android.Net.Uri> covers = new Dictionary<string, Android.Net.Uri>();

        public class Folder
        {
            public string Path;
            public int SongCount;
        }

        /// <summary>One folder of music. Folders, not tags, are what make an album here.</summary>
        public class Album
        {
            public string Folder;
            public string Title;
            public string Artist;
            public List<Song> Songs;
        }

        public class ArtistEntry
        {
            public string Name;
            public List<Song> Songs;
        }

        public static string ImagesPermission =>
            OperatingSystem.IsAndroidVersionAtLeast(33) ? Android.Manifest.Permission.ReadMediaImages : Android.Manifest.Permission.ReadExternalStorage;

        public static List<Album> Albums(Context context, List<Song> songs)
        {
            string various = context.GetString(Resource.String.various_artists);
            return songs.GroupBy(s => s.Folder).Select(g =>
            {
                var tracks = g.OrderBy(s => s.Track).ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase).ToList();
                var tags = tracks.Select(s => s.Album).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToList();
                var artists = tracks.Select(s => s.Artist).Distinct().ToList();
                return new Album
                {
                    Folder = g.Key,
                    // a folder of loose songs has many album tags, so it goes by its own name
                    Title = tags.Count == 1 ? tags[0] : Path.GetFileName(g.Key),
                    Artist = artists.Count == 1 ? artists[0] : various,
                    Songs = tracks,
                };
            }).OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<ArtistEntry> Artists(List<Song> songs)
        {
            return songs.GroupBy(s => s.Artist)
                .Select(g => new ArtistEntry { Name = g.Key, Songs = g.ToList() })
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The image file that serves as this folder's cover, or null. Call ReloadCovers first.</summary>
        public static Android.Net.Uri CoverIn(string folder) => covers.TryGetValue(folder ?? "", out var uri) ? uri : null;

        /// <summary>Re-reads image files from MediaStore. Without the photos permission there are simply no folder covers.</summary>
        public static void ReloadCovers(Context context)
        {
            var found = new Dictionary<string, Android.Net.Uri>();
            if (AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, ImagesPermission) == Android.Content.PM.Permission.Granted)
            {
                var byFolder = new Dictionary<string, List<(string Name, long Id)>>();
                using var cursor = context.ContentResolver.Query(MediaStore.Images.Media.ExternalContentUri, new[] { "_id", "_data" }, null, null, null);
                while (cursor != null && cursor.MoveToNext())
                {
                    string path = cursor.GetString(1);
                    if (string.IsNullOrEmpty(path)) { continue; }
                    string folder = FolderOf(path);
                    if (!byFolder.TryGetValue(folder, out var images)) { byFolder[folder] = images = new List<(string, long)>(); }
                    images.Add((Path.GetFileNameWithoutExtension(path).ToLowerInvariant(), cursor.GetLong(0)));
                }
                foreach (var entry in byFolder)
                {
                    long id = PickCover(entry.Value);
                    if (id >= 0) { found[entry.Key] = ContentUris.WithAppendedId(MediaStore.Images.Media.ExternalContentUri, id); }
                }
            }
            covers = found;
        }

        // A lone image in a folder is almost surely its cover. Among several, only a cover-like name counts: otherwise a Download folder would show a random screenshot.
        static long PickCover(List<(string Name, long Id)> images)
        {
            foreach (string name in CoverNames)
            {
                foreach (var image in images) { if (image.Name == name) { return image.Id; } }
            }
            return images.Count == 1 ? images[0].Id : -1;
        }

        /// <summary>Lower case without accents, so "beyonce" finds "Beyoncé".</summary>
        public static string SearchKey(string text)
        {
            var builder = new System.Text.StringBuilder();
            foreach (char c in (text ?? "").Normalize(System.Text.NormalizationForm.FormD))
            {
                if (char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) { builder.Append(char.ToLowerInvariant(c)); }
            }
            return builder.ToString();
        }

        /// <param name="allFolders">Playlists pass true: a song the user added on purpose stays even if its folder is hidden.</param>
        public static List<Song> Query(Context context, string selection, string order, int limit = int.MaxValue, bool allFolders = false)
        {
            var result = new List<Song>();
            var projection = new[] { "_id", "title", "artist", "album", "duration", "album_id", "_data", "track" };
            using var cursor = context.ContentResolver.Query(MediaStore.Audio.Media.ExternalContentUri, projection, selection, null, order);
            if (cursor == null) { return result; }
            string unknownArtist = context.GetString(Resource.String.unknown_artist);
            var hidden = allFolders ? new HashSet<string>() : HiddenFolders(context);
            while (result.Count < limit && cursor.MoveToNext())
            {
                string folder = FolderOf(cursor.GetString(6));
                if (hidden.Contains(folder)) { continue; }
                string artist = cursor.GetString(2);
                result.Add(new Song
                {
                    Id = cursor.GetLong(0),
                    Title = cursor.GetString(1),
                    Artist = string.IsNullOrEmpty(artist) || artist == "<unknown>" ? unknownArtist : artist,
                    Album = cursor.GetString(3),
                    DurationMs = cursor.GetLong(4),
                    AlbumId = cursor.GetLong(5),
                    Folder = folder,
                    Track = cursor.GetInt(7),
                });
            }
            return result;
        }

        /// <summary>Every folder that holds music, hidden ones included, so they can be turned back on.</summary>
        public static List<Folder> Folders(Context context)
        {
            return Query(context, "is_music != 0", null, allFolders: true)
                .GroupBy(s => s.Folder)
                .Select(g => new Folder { Path = g.Key, SongCount = g.Count() })
                .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static HashSet<string> HiddenFolders(Context context)
        {
            var saved = context.GetSharedPreferences(PrefsName, FileCreationMode.Private).GetStringSet(HiddenFoldersKey, null);
            return saved == null ? new HashSet<string>() : new HashSet<string>(saved);
        }

        public static void SetFolderHidden(Context context, string folder, bool hide)
        {
            var hidden = HiddenFolders(context);
            if (hide) { hidden.Add(folder); } else { hidden.Remove(folder); }
            context.GetSharedPreferences(PrefsName, FileCreationMode.Private).Edit().PutStringSet(HiddenFoldersKey, hidden).Apply();
        }

        static string FolderOf(string filePath) => Path.GetDirectoryName(filePath ?? "") ?? "";

        public static void AddRecent(Context context, long songId)
        {
            var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            var ids = ReadRecentIds(prefs);
            ids.Remove(songId);
            ids.Insert(0, songId);
            if (ids.Count > RecentMax) { ids.RemoveRange(RecentMax, ids.Count - RecentMax); }
            prefs.Edit().PutString(RecentKey, string.Join(",", ids)).Apply();
        }

        /// <summary>Most recent first. Songs deleted from the device since are simply absent.</summary>
        public static List<Song> Recent(Context context)
        {
            var ids = ReadRecentIds(context.GetSharedPreferences(PrefsName, FileCreationMode.Private));
            if (ids.Count == 0) { return new List<Song>(); }
            // ids come from long.TryParse below, so interpolating them into the selection is safe
            var found = Query(context, $"_id IN ({string.Join(",", ids)})", null);
            return found.OrderBy(s => ids.IndexOf(s.Id)).ToList();
        }

        static List<long> ReadRecentIds(ISharedPreferences prefs)
        {
            var ids = new List<long>();
            foreach (var part in (prefs.GetString(RecentKey, "") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(part, out long id)) { ids.Add(id); }
            }
            return ids;
        }
    }
}
