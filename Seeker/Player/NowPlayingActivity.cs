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
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Graphics;
using AndroidX.RecyclerView.Widget;
using Google.Android.Material.BottomSheet;
using System;
using System.Threading.Tasks;

namespace Seeker.Player
{
    // shortcut: no lyrics button yet (nothing to put behind it), and no artwork shared-element transition.
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", Exported = false, ParentActivity = typeof(MainActivity))]
    public class NowPlayingActivity : ThemeableActivity
    {
        const int ArtDp = 512;
        const int BackdropPixels = 32; // scaled back up, these few pixels are the blur
        const float BackdropBlurDp = 24f;
        const int ScrimTopAlpha = 90;
        const int ScrimMiddleAlpha = 170;

        View scrim;
        GestureDetector closeGesture;
        ImageView backdrop, art, outputIcon;
        TextView title, artist, format, elapsed, total, outputLabel;
        SeekBar seek;
        ImageButton play, shuffle, repeat;
        Handler handler;
        bool dragging;
        bool alive;
        long shownSongId = -1;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_now_playing);

            OverridePendingTransition(Resource.Animation.mb_sheet_in, 0);
            closeGesture = new GestureDetector(this, new FlingDownToClose(this));
            scrim = FindViewById(Resource.Id.np_scrim);
            backdrop = FindViewById<ImageView>(Resource.Id.np_backdrop);
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                float blur = BackdropBlurDp * Resources.DisplayMetrics.Density;
                backdrop.SetRenderEffect(RenderEffect.CreateBlurEffect(blur, blur, Shader.TileMode.Clamp));
            }
            art =FindViewById<ImageView>(Resource.Id.np_art);
            title = FindViewById<TextView>(Resource.Id.np_title);
            artist = FindViewById<TextView>(Resource.Id.np_artist);
            format = FindViewById<TextView>(Resource.Id.np_format);
            elapsed = FindViewById<TextView>(Resource.Id.np_elapsed);
            total = FindViewById<TextView>(Resource.Id.np_total);
            seek = FindViewById<SeekBar>(Resource.Id.np_seek);
            play = FindViewById<ImageButton>(Resource.Id.np_play);
            shuffle = FindViewById<ImageButton>(Resource.Id.np_shuffle);
            repeat = FindViewById<ImageButton>(Resource.Id.np_repeat);
            outputIcon = FindViewById<ImageView>(Resource.Id.np_output_icon);
            outputLabel = FindViewById<TextView>(Resource.Id.np_output_label);
            handler = new Handler(Looper.MainLooper);

            // square artwork that shrinks on short screens instead of pushing the controls off
            var artHolder = FindViewById(Resource.Id.np_art_holder);
            artHolder.Post(() =>
            {
                int side = Math.Min(artHolder.Width, artHolder.Height);
                art.LayoutParameters.Width = side;
                art.LayoutParameters.Height = side;
                art.RequestLayout();
            });

            FindViewById(Resource.Id.np_close).Click += (s, e) => Finish();
            FindViewById(Resource.Id.np_queue).Click += (s, e) => ShowQueue();
            FindViewById(Resource.Id.np_sound).Click += (s, e) => StartActivity(new Intent(this, typeof(SoundActivity)));
            FindViewById(Resource.Id.np_output).Click += (s, e) => AudioOutput.OpenPicker(this);
            FindViewById(Resource.Id.np_add).Click += (s, e) => { if (Playback.Current != null) { SongActions.AddToPlaylist(this, new[] { Playback.Current }); } };
            play.Click += (s, e) => Playback.Send(this, Playback.ActionToggle);
            FindViewById(Resource.Id.np_next).Click += (s, e) => Playback.Send(this, Playback.ActionNext);
            FindViewById(Resource.Id.np_previous).Click += (s, e) => Playback.Send(this, Playback.ActionPrevious);
            shuffle.Click += (s, e) => { Playback.Shuffle = !Playback.Shuffle; Refresh(); };
            repeat.Click += (s, e) =>
            {
                Playback.Repeat = (Playback.RepeatMode)(((int)Playback.Repeat + 1) % 3);
                Refresh();
            };
            seek.ProgressChanged += (s, e) => { if (e.FromUser) { elapsed.Text = PlayerFormat.Duration(e.Progress); } };
            seek.StartTrackingTouch += (s, e) => dragging = true;
            seek.StopTrackingTouch += (s, e) => { dragging = false; Playback.SeekTo(seek.Progress); };
        }

        protected override void OnStart()
        {
            base.OnStart();
            alive = true;
            Playback.Changed += Refresh;
            Refresh();
            Tick();
        }

        protected override void OnStop()
        {
            alive = false;
            Playback.Changed -= Refresh;
            base.OnStop();
        }

        void Refresh()
        {
            var song = Playback.Current;
            if (song == null) { Finish(); return; }

            title.Text = song.Title;
            artist.Text = song.Artist;
            seek.Max = (int)Math.Max(song.DurationMs, 1);
            total.Text = PlayerFormat.Duration(song.DurationMs);
            if (!dragging)
            {
                seek.Progress = Playback.PositionMs;
                elapsed.Text = PlayerFormat.Duration(Playback.PositionMs);
            }
            play.SetImageResource(Playback.IsPlaying ? Resource.Drawable.ic_pause_24 : Resource.Drawable.ic_play_24);
            play.ContentDescription = GetString(Playback.IsPlaying ? Resource.String.player_pause : Resource.String.player_play);

            Tint(shuffle, Playback.Shuffle);
            Tint(repeat, Playback.Repeat != Playback.RepeatMode.Off);
            repeat.SetImageResource(Playback.Repeat == Playback.RepeatMode.One ? Resource.Drawable.ic_repeat_one_24 : Resource.Drawable.ic_repeat_24);

            if (song.Id != shownSongId)
            {
                shownSongId = song.Id;
                SetBackdrop(null);
                ArtLoader.Load(art, song, ArtDp, SetBackdrop);
                LoadFormat(song);
            }
            if (song.StreamId != null) { format.Text = GetString(Resource.String.stream_source) + (song.StreamFormat == null ? "" : " · " + song.StreamFormat); }
            RefreshOutput();
        }

        // the artwork's average colour fades into the page, ending above the title so text contrast never depends on the artwork
        void SetBackdrop(Bitmap bitmap)
        {
            backdrop.SetImageBitmap(bitmap == null ? null : Bitmap.CreateScaledBitmap(bitmap, BackdropPixels, BackdropPixels, true));
            int page = PlayerFormat.ThemeColor(this, Android.Resource.Attribute.ColorBackground);
            scrim.Background = new GradientDrawable(GradientDrawable.Orientation.TopBottom,
                new[] { ColorUtils.SetAlphaComponent(page, ScrimTopAlpha), ColorUtils.SetAlphaComponent(page, ScrimMiddleAlpha), page, page });
        }

        // Screen edges swipe the app away on most phones, so a downward fling anywhere closes this sheet, like iOS.
        public override bool DispatchTouchEvent(MotionEvent e)
        {
            closeGesture.OnTouchEvent(e);
            return base.DispatchTouchEvent(e);
        }

        public override void Finish()
        {
            base.Finish();
            OverridePendingTransition(0, Resource.Animation.mb_sheet_out);
        }

        class FlingDownToClose : GestureDetector.SimpleOnGestureListener
        {
            const float MinFlingVelocity = 1500f;
            readonly Activity activity;
            public FlingDownToClose(Activity activity) { this.activity = activity; }

            public override bool OnFling(MotionEvent e1, MotionEvent e2, float velocityX, float velocityY)
            {
                if (velocityY < MinFlingVelocity || Math.Abs(velocityY) < Math.Abs(velocityX)) { return false; }
                activity.Finish();
                return true;
            }
        }

        void LoadFormat(Song song)
        {
            format.Text = "";
            if (song.StreamId != null) { return; }
            long id = song.Id;
            var uri = song.Uri;
            Task.Run(() =>
            {
                var info = TrackFormat.Read(this, uri);
                RunOnUiThread(() =>
                {
                    if (id != shownSongId || info == null) { return; }
                    format.Text = BitPerfect.Active ? info.Label + " · " + GetString(Resource.String.bitperfect_title) : info.Label;
                });
            });
        }

        void RefreshOutput()
        {
            var output = AudioOutput.Current(this);
            outputLabel.Text = output.Label;
            outputIcon.SetImageResource(output.Icon);
            int color = PlayerFormat.ThemeColor(this, output.External ? Resource.Attribute.mainPurple : Resource.Attribute.cellTextColorSubdued);
            outputLabel.SetTextColor(new Color(color));
            outputIcon.SetColorFilter(new Color(color));
        }

        void Tint(ImageButton button, bool active)
        {
            button.SetColorFilter(new Color(PlayerFormat.ThemeColor(this, active ? Resource.Attribute.mainPurple : Resource.Attribute.cellTextColorSubdued)));
            button.Selected = active; // for accessibility services, the colour alone must not carry the state
        }

        void Tick()
        {
            if (!alive) { return; }
            if (Playback.IsPlaying && !dragging)
            {
                seek.Progress = Playback.PositionMs;
                elapsed.Text = PlayerFormat.Duration(Playback.PositionMs);
            }
            RefreshOutput();
            handler.PostDelayed(Tick, 500);
        }

        void ShowQueue()
        {
            var sheet = new BottomSheetDialog(this, Resource.Style.SeekerBottomSheetDialog);
            var view = LayoutInflater.Inflate(Resource.Layout.sheet_queue, null);
            var list = view.FindViewById<RecyclerView>(Resource.Id.queue_list);
            list.SetLayoutManager(new LinearLayoutManager(this));
            list.SetAdapter(new SongAdapter(Playback.Queue, position =>
            {
                Playback.Index = position;
                Playback.Send(this, Playback.ActionPlay);
                sheet.Dismiss();
            }, position => position == Playback.Index));
            list.ScrollToPosition(Math.Max(Playback.Index, 0));
            sheet.SetContentView(view);
            sheet.Show();
        }
    }
}
