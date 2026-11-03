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
using Android.OS;
using Android.Views;
using Android.Widget;
using Google.Android.Material.Chip;
using Google.Android.Material.MaterialSwitch;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Seeker.Player
{
    /// <summary>Output, bit-perfect and the equalizer.</summary>
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", Exported = false, ParentActivity = typeof(MainActivity))]
    public class SoundActivity : ThemeableActivity
    {
        const int SeekStepsPerDb = 2;

        readonly List<(SeekBar Seek, TextView Value)> bands = new List<(SeekBar, TextView)>();
        ChipGroup presets;
        EqCurveView curve;
        MaterialSwitch eqSwitch, bitPerfectSwitch;
        TextView bitPerfectStatus, eqNote, outputLabel;
        ImageView outputIcon;
        Handler handler;
        bool alive;
        bool applyingPreset;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            SetContentView(Resource.Layout.activity_sound);
            handler = new Handler(Looper.MainLooper);

            FindViewById(Resource.Id.sound_back).Click += (s, e) => Finish();
            FindViewById(Resource.Id.sound_output).Click += (s, e) => AudioOutput.OpenPicker(this);
            outputLabel = FindViewById<TextView>(Resource.Id.sound_output_label);
            outputIcon = FindViewById<ImageView>(Resource.Id.sound_output_icon);

            bitPerfectStatus = FindViewById<TextView>(Resource.Id.sound_bitperfect_status);
            bitPerfectSwitch = FindViewById<MaterialSwitch>(Resource.Id.sound_bitperfect_switch);
            bitPerfectSwitch.Enabled = BitPerfect.Supported;
            bitPerfectSwitch.Checked = BitPerfect.Enabled(this);
            bitPerfectSwitch.CheckedChange += (s, e) =>
            {
                BitPerfect.SetEnabled(this, e.IsChecked);
                PlayerService.Instance?.ReopenCurrent();
                RefreshState();
            };

            eqNote = FindViewById<TextView>(Resource.Id.sound_eq_note);
            eqSwitch = FindViewById<MaterialSwitch>(Resource.Id.sound_eq_switch);
            eqSwitch.Checked = Eq.Enabled(this);
            eqSwitch.CheckedChange += (s, e) =>
            {
                Eq.SetEnabled(this, e.IsChecked);
                RefreshState();
            };

            curve = new EqCurveView(this);
            FindViewById<FrameLayout>(Resource.Id.sound_curve_holder).AddView(curve);
            BuildPresets();
            BuildBands();
        }

        void BuildPresets()
        {
            presets = FindViewById<ChipGroup>(Resource.Id.sound_presets);
            foreach (var preset in Eq.Presets)
            {
                var chip = new Chip(this) { Text = GetString(preset.Name), Checkable = true };
                var gains = preset.Gains;
                chip.Click += (s, e) => ApplyPreset(gains);
                presets.AddView(chip);
            }
        }

        void ApplyPreset(float[] gains)
        {
            applyingPreset = true;
            for (int i = 0; i < gains.Length; i++)
            {
                Eq.SetGain(this, i, gains[i]);
                bands[i].Seek.Progress = ToProgress(gains[i]);
                bands[i].Value.Text = FormatDb(gains[i]);
            }
            applyingPreset = false;
            RefreshState();
        }

        void BuildBands()
        {
            var holder = FindViewById<LinearLayout>(Resource.Id.sound_bands);
            var gains = Eq.Gains(this);
            for (int i = 0; i < Eq.Frequencies.Length; i++)
            {
                int band = i;
                var row = LayoutInflater.Inflate(Resource.Layout.row_eq_band, holder, false);
                string frequency = FormatFrequency(Eq.Frequencies[i]);
                row.FindViewById<TextView>(Resource.Id.band_frequency).Text = frequency;
                var value = row.FindViewById<TextView>(Resource.Id.band_value);
                var seek = row.FindViewById<SeekBar>(Resource.Id.band_seek);
                seek.ContentDescription = frequency;
                seek.Max = (int)(2 * Eq.MaxGainDb * SeekStepsPerDb);
                seek.Progress = ToProgress(gains[i]);
                value.Text = FormatDb(gains[i]);
                seek.ProgressChanged += (s, e) =>
                {
                    if (applyingPreset || !e.FromUser) { return; }
                    float db = e.Progress / (float)SeekStepsPerDb - Eq.MaxGainDb;
                    Eq.SetGain(this, band, db);
                    value.Text = FormatDb(db);
                    RefreshState();
                };
                bands.Add((seek, value));
                holder.AddView(row);
            }
        }

        static int ToProgress(float db) => (int)Math.Round((db + Eq.MaxGainDb) * SeekStepsPerDb);

        static string FormatDb(float db) => (db > 0 ? "+" : "") + db.ToString("0.#");

        static string FormatFrequency(int hz) => hz >= 1000 ? (hz / 1000f).ToString("0.#") + " kHz" : hz + " Hz";

        protected override void OnStart()
        {
            base.OnStart();
            alive = true;
            Tick();
        }

        protected override void OnStop()
        {
            alive = false;
            base.OnStop();
        }

        void Tick()
        {
            if (!alive) { return; }
            var output = AudioOutput.Current(this);
            outputLabel.Text = output.Label;
            outputIcon.SetImageResource(output.Icon);
            RefreshState();
            handler.PostDelayed(Tick, 1000);
        }

        void RefreshState()
        {
            bitPerfectStatus.Text = BitPerfect.Status(this);

            bool blocked = !Eq.Supported || BitPerfect.Active;
            eqNote.Visibility = blocked ? ViewStates.Visible : ViewStates.Gone;
            eqNote.Text = GetString(Eq.Supported ? Resource.String.eq_off_for_bitperfect : Resource.String.eq_needs_android9);
            eqSwitch.Enabled = !blocked;

            bool usable = !blocked && eqSwitch.Checked;
            foreach (var band in bands) { band.Seek.Enabled = usable; }
            for (int i = 0; i < presets.ChildCount; i++) { presets.GetChildAt(i).Enabled = usable; }

            var gains = Eq.Gains(this);
            for (int i = 0; i < Eq.Presets.Length; i++)
            {
                ((Chip)presets.GetChildAt(i)).Checked = Eq.Presets[i].Gains.SequenceEqual(gains);
            }
            curve.Update(gains, usable);
        }
    }

    /// <summary>Draws the current EQ shape. Informational only: the sliders are the accessible control.</summary>
    internal class EqCurveView : View
    {
        readonly Paint line = new Paint(PaintFlags.AntiAlias);
        readonly Paint fill = new Paint(PaintFlags.AntiAlias);
        readonly Paint zero = new Paint(PaintFlags.AntiAlias);
        readonly Path path = new Path();
        float[] gains = new float[0];

        public EqCurveView(Context context) : base(context)
        {
            float density = context.Resources.DisplayMetrics.Density;
            line.SetStyle(Paint.Style.Stroke);
            line.StrokeWidth = 2 * density;
            line.StrokeCap = Paint.Cap.Round;
            fill.SetStyle(Paint.Style.Fill);
            zero.StrokeWidth = density;
            zero.Color = new Color(PlayerFormat.ThemeColor(context, Resource.Attribute.slightestContrastColor));
            ImportantForAccessibility = ImportantForAccessibility.No;
        }

        public void Update(float[] newGains, bool active)
        {
            gains = newGains;
            int color = PlayerFormat.ThemeColor(Context, active ? Resource.Attribute.mainPurple : Resource.Attribute.cellTextColorSubdued);
            line.Color = new Color(color);
            fill.Color = new Color(Color.GetRedComponent(color), Color.GetGreenComponent(color), Color.GetBlueComponent(color), 40);
            Invalidate();
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (gains.Length < 2) { return; }
            float w = Width, h = Height, mid = h / 2;
            canvas.DrawLine(0, mid, w, mid, zero);

            float X(int i) => w * i / (gains.Length - 1);
            float Y(int i) => mid - gains[i] / Eq.MaxGainDb * (mid - line.StrokeWidth);

            path.Reset();
            path.MoveTo(0, Y(0));
            for (int i = 1; i < gains.Length - 1; i++)
            {
                path.QuadTo(X(i), Y(i), (X(i) + X(i + 1)) / 2, (Y(i) + Y(i + 1)) / 2);
            }
            path.LineTo(w, Y(gains.Length - 1));
            canvas.DrawPath(path, line);
            path.LineTo(w, mid);
            path.LineTo(0, mid);
            path.Close();
            canvas.DrawPath(path, fill);
        }
    }
}
