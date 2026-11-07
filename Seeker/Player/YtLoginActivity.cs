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
using Android.OS;
using Android.Webkit;
using Common;

namespace Seeker.Player
{
    /// <summary>Google's own sign-in page. The password goes to Google only; MusicBay keeps the session cookies it ends with.</summary>
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", Exported = false, ParentActivity = typeof(MainActivity))]
    public class YtLoginActivity : ThemeableActivity
    {
        const string MusicHome = "https://music.youtube.com";

        WebView web;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            CookieManager.Instance.RemoveAllCookies(null); // start clean, so an old session cannot be picked up instead
            web = new WebView(this);
            web.Settings.JavaScriptEnabled = true;
            web.Settings.DomStorageEnabled = true;
            // Google refuses sign-in from pages that announce an embedded WebView
            web.Settings.UserAgentString = web.Settings.UserAgentString.Replace("; wv", "");
            web.SetWebViewClient(new Client(this));
            // the container takes the status and navigation bar insets as padding; the page would draw under them
            var frame = new Android.Widget.FrameLayout(this);
            frame.SetFitsSystemWindows(true);
            frame.AddView(web);
            SetContentView(frame);
            web.LoadUrl("https://accounts.google.com/ServiceLogin?service=youtube&continue=" + Android.Net.Uri.Encode(MusicHome + "/"));
        }

        public override void OnBackPressed()
        {
            if (web.CanGoBack()) { web.GoBack(); } else { base.OnBackPressed(); }
        }

        protected override void OnDestroy()
        {
            web?.Destroy();
            base.OnDestroy();
        }

        class Client : WebViewClient
        {
            readonly YtLoginActivity activity;

            public Client(YtLoginActivity activity) => this.activity = activity;

            public override void OnPageFinished(WebView view, string url)
            {
                base.OnPageFinished(view, url);
                if (activity.IsFinishing || url == null || !url.StartsWith(MusicHome)) { return; }
                string cookie = CookieManager.Instance.GetCookie(MusicHome);
                if (!YtMusic.IsSession(cookie)) { return; }
                YtAccount.Save(activity, cookie);
                // the encrypted copy is the only one kept: the WebView's own cookie store is not encrypted
                CookieManager.Instance.RemoveAllCookies(null);
                activity.SetResult(Result.Ok);
                activity.Finish();
            }
        }
    }
}
