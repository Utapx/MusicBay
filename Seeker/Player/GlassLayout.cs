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
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.Core.Graphics;
using AndroidX.Core.View;
using AndroidX.Fragment.App;
using AndroidX.RecyclerView.Widget;
using Google.Android.Material.FloatingActionButton;
using Seeker.Helpers;
using System;

namespace Seeker.Player
{
    /// <summary>
    /// A capsule of frosted glass over a live view: blur and saturation on Android 12+, plus a lens at the rim on Android 13+.
    /// Older phones get a nearly opaque capsule, since they cannot blur a live view.
    /// </summary>
    [Register("app.musicbay.GlassLayout")]
    public class GlassLayout : FrameLayout, ViewTreeObserver.IOnPreDrawListener
    {
        const float BlurDp = 12f;
        const float SaturationBoost = 1.9f;
        const float MaxRadiusDp = 32f;
        const float LensDepthDp = 18f;
        const float LensBendDp = 14f;

        // Instead of a flat tint, the range of the light behind the glass is compressed: colors stay vivid, yet
        // even pure white (dark theme) or pure black (light theme) behind it keeps labels readable.
        const float DarkScale = 0.36f, DarkLift = 26f, DarkRimGlow = 0.10f;
        const float LightScale = 0.30f, LightLift = 178f, LightRimGlow = 0.22f;

        // Near the rim, samples are pulled toward the center: the bent edge is what reads as a thick piece of glass.
        const string LensShader = @"
uniform shader content;
uniform float2 size;
uniform float radius;
uniform float depth;
uniform float bend;
uniform float glow;
float roundedBox(float2 p, float2 box, float r) {
    float2 q = abs(p) - box + r;
    return length(max(q, float2(0.0))) + min(max(q.x, q.y), 0.0) - r;
}
half4 main(float2 xy) {
    float2 center = size * 0.5;
    float2 p = xy - center;
    float rim = clamp(1.0 + roundedBox(p, center, radius) / depth, 0.0, 1.0);
    float2 toward = p / max(length(p), 1.0);
    half4 color = content.eval(xy - toward * rim * rim * bend);
    float light = 0.65 - 0.35 * p.y / center.y;
    color.rgb += half3(rim * rim * rim * light * glow);
    return color;
}";

        readonly Paint rimPaint = new Paint(PaintFlags.AntiAlias);
        readonly Paint edgePaint = new Paint(PaintFlags.AntiAlias);
        readonly int[] backdropOrigin = new int[2];
        readonly int[] ownOrigin = new int[2];
        RenderNode node;
        View backdrop;
        int pageColor, fallbackColor;
        bool dark;

        public GlassLayout(Context context, IAttributeSet attrs) : base(context, attrs) => Init();
        protected GlassLayout(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }

        void Init()
        {
            SetWillNotDraw(false);
            pageColor = PlayerFormat.ThemeColor(Context, Android.Resource.Attribute.ColorBackground);
            dark = ColorUtils.CalculateLuminance(pageColor) < 0.5;
            fallbackColor = dark ? Color.Argb(245, 36, 36, 38) : Color.Argb(245, 250, 250, 252);
            rimPaint.SetStyle(Paint.Style.Stroke);
            rimPaint.StrokeWidth = Density;
            // on a white page a white capsule needs a hairline to exist at all
            edgePaint.SetStyle(Paint.Style.Stroke);
            edgePaint.StrokeWidth = Density / 2;
            edgePaint.Color = new Color(0, 0, 0, dark ? 0 : 26);
            OutlineProvider = new CapsuleOutline(this);
            ClipToOutline = true;
            Elevation = 6 * Density;
        }

        float Density => Resources.DisplayMetrics.Density;
        float CornerRadius => Math.Min(Height / 2f, MaxRadiusDp * Density);

        public void SetBackdrop(View view)
        {
            backdrop = view;
            Invalidate();
        }

        protected override void OnAttachedToWindow()
        {
            base.OnAttachedToWindow();
            ViewTreeObserver.AddOnPreDrawListener(this);
        }

        protected override void OnDetachedFromWindow()
        {
            ViewTreeObserver.RemoveOnPreDrawListener(this);
            base.OnDetachedFromWindow();
        }

