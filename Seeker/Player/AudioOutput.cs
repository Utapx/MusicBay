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
using System;
using System.Linq;

namespace Seeker.Player
{
    /// <summary>Where audio is going right now: phone speaker, wired, USB or a named Bluetooth device.</summary>
    internal static class AudioOutput
    {
        public class Info
        {
            public string Label;
            public int Icon;
            public bool External;
        }

        // Screens poll this from their ticker: the route changes shortly after a device-added callback fires, so a callback would read stale state.
        public static Info Current(Context context)
        {
            var device = PlayerService.Instance?.RoutedDevice ?? MostLikely(context);
            return Describe(context, device);
        }

        // Not playing yet, so nothing is routed: use the order Android prefers for media.
        static AudioDeviceInfo MostLikely(Context context)
        {
            var devices = ((AudioManager)context.GetSystemService(Context.AudioService)).GetDevices(GetDevicesTargets.Outputs);
            return devices.OrderBy(d => IsBluetooth(d.Type) ? 0 : IsUsb(d.Type) ? 1 : IsWired(d.Type) ? 2 : 3).FirstOrDefault();
        }

        static Info Describe(Context context, AudioDeviceInfo device)
        {
            var type = device?.Type ?? AudioDeviceType.BuiltinSpeaker;
            string name = device?.ProductName;
            if (IsBluetooth(type))
            {
                return new Info
                {
                    Label = string.IsNullOrEmpty(name) ? context.GetString(Resource.String.output_bluetooth) : context.GetString(Resource.String.output_bluetooth_named, name),
                    Icon = Resource.Drawable.ic_bluetooth_24,
                    External = true,
                };
            }
            if (IsUsb(type))
            {
                return new Info
                {
                    Label = string.IsNullOrEmpty(name) ? context.GetString(Resource.String.output_usb) : context.GetString(Resource.String.output_usb_named, name),
                    Icon = Resource.Drawable.ic_usb_24,
                    External = true,
                };
            }
            if (IsWired(type))
            {
                return new Info { Label = context.GetString(Resource.String.output_wired), Icon = Resource.Drawable.ic_headphones_24, External = true };
            }
            if (type == AudioDeviceType.Hdmi || type == AudioDeviceType.HdmiArc || type == AudioDeviceType.HdmiEarc)
            {
                return new Info { Label = context.GetString(Resource.String.output_hdmi), Icon = Resource.Drawable.ic_speaker_24, External = true };
            }
            return new Info { Label = context.GetString(Resource.String.output_phone), Icon = Resource.Drawable.ic_speaker_24, External = false };
        }

        static bool IsBluetooth(AudioDeviceType t) =>
            t == AudioDeviceType.BluetoothA2dp || t == AudioDeviceType.BluetoothSco || t == AudioDeviceType.BleHeadset
            || t == AudioDeviceType.BleSpeaker || t == AudioDeviceType.BleBroadcast || t == AudioDeviceType.HearingAid;

        internal static bool IsUsb(AudioDeviceType t) =>
            t == AudioDeviceType.UsbHeadset || t == AudioDeviceType.UsbDevice || t == AudioDeviceType.UsbAccessory || t == AudioDeviceType.Dock;

        static bool IsWired(AudioDeviceType t) =>
            t == AudioDeviceType.WiredHeadset || t == AudioDeviceType.WiredHeadphones || t == AudioDeviceType.AuxLine || t == AudioDeviceType.LineAnalog;

        /// <summary>Opens Android's own output picker, or Bluetooth settings where that does not exist.</summary>
        public static void OpenPicker(Context context)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(34) && MediaRouter2.GetInstance(context).ShowSystemOutputSwitcher()) { return; }
            context.StartActivity(new Intent(Android.Provider.Settings.ActionBluetoothSettings));
        }
    }
}
