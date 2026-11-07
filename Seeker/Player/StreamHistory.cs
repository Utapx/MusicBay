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
using Common;
using Seeker.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Seeker.Player
{
    /// <summary>Recently played streamed songs and recently opened albums, newest first. Phone songs keep their own history in MediaLibrary.</summary>
    internal static class StreamHistory
    {
        const int MaxItems = 30;
        const string PrefsName = "musicbay_stream";
        const string SongsKey = "recent";
        const string AlbumsKey = "recent_albums";

        public static List<Song> All(Context context) => Read(context, SongsKey).Select(n => Song.FromStream(new YtItem
        {
            VideoId = (string)n["id"],
            Title = (string)n["title"] ?? "",
            Artist = (string)n["artist"] ?? "",
            Album = (string)n["album"] ?? "",
            DurationMs = (long?)n["ms"] ?? 0,
            ThumbUrl = (string)n["art"],
        })).Where(s => s.StreamId != null).ToList();

        public static void Add(Context context, Song song)
        {
            var songs = All(context).Where(s => s.StreamId != song.StreamId).Prepend(song).Take(MaxItems);
            Write(context, SongsKey, songs.Select(s => new JsonObject { ["id"] = s.StreamId, ["title"] = s.Title, ["artist"] = s.Artist, ["album"] = s.Album, ["ms"] = s.DurationMs, ["art"] = s.ArtUrl }));
        }

        public static List<YtItem> Albums(Context context) => Read(context, AlbumsKey).Select(n => new YtItem
        {
            Kind = YtKind.Album,
            BrowseId = (string)n["id"],
            Title = (string)n["title"] ?? "",
            Artist = (string)n["artist"] ?? "",
            ThumbUrl = (string)n["art"],
        }).Where(a => a.BrowseId != null).ToList();

        public static void AddAlbum(Context context, YtItem album)
        {
            var albums = Albums(context).Where(a => a.BrowseId != album.BrowseId).Prepend(album).Take(MaxItems);
            Write(context, AlbumsKey, albums.Select(a => new JsonObject { ["id"] = a.BrowseId, ["title"] = a.Title, ["artist"] = a.Artist, ["art"] = a.ThumbUrl }));
        }

        // JsonNode rather than serializing objects: the trimmed release build has no reflection-based serialization
        static IEnumerable<JsonNode> Read(Context context, string key)
        {
            try
            {
                var stored = context.GetSharedPreferences(PrefsName, FileCreationMode.Private).GetString(key, null);
                return stored == null ? Enumerable.Empty<JsonNode>() : JsonNode.Parse(stored).AsArray().ToList();
            }
            catch (Exception ex)
            {
                Logger.Debug("stream history unreadable: " + ex.Message);
                return Enumerable.Empty<JsonNode>();
            }
        }

        static void Write(Context context, string key, IEnumerable<JsonObject> items)
        {
            var array = new JsonArray();
            foreach (var item in items) { array.Add(item); }
            context.GetSharedPreferences(PrefsName, FileCreationMode.Private).Edit().PutString(key, array.ToJsonString()).Apply();
        }
    }
}