        // Only when the content behind really changed: an unconditional invalidate here would redraw at full frame rate forever.
        public bool OnPreDraw()
        {
            if (backdrop != null && backdrop.IsDirty && Visibility == ViewStates.Visible) { Invalidate(); }
            return true;
        }

        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            InvalidateOutline();
            rimPaint.SetShader(new LinearGradient(0, 0, 0, h,
                Color.Argb(dark ? 120 : 255, 255, 255, 255), Color.Argb(dark ? 28 : 110, 255, 255, 255), Shader.TileMode.Clamp));
            if (OperatingSystem.IsAndroidVersionAtLeast(31) && w > 0 && h > 0)
            {
                node = node ?? new RenderNode("glass");
                node.SetPosition(0, 0, w, h);
                node.SetRenderEffect(BuildEffect(w, h));
            }
        }

        RenderEffect BuildEffect(int w, int h)
        {
            float blur = BlurDp * Density;
            float scale = dark ? DarkScale : LightScale, lift = dark ? DarkLift : LightLift;
            var glass = new ColorMatrix();
            glass.SetSaturation(SaturationBoost);
            glass.PostConcat(new ColorMatrix(new[]
            {
                scale, 0, 0, 0, lift,
                0, scale, 0, 0, lift,
                0, 0, scale, 0, lift,
                0, 0, 0, 1f, 0,
            }));
            var frosted = RenderEffect.CreateColorFilterEffect(new ColorMatrixColorFilter(glass),
                RenderEffect.CreateBlurEffect(blur, blur, Shader.TileMode.Clamp));
            if (!OperatingSystem.IsAndroidVersionAtLeast(33)) { return frosted; }
            try
            {
                var lens = new RuntimeShader(LensShader);
                lens.SetFloatUniform("size", w, h);
                lens.SetFloatUniform("radius", CornerRadius);
                lens.SetFloatUniform("depth", LensDepthDp * Density);
                lens.SetFloatUniform("bend", LensBendDp * Density);
                lens.SetFloatUniform("glow", dark ? DarkRimGlow : LightRimGlow);
                return RenderEffect.CreateChainEffect(RenderEffect.CreateRuntimeShaderEffect(lens, "content"), frosted);
            }
            catch (Java.Lang.Exception ex)
            {
                // a GPU driver may reject the shader; frosted glass without the lens still looks right
                Logger.Debug("glass lens unavailable: " + ex.Message);
                return frosted;
            }
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            if (node != null && backdrop != null && canvas.IsHardwareAccelerated)
            {
                backdrop.GetLocationInWindow(backdropOrigin);
                GetLocationInWindow(ownOrigin);
                var recording = node.BeginRecording(Width, Height);
                recording.Translate(backdropOrigin[0] - ownOrigin[0], backdropOrigin[1] - ownOrigin[1]);
                recording.DrawColor(new Color(pageColor));
                backdrop.Draw(recording);
                node.EndRecording();
                canvas.DrawRenderNode(node);
            }
            else
            {
                canvas.DrawColor(new Color(fallbackColor));
            }
            float inset = rimPaint.StrokeWidth / 2;
            canvas.DrawRoundRect(inset, inset, Width - inset, Height - inset, CornerRadius, CornerRadius, rimPaint);
            canvas.DrawRoundRect(0, 0, Width, Height, CornerRadius, CornerRadius, edgePaint);
        }

        class CapsuleOutline : ViewOutlineProvider
        {
            readonly GlassLayout glass;
            public CapsuleOutline(GlassLayout glass) { this.glass = glass; }

