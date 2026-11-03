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
using Android.Widget;
using AndroidX.Core.Content;
using Common;
using Google.Android.Material.Dialog;
using Google.Android.Material.ProgressIndicator;
using Org.Json;
using Seeker.Helpers;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Seeker.Player
{
    /// <summary>Offers a new GitHub release when the app opens. Nothing is downloaded until the user taps Update.</summary>
    internal static class AppUpdater
    {
#if MOCK
        const string LatestReleaseUrl = "http://localhost:8765/latest.json";
#else
        const string LatestReleaseUrl = "https://api.github.com/repos/ssenwako/MusicBay/releases/latest";
#endif
        const string PrefsName = "musicbay_player";
        const string SkippedVersionKey = "update_skipped_version";
        const string ApkMimeType = "application/vnd.android.package-archive";
        const int BufferSize = 81920;

        static readonly HttpClient http = CreateClient();
        static bool busy;

        class Release
        {
            public string Tag, Notes, ApkUrl;
        }

        static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // a phone can take minutes over the APK; requests set their own limit
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MusicBay"); // GitHub rejects requests without one
            return client;
        }

        /// <summary>Quiet: no network or no new version shows nothing, so the player is never in the way of music.</summary>
        public static void CheckOnLaunch(Activity activity) => Check(activity, false);

        /// <summary>From the Account panel: reports every outcome, and offers a version the user skipped earlier.</summary>
        public static void CheckNow(Activity activity) => Check(activity, true);

        static void Check(Activity activity, bool userAsked)
        {
            if (busy) { return; }
            busy = true;
            string installed = CommonHelpers.GetVersionString();
            Task.Run(async () =>
            {
                Release release = null;
                bool failed = false;
                try { release = await FetchLatest(); }
                catch (Exception ex)
                {
                    Logger.Debug("update check failed: " + ex.Message);
                    failed = true;
                }
                activity.RunOnUiThread(() =>
                {
                    busy = false;
                    if (activity.IsFinishing || activity.IsDestroyed) { return; }
                    if (failed)
                    {
                        if (userAsked) { Toast.MakeText(activity, Resource.String.update_check_failed, ToastLength.Short).Show(); }
                        return;
                    }
                    if (release == null || !VersionCompare.IsNewer(release.Tag, installed))
                    {
                        if (userAsked) { Toast.MakeText(activity, Resource.String.update_up_to_date, ToastLength.Short).Show(); }
                        return;
                    }
                    if (!userAsked && Prefs(activity).GetString(SkippedVersionKey, null) == release.Tag) { return; }
                    Offer(activity, release);
                });
            });
        }

        static ISharedPreferences Prefs(Context context) => context.GetSharedPreferences(PrefsName, FileCreationMode.Private);

        static async Task<Release> FetchLatest()
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = await http.GetAsync(LatestReleaseUrl, limit.Token);
            // GitHub answers 404 until a release exists
            if (!response.IsSuccessStatusCode) { return null; }
            var json = new JSONObject(await response.Content.ReadAsStringAsync());
            var assets = json.GetJSONArray("assets");
            for (int i = 0; i < assets.Length(); i++)
            {
                string url = assets.GetJSONObject(i).GetString("browser_download_url");
                if (url.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) && IsTrustedUrl(url))
                {
                    return new Release { Tag = json.GetString("tag_name"), Notes = json.OptString("body", ""), ApkUrl = url };
                }
            }
            return null;
        }

#if MOCK
        static bool IsTrustedUrl(string url) => url.StartsWith("http://localhost:");
#else
        // an installer must only ever come over an encrypted connection
        static bool IsTrustedUrl(string url) => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
#endif

        static void Offer(Activity activity, Release release)
        {
            string message = activity.GetString(Resource.String.update_version, release.Tag.TrimStart('v', 'V'));
            if (!string.IsNullOrWhiteSpace(release.Notes)) { message += "\n\n" + release.Notes.Trim(); }
            new MaterialAlertDialogBuilder(activity)
                .SetTitle(Resource.String.update_available)
                .SetMessage(message)
                .SetPositiveButton(Resource.String.update_now, (s, e) => Download(activity, release))
                .SetNegativeButton(Resource.String.update_later, (s, e) => Prefs(activity).Edit().PutString(SkippedVersionKey, release.Tag).Apply())
                .Show();
        }

        static void Download(Activity activity, Release release)
        {
            var cancel = new CancellationTokenSource();
            var progress = new LinearProgressIndicator(activity) { Max = 100 };
            int margin = (int)(24 * activity.Resources.DisplayMetrics.Density);
            var holder = new FrameLayout(activity);
            holder.SetPadding(margin, margin / 2, margin, margin / 2);
            holder.AddView(progress);
            var dialog = new MaterialAlertDialogBuilder(activity)
                .SetTitle(Resource.String.update_downloading)
                .SetView(holder)
                .SetCancelable(false)
                .SetNegativeButton(Android.Resource.String.Cancel, (s, e) => cancel.Cancel())
                .Show();

            string folder = Path.Combine(activity.ExternalCacheDir.AbsolutePath, "updates");
            Task.Run(async () =>
            {
                string apk = null;
                try { apk = await Save(release.ApkUrl, folder, percent => activity.RunOnUiThread(() => progress.SetProgressCompat(percent, true)), cancel.Token); }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Logger.Debug("update download failed: " + ex.Message); }
                activity.RunOnUiThread(() =>
                {
                    if (activity.IsDestroyed) { return; }
                    dialog.Dismiss();
                    if (apk == null) { Toast.MakeText(activity, Resource.String.update_failed, ToastLength.Long).Show(); }
                    else { Install(activity, apk); }
                });
            });
        }

        static async Task<string> Save(string url, string folder, Action<int> onPercent, CancellationToken cancel)
        {
            // only the newest APK is kept, so updates do not pile up in storage
            if (Directory.Exists(folder)) { Directory.Delete(folder, true); }
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, "MusicBay.apk");
            string partial = target + ".part";

            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            long received = 0;
            int lastPercent = -1;
            using (var input = await response.Content.ReadAsStreamAsync())
            using (var output = File.Create(partial))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancel)) > 0)
                {
                    await output.WriteAsync(buffer, 0, read, cancel);
                    received += read;
                    int percent = total > 0 ? (int)(received * 100 / total.Value) : 0;
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        onPercent(percent);
                    }
                }
            }
            // a dropped connection can end the stream early without an error; Android would then reject a broken APK with a confusing message
            if (total.HasValue && received != total.Value) { throw new IOException("incomplete download"); }
            File.Move(partial, target);
            return target;
        }

        static void Install(Activity activity, string apkPath)
        {
            var uri = FileProvider.GetUriForFile(activity, activity.PackageName + ".provider", new Java.IO.File(apkPath));
            var intent = new Intent(Intent.ActionView).SetDataAndType(uri, ApkMimeType).AddFlags(ActivityFlags.GrantReadUriPermission);
            try
            {
                activity.StartActivity(intent);
            }
            catch (ActivityNotFoundException)
            {
                Toast.MakeText(activity, Resource.String.update_failed, ToastLength.Long).Show();
            }
        }
    }
}
