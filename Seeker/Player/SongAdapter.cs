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
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using System;
using System.Collections.Generic;

namespace Seeker.Player
{
    internal static class PlayerFormat
    {
        public static string Duration(long ms)
        {
            var d = TimeSpan.FromMilliseconds(ms);
            return $"{(int)d.TotalMinutes}:{d.Seconds:00}";
        }

        public static int ThemeColor(Context context, int attr)
        {
            var value = new TypedValue();
            context.Theme.ResolveAttribute(attr, value, true);
            return value.Data;
        }
    }

    /// <summary>Song rows for library, playlist and queue, and the Home cards. isCurrent highlights the playing song; onMore shows the row's menu button.</summary>
    internal class SongAdapter : RecyclerView.Adapter
    {
        const int RowArtDp = 48;
        const int CardArtDp = 140;

        readonly List<Song> items;
        readonly Action<int> onClick;
        readonly Func<int, bool> isCurrent;
        readonly int layout;
        readonly Action<View, int> onMore;

        public SongAdapter(List<Song> items, Action<int> onClick, Func<int, bool> isCurrent = null, int layout = Resource.Layout.row_song, Action<View, int> onMore = null)
        {
            this.items = items;
            this.onClick = onClick;
            this.isCurrent = isCurrent;
            this.layout = layout;
            this.onMore = onMore;
        }

        public override int ItemCount => items.Count;

        public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
        {
            var row = LayoutInflater.From(parent.Context).Inflate(layout, parent, false);
            var holder = new Holder(row);
            row.Click += (s, e) => { if (holder.BindingAdapterPosition >= 0) { onClick(holder.BindingAdapterPosition); } };
            if (holder.More != null)
            {
                holder.More.Visibility = onMore == null ? ViewStates.Gone : ViewStates.Visible;
                holder.More.Click += (s, e) => { if (holder.BindingAdapterPosition >= 0) { onMore(holder.More, holder.BindingAdapterPosition); } };
            }
            return holder;
        }

        public override void OnBindViewHolder(RecyclerView.ViewHolder viewHolder, int position)
        {
            var song = items[position];
            var holder = (Holder)viewHolder;
            holder.Title.Text = song.Title;
            holder.Title.SetTextColor(new Android.Graphics.Color(PlayerFormat.ThemeColor(holder.ItemView.Context,
                isCurrent != null && isCurrent(position) ? Resource.Attribute.mainPurple : Resource.Attribute.cellTextColor)));
            // the narrow home cards only have room for the artist
            holder.Subtitle.Text = string.IsNullOrEmpty(song.Album) || layout != Resource.Layout.row_song ? song.Artist : song.Artist + " · " + song.Album;
            holder.Duration.Text = PlayerFormat.Duration(song.DurationMs);
            ArtLoader.Load(holder.Art, song, layout == Resource.Layout.row_song ? RowArtDp : CardArtDp);
        }

        class Holder : RecyclerView.ViewHolder
        {
            public TextView Title, Subtitle, Duration;
            public ImageView Art;
            public View More;
            public Holder(View v) : base(v)
            {
                Title = v.FindViewById<TextView>(Resource.Id.song_title);
                Subtitle = v.FindViewById<TextView>(Resource.Id.song_subtitle);
                Duration = v.FindViewById<TextView>(Resource.Id.song_duration);
                Art = v.FindViewById<ImageView>(Resource.Id.song_art);
                More = v.FindViewById(Resource.Id.song_more);
            }
        }
    }
}
