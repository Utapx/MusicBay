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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Common
{
    public enum YtKind { Song, Album, Artist, Playlist }

    public class YtItem
    {
        public YtKind Kind;
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        public string? VideoId;
        public string? BrowseId;
        public long DurationMs;
        public string? ThumbUrl;
    }

    /// <summary>Search and album/artist pages from YouTube Music, through the same web API music.youtube.com calls.</summary>
    public static class YtMusic
    {
        const string Api = "https://music.youtube.com/youtubei/v1/";
        // shortcut: fixed web client version; if YouTube starts rejecting requests, update it to what music.youtube.com sends
        const string ClientVersion = "1.20251020.01.00";

        static readonly Dictionary<YtKind, string> SearchFilters = new Dictionary<YtKind, string>
        {
            { YtKind.Song, "EgWKAQIIAWoMEA4QChADEAQQCRAF" },
            { YtKind.Album, "EgWKAQIYAWoMEA4QChADEAQQCRAF" },
            { YtKind.Artist, "EgWKAQIgAWoMEA4QChADEAQQCRAF" },
        };

        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        const string Origin = "https://music.youtube.com";
        // shortcut: stops after this many pages of 100 songs; raise it if people have longer playlists
        const int MaxPages = 20;
        public const string LikedMusicId = "VLLM";

        /// <summary>Cookies of a signed-in music.youtube.com session, or null to stay anonymous.</summary>
        public static string? Cookie;

        public static async Task<List<YtItem>> SearchAsync(string query, YtKind kind, CancellationToken token = default)
        {
            string json = await PostAsync("search", new JsonObject { ["query"] = query, ["params"] = SearchFilters[kind] }, token);
            return ParseSearch(json, kind);
        }

        /// <summary>An album's tracks, or an artist's songs (all of them when the page links to the full list).</summary>
        public static async Task<List<YtItem>> SongsAsync(string browseId, CancellationToken token = default)
        {
            string json = await PostAsync("browse", new JsonObject { ["browseId"] = browseId }, token);
            var songs = ParseSongs(json, out string? allSongsId);
            if (allSongsId != null)
            {
                json = await PostAsync("browse", new JsonObject { ["browseId"] = allSongsId }, token);
                songs = ParseSongs(json, out _);
            }
            // long playlists, such as Liked Music, come 100 songs at a time
            for (int page = 1; page < MaxPages && NextPage(json) is string next; page++)
            {
                json = await PostAsync("browse", new JsonObject { ["continuation"] = next }, token);
                songs.AddRange(ParseSongs(json, out _));
            }
            return songs;
        }

        /// <summary>The signed-in account's playlists, with Liked Music first.</summary>
        public static async Task<List<YtItem>> LibraryPlaylistsAsync(CancellationToken token = default)
        {
            var playlists = ParseCards(await PostAsync("browse", new JsonObject { ["browseId"] = "FEmusic_liked_playlists" }, token));
            if (!playlists.Any(p => p.BrowseId == LikedMusicId))
            {
                playlists.Insert(0, new YtItem { Kind = YtKind.Playlist, Title = "Liked Music", BrowseId = LikedMusicId });
            }
            return playlists;
        }

        // JsonObject rather than serializing objects: the trimmed release build has no reflection-based serialization
        static async Task<string> PostAsync(string endpoint, JsonObject body, CancellationToken token)
        {
            body["context"] = new JsonObject { ["client"] = new JsonObject { ["clientName"] = "WEB_REMIX", ["clientVersion"] = ClientVersion, ["hl"] = "en", ["gl"] = Region() } };
            using var request = new HttpRequestMessage(HttpMethod.Post, Api + endpoint + "?prettyPrint=false")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Origin", Origin);
            string? cookie = Cookie;
            if (cookie != null && AuthHeader(cookie, DateTimeOffset.UtcNow.ToUnixTimeSeconds()) is string auth)
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
                request.Headers.TryAddWithoutValidation("Authorization", auth);
                request.Headers.Add("X-Origin", Origin);
                request.Headers.Add("X-Goog-AuthUser", "0");
            }
            // Android's default agent gets a mobile layout that leaves out track durations
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
            using var response = await http.SendAsync(request, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>True when the cookies hold a signed-in session.</summary>
        public static bool IsSession(string? cookie) => cookie != null && SapiSid(cookie) != null;

        /// <summary>The header Google's web clients sign requests with: a SHA-1 of the time, the session ID and the origin.</summary>
        public static string? AuthHeader(string cookie, long unixSeconds)
        {
            string? sapiSid = SapiSid(cookie);
            if (sapiSid == null) { return null; }
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(unixSeconds + " " + sapiSid + " " + Origin));
            return "SAPISIDHASH " + unixSeconds + "_" + string.Concat(hash.Select(b => b.ToString("x2")));
        }

        static string? SapiSid(string cookie)
        {
            var values = cookie.Split(';').Select(c => c.Trim().Split(new[] { '=' }, 2)).Where(c => c.Length == 2).ToDictionary(c => c[0], c => c[1]);
            return values.TryGetValue("SAPISID", out var id) || values.TryGetValue("__Secure-3PAPISID", out id) ? id : null;
        }

        static string Region()
        {
            try { return RegionInfo.CurrentRegion.TwoLetterISORegionName; }
            catch (ArgumentException) { return "US"; }
        }

        public static List<YtItem> ParseSearch(string json, YtKind kind)
        {
            using var doc = JsonDocument.Parse(json);
            return Items(doc.RootElement).Select(e => ParseItem(e, kind)).Where(Playable).ToList();
        }

        /// <summary>Tracks of an album, artist or playlist page. <paramref name="allSongsId"/> is the artist's full song list, if linked.</summary>
        public static List<YtItem> ParseSongs(string json, out string? allSongsId)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            allSongsId = Find(root, "musicShelfRenderer").Select(s => Str(s, "bottomEndpoint", "browseEndpoint", "browseId")).FirstOrDefault(id => id != null);

            // album tracks carry no artwork or album name of their own; on other pages the header is a playlist, not an album
            var header = Find(root, "musicResponsiveHeaderRenderer").FirstOrDefault();
            string album = header.ValueKind == JsonValueKind.Object ? Runs(header, "title") : "";
            string? thumb = header.ValueKind == JsonValueKind.Object ? Thumb(header) : null;

            var songs = Items(root).Select(e => ParseItem(e, YtKind.Song)).Where(Playable).ToList();
            foreach (var song in songs.Where(s => s.ThumbUrl == null))
            {
                if (song.Album == "") { song.Album = album; }
                song.ThumbUrl = thumb;
            }
            return songs;
        }

        /// <summary>The token for the next page of a long playlist, or null on the last page.</summary>
        public static string? NextPage(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return Find(doc.RootElement, "continuationItemRenderer").Select(c => Str(c, "continuationEndpoint", "continuationCommand", "token")).FirstOrDefault(t => t != null);
        }

        /// <summary>Square cards, such as the account's playlists.</summary>
        public static List<YtItem> ParseCards(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return Find(doc.RootElement, "musicTwoRowItemRenderer").Select(e => new YtItem
            {
                Kind = YtKind.Playlist,
                Title = Runs(e, "title"),
                Artist = Runs(e, "subtitle"),
                BrowseId = Str(e, "navigationEndpoint", "browseEndpoint", "browseId"),
                ThumbUrl = Get(e, "thumbnailRenderer") is JsonElement art ? Thumb(art) : null,
            }).Where(Playable).ToList();
        }

        static bool Playable(YtItem item) => item.Kind == YtKind.Song ? item.VideoId != null : item.BrowseId != null;

        static YtItem ParseItem(JsonElement item, YtKind kind)
        {
            var columns = item.TryGetProperty("flexColumns", out var flex)
                ? flex.EnumerateArray().Select(c => Get(c, "musicResponsiveListItemFlexColumnRenderer", "text")).ToList()
                : new List<JsonElement?>();
            string Column(int i) => i < columns.Count && columns[i] is JsonElement c ? Runs(c) : "";
            var parts = Column(1).Split(new[] { " • " }, StringSplitOptions.RemoveEmptyEntries);

            var result = new YtItem
            {
                Kind = kind,
                Title = Column(0),
                VideoId = Str(item, "playlistItemData", "videoId"),
                BrowseId = Str(item, "navigationEndpoint", "browseEndpoint", "browseId"),
                ThumbUrl = Thumb(item),
            };
            switch (kind)
            {
                case YtKind.Song:
                    result.Artist = parts.FirstOrDefault() ?? "";
                    result.Album = AlbumRun(columns) ?? "";
                    string? duration = item.TryGetProperty("fixedColumns", out var fixedColumns)
                        ? fixedColumns.EnumerateArray().Select(c => Get(c, "musicResponsiveListItemFixedColumnRenderer", "text")).Select(t => t is JsonElement e ? Runs(e) : "").FirstOrDefault()
                        : parts.LastOrDefault();
                    result.DurationMs = ParseDuration(duration);
                    break;
                case YtKind.Album:
                    // "Album • Daft Punk • 2013": the first part is the release type
                    result.Artist = parts.Length > 1 ? parts[1] : "";
                    break;
            }
            return result;
        }

        static string? AlbumRun(List<JsonElement?> columns)
        {
            foreach (var column in columns.Skip(1))
            {
                if (!(column is JsonElement c) || !c.TryGetProperty("runs", out var runs)) { continue; }
                foreach (var run in runs.EnumerateArray())
                {
                    if (Str(run, "navigationEndpoint", "browseEndpoint", "browseEndpointContextSupportedConfigs", "browseEndpointContextMusicConfig", "pageType") == "MUSIC_PAGE_TYPE_ALBUM")
                    {
                        return Str(run, "text");
                    }
                }
            }
            return null;
        }

        public static long ParseDuration(string? text)
        {
            if (text == null || !Regex.IsMatch(text, @"^\d+(:\d{2}){1,2}$")) { return 0; }
            return text.Split(':').Aggregate(0L, (total, part) => total * 60 + long.Parse(part, CultureInfo.InvariantCulture)) * 1000;
        }

        // The API returns small thumbnails; googleusercontent URLs take the size in their suffix.
        static string? Thumb(JsonElement e)
        {
            if (!((Get(e, "musicThumbnailRenderer", "thumbnail", "thumbnails") ?? Get(e, "thumbnail", "musicThumbnailRenderer", "thumbnail", "thumbnails")) is JsonElement list) || list.GetArrayLength() == 0) { return null; }
            string? url = Str(list[list.GetArrayLength() - 1], "url");
            return url == null ? null : Regex.Replace(url, @"=w\d+-h\d+", "=w544-h544");
        }

        static IEnumerable<JsonElement> Items(JsonElement root) => Find(root, "musicResponsiveListItemRenderer");

        static IEnumerable<JsonElement> Find(JsonElement e, string name)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == name) { yield return p.Value; }
                    else { foreach (var found in Find(p.Value, name)) { yield return found; } }
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in e.EnumerateArray()) { foreach (var found in Find(v, name)) { yield return found; } }
            }
        }

        static string Runs(JsonElement e, string? child = null)
        {
            if (child != null && !e.TryGetProperty(child, out e)) { return ""; }
            return e.TryGetProperty("runs", out var runs) ? string.Concat(runs.EnumerateArray().Select(r => Str(r, "text"))) : "";
        }

        static JsonElement? Get(JsonElement e, params string[] path)
        {
            foreach (var key in path)
            {
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out e)) { return null; }
            }
            return e;
        }

        static string? Str(JsonElement e, params string[] path) => Get(e, path) is JsonElement v && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
