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
using Android.Media;
using Android.Media.Audiofx;
using Seeker.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Seeker.Player
{
    /// <summary>What a file actually contains, read from its header.</summary>
    internal class TrackFormat
    {
        public string Codec;
        public int SampleRate;
        public int Channels;
        public int Bits; // 0 when the container does not say (lossy codecs)

        public string Label
        {
            get
            {
                var parts = new List<string> { Codec };
                if (SampleRate > 0) { parts.Add((SampleRate / 1000f).ToString("0.#") + " kHz"); }
                if (Bits > 0) { parts.Add(Bits + "-bit"); }
                return string.Join(" · ", parts);
            }
        }

        public static TrackFormat Read(Context context, Android.Net.Uri uri)
        {
            using var extractor = new MediaExtractor();
            try
            {
                extractor.SetDataSource(context, uri, null);
                for (int i = 0; i < extractor.TrackCount; i++)
                {
                    var format = extractor.GetTrackFormat(i);
                    string mime = format.GetString(MediaFormat.KeyMime);
                    if (mime == null || !mime.StartsWith("audio/")) { continue; }
                    return new TrackFormat
                    {
                        Codec = CodecName(mime),
                        SampleRate = format.ContainsKey(MediaFormat.KeySampleRate) ? format.GetInteger(MediaFormat.KeySampleRate) : 0,
                        Channels = format.ContainsKey(MediaFormat.KeyChannelCount) ? format.GetInteger(MediaFormat.KeyChannelCount) : 0,
                        Bits = BitsOf(format),
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("could not read format: " + ex.Message);
            }
            return null;
        }

        static int BitsOf(MediaFormat format)
        {
            if (format.ContainsKey("bits-per-sample")) { return format.GetInteger("bits-per-sample"); }
            if (!format.ContainsKey(MediaFormat.KeyPcmEncoding)) { return 0; }
            switch ((Encoding)format.GetInteger(MediaFormat.KeyPcmEncoding))
            {
                case Encoding.Pcm8bit: return 8;
                case Encoding.Pcm16bit: return 16;
                case Encoding.Pcm24bitPacked: return 24;
                case Encoding.Pcm32bit:
                case Encoding.PcmFloat: return 32;
                default: return 0;
            }
        }

        static string CodecName(string mime)
        {
            switch (mime)
            {
                case "audio/flac": return "FLAC";
                case "audio/mpeg": return "MP3";
                case "audio/mp4a-latm": return "AAC";
                case "audio/alac": return "ALAC";
                case "audio/opus": return "Opus";
                case "audio/vorbis": return "Vorbis";
                case "audio/raw": return "WAV";
                default: return mime.Substring("audio/".Length).ToUpperInvariant();
            }
        }
    }

    /// <summary>
    /// Android 14+ can hand audio to a USB DAC with no mixing or resampling. Bluetooth and the speaker have no such path.
    /// </summary>
    internal static class BitPerfect
    {
        const string PrefKey = "bit_perfect";

        /// <summary>True only after the system accepted a bit-perfect mixer for the current song.</summary>
        public static bool Active { get; private set; }
        static string lastStatus;

        public static bool Enabled(Context context) => Prefs(context).GetBoolean(PrefKey, false);

        public static void SetEnabled(Context context, bool enabled)
        {
            Prefs(context).Edit().PutBoolean(PrefKey, enabled).Apply();
            lastStatus = null;
        }

        static ISharedPreferences Prefs(Context context) => context.GetSharedPreferences("musicbay_audio", FileCreationMode.Private);

        public static bool Supported => OperatingSystem.IsAndroidVersionAtLeast(34);

        public static string Status(Context context)
        {
            if (!Supported) { return context.GetString(Resource.String.bitperfect_needs_android14); }
            if (Dac(context) == null) { return context.GetString(Resource.String.bitperfect_no_dac); }
            if (!Enabled(context)) { return context.GetString(Resource.String.bitperfect_description); }
            return lastStatus ?? context.GetString(Resource.String.bitperfect_next_song);
        }

        static AudioDeviceInfo Dac(Context context) =>
            ((AudioManager)context.GetSystemService(Context.AudioService)).GetDevices(GetDevicesTargets.Outputs).FirstOrDefault(d => AudioOutput.IsUsb(d.Type));

        /// <summary>Call before the player opens its output: the mixer is chosen when the track starts.</summary>
        public static void Apply(Context context, AudioAttributes attributes, Song song)
        {
            Active = false;
            if (!Supported) { return; }
            var dac = Dac(context);
            if (dac == null) { return; }
            var audio = (AudioManager)context.GetSystemService(Context.AudioService);
            if (!Enabled(context))
            {
                audio.ClearPreferredMixerAttributes(attributes, dac);
                return;
            }

            var format = song.StreamId == null ? TrackFormat.Read(context, song.Uri) : null; // a stream is always resampled
            if (format == null || format.SampleRate == 0)
            {
                audio.ClearPreferredMixerAttributes(attributes, dac);
                lastStatus = context.GetString(Resource.String.bitperfect_unknown_format);
                return;
            }
            // shortcut: MediaPlayer's decoders hand over 16-bit PCM, so a 24-bit file would be cut down. Refuse rather than claim bit-perfect; hi-res needs a MediaCodec + AudioTrack path tested on a real DAC.
            if (format.Bits > 16)
            {
                audio.ClearPreferredMixerAttributes(attributes, dac);
                lastStatus = context.GetString(Resource.String.bitperfect_hires, format.Label);
                return;
            }
            int channels = format.Channels > 0 ? format.Channels : 2;
            var mixer = audio.GetSupportedMixerAttributes(dac).FirstOrDefault(m =>
                m.MixerBehavior == AudioMixerBehavior.BitPerfect
                && m.Format.SampleRate == format.SampleRate
                && m.Format.ChannelCount == channels
                && m.Format.Encoding == Encoding.Pcm16bit);
            if (mixer == null)
            {
                audio.ClearPreferredMixerAttributes(attributes, dac);
                lastStatus = context.GetString(Resource.String.bitperfect_no_mode, format.Label);
                return;
            }
            Active = audio.SetPreferredMixerAttributes(attributes, dac, mixer);
            lastStatus = context.GetString(Active ? Resource.String.bitperfect_active : Resource.String.bitperfect_refused, format.Label);
        }
    }

    /// <summary>15-band graphic equalizer. DynamicsProcessing sets its own bands, unlike the platform Equalizer whose bands are fixed by the phone (usually 5).</summary>
    internal static class Eq
    {
        public const float MaxGainDb = 15f;
        const string EnabledKey = "eq_enabled";
        const string GainsKey = "eq_gains";
        const string PreampKey = "eq_preamp";

        public static readonly int[] Frequencies = { 25, 40, 63, 100, 160, 250, 400, 630, 1000, 1600, 2500, 4000, 6300, 10000, 16000 };

        public static readonly (int Name, float[] Gains)[] Presets =
        {
            (Resource.String.eq_flat,   new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
            (Resource.String.eq_bass,   new float[] { 7, 7, 6, 5, 3, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
            (Resource.String.eq_warm,   new float[] { 3, 3, 3, 2, 2, 1, 0, 0, 0, -1, -1, -2, -2, -3, -3 }),
            (Resource.String.eq_vocal,  new float[] { -3, -3, -2, -1, 0, 1, 2, 3, 4, 4, 3, 2, 1, 0, 0 }),
            (Resource.String.eq_bright, new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 6 }),
            (Resource.String.eq_vshape, new float[] { 6, 5, 4, 2, 0, -2, -3, -3, -2, 0, 2, 3, 4, 5, 5 }),
            (Resource.String.eq_rock,   new float[] { 5, 5, 4, 3, 1, -1, -2, -2, -1, 1, 2, 3, 4, 4, 4 }),
            (Resource.String.eq_pop,    new float[] { -1, -1, 0, 1, 3, 4, 4, 3, 1, 0, -1, -1, 0, 1, 1 }),
            (Resource.String.eq_hiphop, new float[] { 7, 7, 6, 4, 2, 0, -1, -1, 0, 1, 1, 2, 2, 2, 2 }),
            (Resource.String.eq_electronic, new float[] { 7, 6, 5, 3, 1, 0, -1, -1, 0, 1, 2, 3, 4, 4, 5 }),
            (Resource.String.eq_acoustic, new float[] { 3, 3, 3, 2, 1, 0, 0, 1, 2, 2, 3, 3, 2, 2, 1 }),
            (Resource.String.eq_classical, new float[] { 3, 3, 2, 2, 1, 0, 0, 0, 0, 0, 1, 2, 2, 3, 3 }),
        };

        static float[] gains;
        static DynamicsProcessing effect;

        public static bool Supported => OperatingSystem.IsAndroidVersionAtLeast(28);

        static ISharedPreferences Prefs(Context context) => context.GetSharedPreferences("musicbay_audio", FileCreationMode.Private);

        public static bool Enabled(Context context) => Prefs(context).GetBoolean(EnabledKey, false);

        public static float[] Gains(Context context)
        {
            if (gains != null) { return gains; }
            gains = new float[Frequencies.Length];
            var saved = (Prefs(context).GetString(GainsKey, "") ?? "").Split(',');
            if (saved.Length == gains.Length)
            {
                for (int i = 0; i < gains.Length; i++)
                {
                    float.TryParse(saved[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out gains[i]);
                }
            }
            return gains;
        }

        public static void SetEnabled(Context context, bool enabled)
        {
            Prefs(context).Edit().PutBoolean(EnabledKey, enabled).Apply();
            if (effect != null) { effect.SetEnabled(enabled); }
        }

        public static float Preamp(Context context) => Prefs(context).GetFloat(PreampKey, 0f);

        public static void SetPreamp(Context context, float db)
        {
            Prefs(context).Edit().PutFloat(PreampKey, db).Apply();
            ApplyInputGain(context);
        }

        // Lowers the input by the largest boost, so boosted bands get louder relative to the rest instead of
        // hitting the limiter, which would squash them back down on loud masters.
        static void ApplyInputGain(Context context)
        {
            effect?.SetInputGainAllChannelsTo(Preamp(context) - Math.Max(0f, Gains(context).Max()));
        }

        public static void SetGain(Context context, int band, float db)
        {
            Gains(context)[band] = db;
            Prefs(context).Edit().PutString(GainsKey, string.Join(",", gains.Select(g => g.ToString(System.Globalization.CultureInfo.InvariantCulture)))).Apply();
            effect?.SetPreEqBandAllChannelsTo(band, Band(band, db));
            ApplyInputGain(context);
        }

        // A band's "cutoff" is its upper edge: halfway (on a log scale) to the next band's centre.
        static DynamicsProcessing.EqBand Band(int i, float db)
        {
            float cutoff = i == Frequencies.Length - 1 ? 20000f : (float)Math.Sqrt(Frequencies[i] * (double)Frequencies[i + 1]);
            return new DynamicsProcessing.EqBand(true, cutoff, db);
        }

        /// <summary>Binds the equalizer to a new player. Off while bit-perfect is active, since any processing would break it.</summary>
        public static void Attach(Context context, int audioSessionId)
        {
            Release();
            if (!Supported || BitPerfect.Active) { return; }
            try
            {
                // The default 10 ms frame resolves only ~100 Hz, merging the 25-100 Hz bands; 40 ms resolves ~25 Hz.
                var config = new DynamicsProcessing.Config.Builder((int)VariantType.FavorFrequencyResolution, 2,
                    true, Frequencies.Length, false, 0, false, 0, true).SetPreferredFrameDuration(40f).Build();
                // only catches peaks above -0.5 dBFS (a positive preamp); the default -2 dB threshold flattened boosts
                config.SetLimiterAllChannelsTo(new DynamicsProcessing.Limiter(true, true, 0, 1f, 60f, 10f, -0.5f, 0f));
                effect = new DynamicsProcessing(0, audioSessionId, config);
                var current = Gains(context);
                for (int i = 0; i < current.Length; i++) { effect.SetPreEqBandAllChannelsTo(i, Band(i, current[i])); }
                ApplyInputGain(context);
                effect.SetEnabled(Enabled(context));
            }
            catch (Java.Lang.Exception ex)
            {
                // some phones refuse the effect; music must keep playing without it
                Logger.Debug("equalizer unavailable: " + ex.Message);
                Release();
            }
        }

        public static void Release()
        {
            effect?.Release();
            effect = null;
        }
    }
}
