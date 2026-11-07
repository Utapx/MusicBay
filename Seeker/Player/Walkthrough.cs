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
using Android.Views;
using Android.Widget;
using Google.Android.Material.Button;
using Seeker.Helpers;
using System;

namespace Seeker.Player
{
    /// <summary>
    /// A first-run tour: the screen dims, one feature at a time is lit up, and a card explains it.
    /// Only for a fresh install; people who updated already know the app.
    /// </summary>
    internal class Walkthrough : FrameLayout
    {
        const string PrefsName = "musicbay_player";
        const string DoneKey = "walkthrough_done";
        const int HolePadDp = 6;
        const int CardGapDp = 16;
        const int MaxCardWidthDp = 400;

        readonly (Func<View> Target, int Title, int Body)[] steps;
        readonly Paint clear = new Paint(PaintFlags.AntiAlias);
        readonly LinearLayout card;
        readonly TextView title, body, counter;
        readonly MaterialButton next;
        readonly float density;
        RectF hole;
        int step;
        GenericOnBackPressedCallback back;

        public static void StartIfNew(AndroidX.AppCompat.App.AppCompatActivity activity)
        {
            var prefs = activity.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            if (prefs.GetBoolean(DoneKey, false)) { return; }
            // marked before it shows: the tour is offered once at most, and an update never offers it
            prefs.Edit().PutBoolean(DoneKey, true).Apply();
#pragma warning disable CS0618 // the PackageInfoFlags overload needs API 33; minSdk is 23
            var info = activity.PackageManager.GetPackageInfo(activity.PackageName, 0);
#pragma warning restore CS0618
            if (info.FirstInstallTime != info.LastUpdateTime) { return; }

            var tabs = activity.FindViewById<GlassTabBar>(Resource.Id.navigation);
            var decor = (ViewGroup)activity.Window.DecorView;
            // after the first layout, so the tabs and the Home header have their places on screen
            decor.PostDelayed(() =>
            {
                if (activity.IsFinishing || activity.IsDestroyed) { return; }
                var tour = new Walkthrough(activity, new (Func<View>, int, int)[]
                {
                    (null, Resource.String.walk_welcome_title, Resource.String.walk_welcome),
                    (() => tabs?.TabFor(0), Resource.String.home_tab, Resource.String.walk_home),
                    (() => tabs?.TabFor(1), Resource.String.searches_tab2, Resource.String.walk_search),
                    (() => tabs?.TabFor(4), Resource.String.library_tab, Resource.String.walk_library),
                    (() => tabs?.TabFor(2), Resource.String.downloads_tab, Resource.String.walk_downloads),
                    (() => activity.FindViewById(Resource.Id.home_avatar), Resource.String.account_title, Resource.String.walk_account),
                });
                // back closes the tour rather than the app; with predictive back, views never receive the key
                tour.back = new GenericOnBackPressedCallback(true, cb => tour.Finish());
                activity.OnBackPressedDispatcher.AddCallback(tour.back);
                decor.AddView(tour, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            }, 800);
        }

        Walkthrough(Context context, (Func<View>, int, int)[] steps) : base(context)
        {
            this.steps = steps;
            density = context.Resources.DisplayMetrics.Density;
            clear.SetXfermode(new PorterDuffXfermode(PorterDuff.Mode.Clear));
            SetLayerType(LayerType.Hardware, null); // the hole is cut out of this view's own layer
            SetWillNotDraw(false);
            Clickable = true; // the app underneath must not react while the tour is open

            int text = PlayerFormat.ThemeColor(context, Resource.Attribute.cellTextColor);
            int subdued = PlayerFormat.ThemeColor(context, Resource.Attribute.cellTextColorSubdued);
            card = new LinearLayout(context) { Orientation = Orientation.Vertical };
            int pad = Dp(20);
            card.SetPadding(pad, pad, pad, Dp(8));
            var background = new GradientDrawable();
            background.SetColor(PlayerFormat.ThemeColor(context, Resource.Attribute.colorSurfaceContainerHigh));
            background.SetCornerRadius(Dp(24));
            card.Background = background;
            card.Elevation = Dp(8);

            title = new TextView(context) { TextSize = 20 };
            title.SetTypeface(Typeface.Create("sans-serif-medium", TypefaceStyle.Normal), TypefaceStyle.Normal);
            title.SetTextColor(new Color(text));
            body = new TextView(context) { TextSize = 15 };
            body.SetTextColor(new Color(subdued));
            body.SetPadding(0, Dp(8), 0, Dp(8));
            card.AddView(title);
            card.AddView(body);

            var buttons = new LinearLayout(context) { Orientation = Orientation.Horizontal };
            buttons.SetGravity(GravityFlags.CenterVertical);
            counter = new TextView(context) { TextSize = 13 };
            counter.SetTextColor(new Color(subdued));
            buttons.AddView(counter, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
            var skip = new MaterialButton(context, null, Resource.Attribute.borderlessButtonStyle) { Text = context.GetString(Resource.String.walk_skip) };
            skip.Click += (s, e) => Finish();
            buttons.AddView(skip);
            next = new MaterialButton(context);
            next.Click += (s, e) => { if (step + 1 < steps.Length) { Show(step + 1); } else { Finish(); } };
            buttons.AddView(next);
            card.AddView(buttons);
            AddView(card, new LayoutParams(Math.Min(context.Resources.DisplayMetrics.WidthPixels - Dp(32), Dp(MaxCardWidthDp)), ViewGroup.LayoutParams.WrapContent));
            Post(() => Show(0));
        }

        int Dp(int dp) => (int)(dp * density);

        void Show(int index)
        {
            step = index;
            var (target, titleId, bodyId) = steps[index];
            title.SetText(titleId);
            body.SetText(bodyId);
            counter.Text = Context.GetString(Resource.String.walk_step, index + 1, steps.Length);
            next.SetText(index + 1 < steps.Length ? Resource.String.walk_next : Resource.String.walk_done);

            var view = target?.Invoke();
            hole = null;
            if (view != null && view.IsShown && view.Width > 0)
            {
                var at = new int[2];
                view.GetLocationInWindow(at);
                float pad = Dp(HolePadDp);
                hole = new RectF(at[0] - pad, at[1] - pad, at[0] + view.Width + pad, at[1] + view.Height + pad);
            }
            card.Post(PlaceCard);
            Invalidate();
            card.AnnounceForAccessibility(title.Text + ". " + body.Text);
        }

        // centred with nothing lit up; otherwise on whichever side of the hole has more room
        void PlaceCard()
        {
            card.TranslationX = (Width - card.Width) / 2f;
            if (hole == null) { card.TranslationY = (Height - card.Height) / 2f; return; }
            bool above = hole.CenterY() > Height / 2f;
            card.TranslationY = above ? hole.Top - Dp(CardGapDp) - card.Height : hole.Bottom + Dp(CardGapDp);
        }

        protected override void DispatchDraw(Canvas canvas)
        {
            canvas.DrawColor(new Color(0, 0, 0, 160));
            if (hole != null)
            {
                float radius = Math.Min(hole.Height(), hole.Width()) / 2f;
                canvas.DrawRoundRect(hole, radius, radius, clear);
            }
            base.DispatchDraw(canvas);
        }

        void Finish()
        {
            back?.Remove();
            (Parent as ViewGroup)?.RemoveView(this);
        }
    }
}
