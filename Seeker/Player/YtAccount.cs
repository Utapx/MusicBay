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
using Android.Security.Keystore;
using Android.Webkit;
using Common;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Seeker.Helpers;
using System;
using System.Text;

namespace Seeker.Player
{
    /// <summary>
    /// The YouTube Music sign-in session. Its cookies give access to the Google account, so they are stored only
    /// encrypted, with a key the Android keystore holds and never lets out of the phone.
    /// </summary>
    internal static class YtAccount
    {
        const string KeyAlias = "musicbay_youtube_session";
        const string PrefsName = "musicbay_stream";
        const string SessionKey = "session";
        static bool loaded;

        public static bool SignedIn => YtMusic.Cookie != null;

        public static void Load(Context context)
        {
            if (loaded) { return; }
            loaded = true;
            string stored = Prefs(context).GetString(SessionKey, null);
            if (stored == null) { return; }
            try
            {
                var parts = stored.Split(':');
                var cipher = Cipher.GetInstance("AES/GCM/NoPadding");
                cipher.Init(CipherMode.DecryptMode, Key(), new GCMParameterSpec(128, Convert.FromBase64String(parts[0])));
                YtMusic.Cookie = Encoding.UTF8.GetString(cipher.DoFinal(Convert.FromBase64String(parts[1])));
            }
            catch (Exception ex)
            {
                // a restored backup on another phone cannot be read: the key never leaves the phone that made it
                Logger.Debug("YouTube Music session unreadable: " + ex.Message);
                SignOut(context);
            }
        }

        public static void Save(Context context, string cookie)
        {
            var cipher = Cipher.GetInstance("AES/GCM/NoPadding");
            cipher.Init(CipherMode.EncryptMode, Key());
            var data = cipher.DoFinal(Encoding.UTF8.GetBytes(cookie));
            Prefs(context).Edit().PutString(SessionKey, Convert.ToBase64String(cipher.GetIV()) + ":" + Convert.ToBase64String(data)).Apply();
            YtMusic.Cookie = cookie;
            loaded = true;
        }

        public static void SignOut(Context context)
        {
            Prefs(context).Edit().Remove(SessionKey).Apply();
            YtMusic.Cookie = null;
            CookieManager.Instance.RemoveAllCookies(null);
        }

        static ISharedPreferences Prefs(Context context) => context.GetSharedPreferences(PrefsName, FileCreationMode.Private);

        static IKey Key()
        {
            var store = KeyStore.GetInstance("AndroidKeyStore");
            store.Load(null);
            if (!store.ContainsAlias(KeyAlias))
            {
                var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore");
                generator.Init(new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                    .SetBlockModes(KeyProperties.BlockModeGcm)
                    .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                    .Build());
                generator.GenerateKey();
            }
            return store.GetKey(KeyAlias, null);
        }
    }
}
