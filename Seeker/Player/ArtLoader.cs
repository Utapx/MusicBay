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
using Android.Graphics;
using Android.Util;
using Android.Widget;
using System;
using System.Threading.Tasks;

namespace Seeker.Player
{
    /// <summary>Artwork for rows and cards: embedded art or a cover image in the song's folder. Async, cached, safe against recycled views.</summary>
    internal static class ArtLoader
    {
        static readonly LruCache cache = new LruCache(150);

        /// <param name="onLoaded">Called on the UI thread when real artwork is shown; not called for the placeholder.</param>
        public static void Load(ImageView view, Song song, int sizeDp, Action<Bitmap> onLoaded = null)
        {
            Load(view, "song:" + song.Id, sizeDp, (context, size) => ReadForSong(context, song, size), onLoaded);
        }

        /// <summary>An album shows its folder's cover image first: that is the picture chosen for the whole album.</summary>
        public static void LoadAlbum(ImageView view, MediaLibrary.Album album, int sizeDp)
        {
            Load(view, "album:" + album.Folder, sizeDp,
                (context, size) => ReadFolderCover(context, album.Folder, size) ?? ReadEmbedded(context, album.Songs[0], size), null);
        }

        /// <summary>Blocking; call off the UI thread.</summary>
        public static Bitmap ReadForSong(Context context, Song song, int size) =>
            ReadEmbedded(context, song, size) ?? ReadFolderCover(context, song.Folder, size);

        static void Load(ImageView view, string id, int sizeDp, Func<Context, int, Bitmap> read, Action<Bitmap> onLoaded)
        {
            var context = view.Context;
            int size = (int)(sizeDp * context.Resources.DisplayMetrics.Density);
            string key = id + ":" + size;
            view.Tag = key;

            if (cache.Get(key) is Bitmap cached)
            {
                Show(view, cached);
                onLoaded?.Invoke(cached);
                return;
            }

            int pad = size / 3;
            view.SetPadding(pad, pad, pad, pad);
            view.SetScaleType(ImageView.ScaleType.FitCenter);
            view.SetImageResource(Resource.Drawable.ic_music_note_24);
            view.SetColorFilter(new Color(PlayerFormat.ThemeColor(context, Resource.Attribute.cellTextColorSubdued)));

            var appContext = context.ApplicationContext;
            Task.Run(() =>
            {
                var bitmap = read(appContext, size);
                if (bitmap == null) { return; }
                view.Post(() =>
                {
                    cache.Put(key, bitmap);
                    if (view.Tag?.ToString() != key) { return; } // the row now shows another song
                    Show(view, bitmap);
                    onLoaded?.Invoke(bitmap);
                });
            });
        }

        static void Show(ImageView view, Bitmap bitmap)
        {
            view.SetPadding(0, 0, 0, 0);
            view.ClearColorFilter();
            view.SetScaleType(ImageView.ScaleType.CenterCrop);
            view.SetImageBitmap(bitmap);
        }

        static Bitmap ReadEmbedded(Context context, Song song, int size)
        {
            var uri = OperatingSystem.IsAndroidVersionAtLeast(29)
                ? song.Uri
                : ContentUris.WithAppendedId(Android.Net.Uri.Parse("content://media/external/audio/albumart"), song.AlbumId);
            return Decode(context, uri, size);
        }

        static Bitmap ReadFolderCover(Context context, string folder, int size)
        {
            var uri = MediaLibrary.CoverIn(folder);
            return uri == null ? null : Decode(context, uri, size);
        }

        static Bitmap Decode(Context context, Android.Net.Uri uri, int size)
        {
            try
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(29))
                {
                    return context.ContentResolver.LoadThumbnail(uri, new Size(size, size), null);
                }
                // a phone-camera sized cover would not fit in memory at full resolution
                var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
                using (var stream = context.ContentResolver.OpenInputStream(uri)) { BitmapFactory.DecodeStream(stream, null, bounds); }
                var options = new BitmapFactory.Options { InSampleSize = Math.Max(1, Math.Min(bounds.OutWidth, bounds.OutHeight) / size) };
                using (var stream = context.ContentResolver.OpenInputStream(uri)) { return BitmapFactory.DecodeStream(stream, null, options); }
            }
            catch (Exception)
            {
                return null; // no artwork, or the file is unreadable
            }
        }
    }
}