            public override void GetOutline(View view, Outline outline) =>
                outline.SetRoundRect(0, 0, view.Width, view.Height, glass.CornerRadius);
        }
    }

    /// <summary>Lets every tab scroll behind the floating glass bars, while keeping its last row and floating button reachable above them.</summary>
    public static class GlassBars
    {
        const int GapDp = 8;
        static int reserved;

        public static void Install(AndroidX.AppCompat.App.AppCompatActivity activity)
        {
            var bars = activity.FindViewById(Resource.Id.glass_bars);
            var pager = activity.FindViewById(Resource.Id.pager);
            activity.FindViewById<GlassLayout>(Resource.Id.navigation_glass).SetBackdrop(pager);
            activity.FindViewById<GlassLayout>(Resource.Id.mini_player).SetBackdrop(pager);

            // content fades out below the capsules instead of colliding with the gesture bar
            int page = PlayerFormat.ThemeColor(activity, Android.Resource.Attribute.ColorBackground);
            activity.FindViewById(Resource.Id.glass_edge_fade).Background = new Android.Graphics.Drawables.GradientDrawable(
                Android.Graphics.Drawables.GradientDrawable.Orientation.BottomTop, new[] { page, ColorUtils.SetAlphaComponent(page, 0) });

            int gap = (int)(GapDp * activity.Resources.DisplayMetrics.Density);
            ViewCompat.SetOnApplyWindowInsetsListener(bars, new BottomInset(gap));
            // Home and Library hide the toolbar, and an empty app bar would otherwise collapse under the status bar
            ViewCompat.SetOnApplyWindowInsetsListener(activity.FindViewById(Resource.Id.appbarLayout), new TopInset());

            var fragments = activity.SupportFragmentManager;
            bars.LayoutChange += (s, e) =>
            {
                if (bars.Height == reserved) { return; }
                reserved = bars.Height;
                foreach (var fragment in fragments.Fragments) { if (fragment.View != null) { Reserve(fragment.View); } }
            };
            fragments.RegisterFragmentLifecycleCallbacks(new PageWatcher(), true);
        }

        internal static void Reserve(View view)
        {
            if (view is ScrollView || view is AndroidX.Core.Widget.NestedScrollView)
            {
                // On the child, not the scroll view: a scroll view ignores its own padding when deciding whether it can scroll,
                // and then a drag starting on a tappable row never scrolls.
                var content = ((ViewGroup)view).GetChildAt(0);
                if (content != null) { PadBottom(content); }
                return;
            }
            if (view is RecyclerView || view is AbsListView)
            {
                // a wrap-content strip, like the search chips, sits above the bars and would just grow taller
                if (view.LayoutParameters?.Height == ViewGroup.LayoutParams.WrapContent) { return; }
                PadBottom(view);
                ((ViewGroup)view).SetClipToPadding(false);
                return;
            }
            if (view is FloatingActionButton)
            {
                view.TranslationY = -reserved;
                return;
            }
            if (view is ViewGroup group)
            {
                for (int i = 0; i < group.ChildCount; i++) { Reserve(group.GetChildAt(i)); }
            }
        }

        static void PadBottom(View view)
        {
            // the first height seen is the view's own, so repeated calls never pile up
            var saved = view.GetTag(Resource.Id.glass_base_padding) as Java.Lang.Integer;
            int basePadding = saved?.IntValue() ?? view.PaddingBottom;
            if (saved == null) { view.SetTag(Resource.Id.glass_base_padding, Java.Lang.Integer.ValueOf(basePadding)); }
            view.SetPadding(view.PaddingLeft, view.PaddingTop, view.PaddingRight, basePadding + reserved);
        }

        class PageWatcher : FragmentManager.FragmentLifecycleCallbacks
        {
            public override void OnFragmentViewCreated(FragmentManager fm, Fragment f, View v, Android.OS.Bundle savedInstanceState) =>
                v.Post(() => Reserve(v));
        }

        class TopInset : Java.Lang.Object, IOnApplyWindowInsetsListener
        {
            public WindowInsetsCompat OnApplyWindowInsets(View v, WindowInsetsCompat insets)
            {
                v.SetPadding(v.PaddingLeft, insets.GetInsets(WindowInsetsCompat.Type.SystemBars()).Top, v.PaddingRight, v.PaddingBottom);
                return insets;
            }
        }

        class BottomInset : Java.Lang.Object, IOnApplyWindowInsetsListener
        {
            readonly int gap;
            public BottomInset(int gap) { this.gap = gap; }

            public WindowInsetsCompat OnApplyWindowInsets(View v, WindowInsetsCompat insets)
            {
                int bottom = insets.GetInsets(WindowInsetsCompat.Type.SystemBars()).Bottom;
                v.SetPadding(v.PaddingLeft, v.PaddingTop, v.PaddingRight, bottom + gap);
                return insets;
            }
        }
    }

    /// <summary>
    /// With tab swiping off, nothing claims a sideways drag any more, so lifting the finger over a wide row would count as tapping it.
    /// This cancels such a drag, unless it began on something that really moves sideways.
    /// </summary>
    internal class SidewaysDragGuard
    {
        readonly View pager, bars;
        readonly int slop;
        readonly int[] origin = new int[2];
        bool watching, swallowing;
        float startX, startY;

        public SidewaysDragGuard(Android.App.Activity activity)
        {
            pager = activity.FindViewById(Resource.Id.pager);
            bars = activity.FindViewById(Resource.Id.glass_bars);
            slop = ViewConfiguration.Get(activity).ScaledTouchSlop;
        }

        /// <summary>True when the event must not reach the app. dispatch sends the cancel that ends the press under the finger.</summary>
        public bool Filter(MotionEvent e, Func<MotionEvent, bool> dispatch)
        {
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    swallowing = false;
                    startX = e.RawX;
                    startY = e.RawY;
                    watching = Inside(pager, startX, startY) && !(bars.Visibility == ViewStates.Visible && Inside(bars, startX, startY)) && !MovesSideways(pager, startX, startY);
                    return false;
                case MotionEventActions.Move:
                    if (swallowing) { return true; }
                    if (!watching) { return false; }
                    float dx = Math.Abs(e.RawX - startX), dy = Math.Abs(e.RawY - startY);
                    if (dy > slop) { watching = false; return false; } // an up-down scroll owns this gesture
                    if (dx <= slop) { return false; }
                    swallowing = true;
                    var cancel = MotionEvent.Obtain(e);
                    cancel.Action = MotionEventActions.Cancel;
                    dispatch(cancel);
                    cancel.Recycle();
                    return true;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    watching = false;
                    bool swallowed = swallowing;
                    swallowing = false;
                    return swallowed;
                default:
                    return swallowing;
            }
        }

        bool Inside(View view, float x, float y)
        {
            view.GetLocationOnScreen(origin);
            return x >= origin[0] && x < origin[0] + view.Width && y >= origin[1] && y < origin[1] + view.Height;
        }

        // only the topmost child under the finger counts: a row covered by the Account panel is not what is being touched
        bool MovesSideways(View view, float x, float y)
        {
            if (view is EditText || view is SeekBar || view is HorizontalScrollView) { return true; }
            // the pager and its page list still report sideways scrolling even with swiping turned off
            bool isPager = view == pager || view.Parent == pager;
            if (!isPager && (view.CanScrollHorizontally(1) || view.CanScrollHorizontally(-1))) { return true; }
            if (view is ViewGroup group)
            {
                for (int i = group.ChildCount - 1; i >= 0; i--)
                {
                    var child = group.GetChildAt(i);
                    if (child.Visibility == ViewStates.Visible && Inside(child, x, y)) { return MovesSideways(child, x, y); }
                }
            }
            return false;
        }
    }

    /// <summary>The tabs inside the glass capsule, with a highlight that slides to the selected one.</summary>
    [Register("app.musicbay.GlassTabBar")]
    public class GlassTabBar : FrameLayout
    {
        const int BrowsePage = 3; // Browse has no tab of its own and sits under Search
        const int SearchTabIndex = 1;
        const int LensInsetDp = 4;
        const long LensMoveMs = 300;

        static readonly (int Page, int Icon, int Label)[] Tabs =
        {
            (0, Resource.Drawable.ic_tab_home, Resource.String.home_tab),
            (1, Resource.Drawable.ic_tab_search, Resource.String.searches_tab2),
            (4, Resource.Drawable.ic_tab_library, Resource.String.library_tab),
            (2, Resource.Drawable.ic_tab_downloads, Resource.String.downloads_tab),
        };

        /// <summary>Raised with the pager page of the tab the user tapped.</summary>
        public Action<int> PageSelected;

        readonly View lens;
        readonly LinearLayout row;
        int selected;

        public GlassTabBar(Context context, IAttributeSet attrs) : base(context, attrs)
        {
            lens = new View(context);
            AddView(lens);
            row = new LinearLayout(context) { Orientation = Android.Widget.Orientation.Horizontal };
            AddView(row, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            var inflater = LayoutInflater.From(context);
            for (int i = 0; i < Tabs.Length; i++)
            {
                int index = i;
                var tab = inflater.Inflate(Resource.Layout.tab_item, row, false);
                tab.FindViewById<ImageView>(Resource.Id.tab_icon).SetImageResource(Tabs[i].Icon);
                tab.FindViewById<TextView>(Resource.Id.tab_label).SetText(Tabs[i].Label);
                tab.Click += (s, e) =>
                {
                    MoveTo(index, true);
                    PageSelected?.Invoke(Tabs[index].Page);
                };
                row.AddView(tab);
            }
            Tint();
        }

        protected GlassTabBar(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }

        /// <summary>Follows the pager when something other than a tap changed the page.</summary>
        public void Select(int page)
        {
            int index = page == BrowsePage ? SearchTabIndex : Array.FindIndex(Tabs, t => t.Page == page);
            if (index >= 0) { MoveTo(index, true); }
        }

        void MoveTo(int index, bool animate)
        {
            bool changed = index != selected;
            selected = index;
            if (changed) { Tint(); }
            float x = LensX(index);
            if (animate && changed && Width > 0)
            {
                lens.Animate().TranslationX(x).SetDuration(LensMoveMs).SetInterpolator(new Android.Views.Animations.OvershootInterpolator(1.1f));
            }
            else
            {
                lens.TranslationX = x;
            }
        }

        float LensX(int index) => index * (float)Width / Tabs.Length + LensInsetDp * Resources.DisplayMetrics.Density;

        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            int inset = (int)(LensInsetDp * Resources.DisplayMetrics.Density);
            int lensHeight = h - 2 * inset;
            var capsule = new Android.Graphics.Drawables.GradientDrawable();
            capsule.SetColor(PlayerFormat.ThemeColor(Context, Resource.Attribute.whitePurpleForNavBar));
            capsule.SetCornerRadius(lensHeight / 2f);
            lens.Background = capsule;
            lens.TranslationY = inset;
            var size = new LayoutParams(w / Tabs.Length - 2 * inset, lensHeight);
            Post(() => { lens.LayoutParameters = size; });
            lens.TranslationX = LensX(selected);
        }

        void Tint()
        {
            int active = PlayerFormat.ThemeColor(Context, Resource.Attribute.mainPurple);
            int idle = PlayerFormat.ThemeColor(Context, Resource.Attribute.navItemUnselectedColor);
            for (int i = 0; i < row.ChildCount; i++)
            {
                var tab = row.GetChildAt(i);
                var color = new Color(i == selected ? active : idle);
                tab.Selected = i == selected;
                tab.FindViewById<ImageView>(Resource.Id.tab_icon).SetColorFilter(color);
                tab.FindViewById<TextView>(Resource.Id.tab_label).SetTextColor(color);
            }
        }
    }

    /// <summary>The hairline between list rows, starting where the text starts.</summary>
    internal class InsetDivider : RecyclerView.ItemDecoration
    {
        const int InsetDp = 76;
        readonly Paint paint = new Paint();
        readonly int inset;

        public InsetDivider(Context context)
        {
            float density = context.Resources.DisplayMetrics.Density;
            inset = (int)(InsetDp * density);
            paint.Color = new Color(PlayerFormat.ThemeColor(context, Resource.Attribute.cellStroke));
            paint.StrokeWidth = Math.Max(1f, density / 2);
        }

        public override void OnDrawOver(Canvas c, RecyclerView parent, RecyclerView.State state)
        {
            if (parent.GetLayoutManager() is GridLayoutManager) { return; }
            int last = (parent.GetAdapter()?.ItemCount ?? 0) - 1;
            for (int i = 0; i < parent.ChildCount; i++)
            {
                var child = parent.GetChildAt(i);
                if (parent.GetChildAdapterPosition(child) == last) { continue; }
                c.DrawLine(inset, child.Bottom, parent.Width, child.Bottom, paint);
            }
        }
    }
}
