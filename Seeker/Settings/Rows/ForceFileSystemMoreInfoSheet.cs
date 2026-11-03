// Copyright (c) 2026 Arsen
// Modified: links point to the MusicBay repository, removed the Seeker F-Droid link

using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Google.Android.Material.BottomSheet;

namespace Seeker.Settings.Rows
{
    public sealed class ForceFileSystemMoreInfoSheet : BottomSheetDialogFragment
    {
        private ISettingsHost _host;

        public static void Show(ISettingsHost host)
        {
            var sheet = new ForceFileSystemMoreInfoSheet { _host = host };
            sheet.Show(((AndroidX.AppCompat.App.AppCompatActivity)host.Activity).SupportFragmentManager, "ForceFileSystemMoreInfo");
        }

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            var view = inflater.Inflate(Resource.Layout.force_filesystem_more_info_sheet, container, false);

            // MusicBay has no F-Droid listing, and the old button opened the original app's page
            view.FindViewById<Button>(Resource.Id.get_izzydroid_build).Visibility = ViewStates.Gone;

            view.FindViewById<Button>(Resource.Id.get_github_build).Click += (s, e) =>
            {
                Dismiss();
                CommonHelpers.ViewUri(Android.Net.Uri.Parse("https://github.com/Utapx/MusicBay/releases"), this.Context);
            };

            return view;
        }
    }
}
