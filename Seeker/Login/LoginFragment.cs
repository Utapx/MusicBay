/*
 * Copyright 2021 Seeker
 * Copyright (c) 2026 Arsen
 * Modified: Home shows the music section above the Soulseek account section
 *
 * This file is part of Seeker
 *
 * Seeker is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * Seeker is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with Seeker. If not, see <http://www.gnu.org/licenses/>.
 */

using Seeker.Services;
using Android.Content;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Fragment.App;
using Google.Android.Material.TextField;
using Soulseek;
using System;
using System.Linq;
using System.Threading.Tasks;
using Seeker.Helpers;
using Seeker.Messages;
using Common;
namespace Seeker
{
    public class LoginFragment : Fragment
    {
        public const string LogoutMessage = "UserLogout";

        private ViewFlipper viewFlipper;
        private View rootView;

        // Login form views (child 0)
        private Button loginButton;
        private EditText usernameTextEdit;
        private EditText passwordTextEdit;
        private TextInputLayout usernameInputLayout;

        // Logged-in views (child 2)
        private View mustSelectDirButton;
        private TextView welcomeTextView;
        private View connectionStatusDot;
        private TextView connectionStatusText;
        private View connectionStatusChip;

        private View serverMessageCard;
        private TextView serverMessageText;
        private TextView serverMessageTime;

        // Menu rows
        private View menuSetUpSharing;
        private View menuManageUserList;
        private View menuMessages;
        private TextView messagesUnreadBadge;
        private View menuSettings;
        private View menuLogout;

        private const int ChildLoginForm = 0;
        private const int ChildLoading = 1;
        private const int ChildLoggedIn = 2;

        private View accountPanel;
        private ClosePanelOnBack closePanelOnBack;

        private sealed class ClosePanelOnBack : AndroidX.Activity.OnBackPressedCallback
        {
            private readonly LoginFragment fragment;
            public ClosePanelOnBack(LoginFragment fragment) : base(false) { this.fragment = fragment; }
            public override void HandleOnBackPressed() => fragment.CloseAccountPanel();
        }

        /// <summary>Shows the account and the app-level destinations, which used to live in the top bar.</summary>
        public void OpenAccountPanel()
        {
            if (accountPanel == null) { return; }
            accountPanel.Visibility = ViewStates.Visible;
            closePanelOnBack.Enabled = true;
        }

        public void CloseAccountPanel()
        {
            if (accountPanel == null) { return; }
            accountPanel.Visibility = ViewStates.Gone;
            closePanelOnBack.Enabled = false;
        }

        private void SetUpAccountPanel()
        {
            accountPanel = rootView.FindViewById(Resource.Id.account_panel);
            closePanelOnBack = new ClosePanelOnBack(this);
            rootView.FindViewById(Resource.Id.account_done).Click += (s, e) => CloseAccountPanel();

            // one list holds these, for signed-in and signed-out alike
            menuManageUserList.Visibility = ViewStates.Gone;
            menuMessages.Visibility = ViewStates.Gone;
            menuSettings.Visibility = ViewStates.Gone;
            rootView.FindViewById(Resource.Id.menuCommunicationHeader).Visibility = ViewStates.Gone;

            var rows =rootView.FindViewById<LinearLayout>(Resource.Id.account_rows);
            void Action(int actionId) => SeekerState.MainActivityRef.HandleAccountAction(actionId);
            var messages = AddAccountRow(rows, Resource.Drawable.baseline_chat_bubble_white_24, Resource.String.messages, () => Action(Resource.Id.messages_action));
            messagesUnreadBadge = messages.FindViewById<TextView>(Resource.Id.account_row_badge);
            AddAccountRow(rows, Resource.Drawable.outline_forum_white_24, Resource.String.chatrooms, () => Action(Resource.Id.chatroom_action));
            AddAccountRow(rows, Resource.Drawable.baseline_group_white_24, Resource.String.target_user_list, () => Action(Resource.Id.user_list_action));
            AddAccountRow(rows, Resource.Drawable.baseline_folder_white_24, Resource.String.browse_tab, () =>
            {
                CloseAccountPanel();
                SeekerState.MainActivityRef.GoToTab(BrowsePage);
            });
            AddAccountRow(rows, Resource.Drawable.ic_equalizer_24, Resource.String.sound_title, () => StartActivity(new Intent(Activity, typeof(Seeker.Player.SoundActivity))));
            AddAccountRow(rows, Resource.Drawable.baseline_settings_white_24, Resource.String.settings, () => Action(Resource.Id.settings_action));
            AddAccountRow(rows, Resource.Drawable.ic_tab_downloads, Resource.String.update_check, () => Seeker.Player.AppUpdater.CheckNow(Activity));
            AddAccountRow(rows, Resource.Drawable.information_outline, Resource.String.about_title, () => Action(Resource.Id.about_action));
            AddAccountRow(rows, Resource.Drawable.power_settings_new_28dp, Resource.String.shutdown, () => Action(Resource.Id.shutdown_action));
        }

        private View AddAccountRow(LinearLayout rows, int icon, int title, Action onClick)
        {
            var row = LayoutInflater.From(rows.Context).Inflate(Resource.Layout.row_account, rows, false);
            row.FindViewById<ImageView>(Resource.Id.account_row_icon).SetImageResource(icon);
            row.FindViewById<TextView>(Resource.Id.account_row_title).SetText(title);
            row.Click += (s, e) => onClick();
            if (rows.ChildCount > 0)
            {
                var line = new View(rows.Context);
                var value = new Android.Util.TypedValue();
                rows.Context.Theme.ResolveAttribute(Resource.Attribute.cellStroke, value, true);
                line.SetBackgroundColor(new Android.Graphics.Color(value.Data));
                var size = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 1);
                size.LeftMargin = (int)(52 * rows.Resources.DisplayMetrics.Density);
                rows.AddView(line, size);
            }
            rows.AddView(row);
            return row;
        }

        private const int BrowsePage = 3;

        public override void OnResume()
        {
            base.OnResume();

            SeekerState.SoulseekClient.StateChanged += SoulseekClient_StateChanged;
            UpdateConnectionStatus(SeekerState.SoulseekClient.State);

            MessageController.MessageReceived += OnMessageReceivedUpdateBadge;
            MessagesBroadcastReceiver.MarkAsReadFromNotification += OnMarkAsReadUpdateBadge;
            UpdateUnreadBadge();

            SessionService.LoginStarted += OnLoginStarted;
            SessionService.LoginCompleted += OnLoginCompleted;
            ServerMessageService.Changed += OnServerMessageChanged;
            RenderFromState();
            UpdateServerMessage();
            Seeker.Player.HomeMusicSection.Bind(rootView, Activity);
        }

        public override void OnPause()
        {
            base.OnPause();
            SeekerState.SoulseekClient.StateChanged -= SoulseekClient_StateChanged;
            MessageController.MessageReceived -= OnMessageReceivedUpdateBadge;
            MessagesBroadcastReceiver.MarkAsReadFromNotification -= OnMarkAsReadUpdateBadge;
            SessionService.LoginStarted -= OnLoginStarted;
            SessionService.LoginCompleted -= OnLoginCompleted;
            ServerMessageService.Changed -= OnServerMessageChanged;
        }

        private void OnServerMessageChanged(object sender, EventArgs e)
        {
            UpdateServerMessage();
        }

        private void UpdateServerMessage()
        {
            if (serverMessageCard == null)
            {
                return;
            }
            if (ServerMessageService.HasActiveMessage)
            {
                serverMessageText.Text = ServerMessageService.Text;
                DateTime receivedUtc = ServerMessageService.ReceivedUtc;
                serverMessageTime.Text = CommonHelpers.GetRecentTimeNiceFormated(receivedUtc, DateTime.UtcNow - receivedUtc);
                serverMessageCard.Visibility = ViewStates.Visible;
            }
            else
            {
                serverMessageCard.Visibility = ViewStates.Gone;
            }
        }

        private void OnLoginStarted(object sender, EventArgs e)
        {
            this.Activity?.RunOnUiThread(() =>
            {
                UpdateConnectionStatus(SeekerState.SoulseekClient.State);
            });
        }

        private void OnLoginCompleted(object sender, LoginCompletedEventArgs e)
        {
            this.Activity?.RunOnUiThread(() =>
            {
                RenderFromState();
            });
        }

        private void SoulseekClient_StateChanged(object sender, SoulseekClientStateChangedEventArgs e)
        {
            this.Activity?.RunOnUiThread(() =>
            {
                UpdateConnectionStatus(e.State);
            });
        }

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            Logger.Debug("LoginFragmentOnCreateView");
            SeekerState.LoginFragmentRef = this;

            rootView = inflater.Inflate(Resource.Layout.login_viewflipper, container, false);
            viewFlipper = rootView.FindViewById<ViewFlipper>(Resource.Id.loginViewFlipper);

            SetUpLoginFormViews();
            SetUpLoggedInViews();
            SetUpAccountPanel();

            ReconnectIfNeeded();
            RenderFromState();

            return rootView;
        }

        /// <summary>
        /// If we are supposed to be logged in but we are not currently either connected 
        ///   or logging in then trigger login here. This is a "background" log in
        /// </summary>
        private void ReconnectIfNeeded()
        {
            if (SessionService.Instance.IsNotLoggedIn())
            {
                return;
            }
            if (SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.LoggedIn)
                || SessionService.InFlightLoginOrigin != null)
            {
                return;
            }

            SeekerState.ManualResetEvent.Reset();
            Task login = SessionService.BeginLogin(
                LoginOrigin.Background, PreferencesState.Username, PreferencesState.Password);
            login?.ContinueWith(MainActivity.GetPostNotifPermissionTask());
            SeekerApplication.SetUpLoginContinueWith(login);
        }

        /// <summary>
        /// Determines ViewFlipper
        /// If Interactive (i.e. user clicked login and we have never logged in b4) then 
        ///   show the loading viewflipper ELSE always show logged in but with "connecting"
        /// </summary>
        private void RenderFromState()
        {
            if (SessionService.InFlightLoginOrigin == LoginOrigin.Interactive)
            {
                viewFlipper.DisplayedChild = ChildLoading;
            }
            else if (SessionService.Instance.IsNotLoggedIn())
            {
                if (string.IsNullOrEmpty(usernameTextEdit.Text) && !string.IsNullOrEmpty(PreferencesState.Username))
                {
                    usernameTextEdit.Text = PreferencesState.Username;
                    passwordTextEdit.Text = PreferencesState.Password;
                }
                viewFlipper.DisplayedChild = ChildLoginForm;
            }
            else
            {
                ShowLoggedIn();
            }
        }

        public override void OnViewCreated(View view, Bundle savedInstanceState)
        {
            base.OnViewCreated(view, savedInstanceState);
            RequireActivity().OnBackPressedDispatcher.AddCallback(ViewLifecycleOwner, closePanelOnBack);
            SeekerState.ManualResetEvent.Set();
        }

        private void SetUpLoginFormViews()
        {
            loginButton = rootView.FindViewById<Button>(Resource.Id.buttonLogin);
            loginButton.Click += LogInClick;
            usernameTextEdit = rootView.FindViewById<EditText>(Resource.Id.etUsername);
            passwordTextEdit = rootView.FindViewById<EditText>(Resource.Id.etPassword);
            usernameInputLayout = rootView.FindViewById<TextInputLayout>(Resource.Id.usernameTextInputLayout);
            usernameTextEdit.TextChanged += UsernamePasswordTextEdit_TextChanged;
            usernameTextEdit.FocusChange += UiHelpers.OnFocusAdjustNothing;
            usernameTextEdit.FocusChange += UsernameTextEdit_FocusChange;
            passwordTextEdit.TextChanged += UsernamePasswordTextEdit_TextChanged;
            passwordTextEdit.FocusChange += UiHelpers.OnFocusAdjustNothing;
            bool hasError = ValidateUsername(true);
            EnableDisableLoginButton(usernameTextEdit, passwordTextEdit, loginButton, hasError);
        }

        private void SetUpLoggedInViews()
        {
            mustSelectDirButton = rootView.FindViewById<View>(Resource.Id.mustSelectDirectory);
            welcomeTextView = rootView.FindViewById<TextView>(Resource.Id.userNameView);
            connectionStatusDot = rootView.FindViewById<View>(Resource.Id.connectionStatusDot);
            connectionStatusText = rootView.FindViewById<TextView>(Resource.Id.connectionStatusText);
            connectionStatusChip = rootView.FindViewById<View>(Resource.Id.connectionStatusChip);

            serverMessageCard = rootView.FindViewById<View>(Resource.Id.serverMessageCard);
            serverMessageText = rootView.FindViewById<TextView>(Resource.Id.serverMessageText);
            serverMessageTime = rootView.FindViewById<TextView>(Resource.Id.serverMessageTime);
            rootView.FindViewById<View>(Resource.Id.serverMessageDismiss).Click += (s, e) => ServerMessageService.Dismiss();

            menuSetUpSharing = rootView.FindViewById<View>(Resource.Id.menuSetUpSharing);
            menuManageUserList = rootView.FindViewById<View>(Resource.Id.menuManageUserList);
            menuMessages = rootView.FindViewById<View>(Resource.Id.menuMessages);
            messagesUnreadBadge = rootView.FindViewById<TextView>(Resource.Id.messagesUnreadBadge);
            menuSettings = rootView.FindViewById<View>(Resource.Id.menuSettings);
            menuLogout = rootView.FindViewById<View>(Resource.Id.menuLogout);

            menuManageUserList.Click += (s, e) =>
            {
                Intent intent = new Intent(SeekerState.MainActivityRef, typeof(UserListActivity));
                SeekerState.MainActivityRef.StartActivityForResult(intent, 141);
            };
            menuMessages.Click += (s, e) =>
            {
                Intent intent = new Intent(SeekerState.MainActivityRef, typeof(MessagesActivity));
                SeekerState.MainActivityRef.StartActivityForResult(intent, 142);
            };
            menuSetUpSharing.Click += (s, e) => {
                Intent intent = new Intent(SeekerState.MainActivityRef, typeof(SettingsActivity));
                intent.PutExtra(SettingsActivity.SCROLL_TO_SHARING_SECTION_STRING, SettingsActivity.SCROLL_TO_SHARING_SECTION);
                SeekerState.MainActivityRef.StartActivityForResult(intent, 140);
            };
            menuSettings.Click += Settings_Click;
            menuLogout.Click += LogoutClick;
        }

        // --- View-flipping methods ---

        public void ShowLoginForm(bool prefill)
        {
            var action = new Action(() =>
            {
                if (prefill && !string.IsNullOrEmpty(PreferencesState.Username))
                {
                    usernameTextEdit.Text = PreferencesState.Username;
                    passwordTextEdit.Text = PreferencesState.Password;
                }
                else
                {
                    usernameTextEdit.Text = string.Empty;
                    passwordTextEdit.Text = string.Empty;
                }
                viewFlipper.DisplayedChild = ChildLoginForm;
                Seeker.Player.HomeMusicSection.UpdateAccount(rootView);
            });
            if (SeekerApplication.OnUIThread())
            {
                action();
            }
            else
            {
                SeekerState.MainActivityRef.RunOnUiThread(action);
            }
        }
        public void ShowLoggedIn()
        {
            var action = new Action(() =>
            {
                welcomeTextView.Text = PreferencesState.Username;
                UpdateConnectionStatus(SeekerState.SoulseekClient.State);

                UpdateUnreadBadge();
                UpdateServerMessage();

                if (UploadDirectoryManager.UploadDirectories == null || UploadDirectoryManager.UploadDirectories.Count == 0)
                {
                    menuSetUpSharing.Visibility = ViewStates.Visible;
                }
                else
                {
                    menuSetUpSharing.Visibility = ViewStates.Gone;
                }

                viewFlipper.DisplayedChild = ChildLoggedIn;
                Seeker.Player.HomeMusicSection.UpdateAccount(rootView);
            });
            if (SeekerApplication.OnUIThread())
            {
                action();
            }
            else
            {
                SeekerState.MainActivityRef.RunOnUiThread(action);
            }
        }

        public void UpdateConnectionStatus(SoulseekClientStates state)
        {
            if (this.Context != null)
            {
                try
                {
                    int textResId;
                    int dotColorResId;
                    int textColorResId;
                    int chipBgColorResId;

                    if (state.HasFlag(SoulseekClientStates.LoggedIn))
                    {
                        textResId = Resource.String.status_connected;
                        dotColorResId = Resource.Color.statusConnectedDot;
                        textColorResId = Resource.Color.statusConnectedText;
                        chipBgColorResId = Resource.Color.statusConnectedChipBg;
                    }
                    else if (state.HasFlag(SoulseekClientStates.Connecting) || state.HasFlag(SoulseekClientStates.LoggingIn)
                        || SessionService.InFlightLoginOrigin != null) // i.e. resolving DNS or any pre-connect step
                    {
                        textResId = Resource.String.status_connecting;
                        dotColorResId = Resource.Color.statusConnectingDot;
                        textColorResId = Resource.Color.statusConnectingText;
                        chipBgColorResId = Resource.Color.statusConnectingChipBg;
                    }
                    else
                    {
                        if (state.HasFlag(SoulseekClientStates.Disconnecting))
                        {
                            textResId = Resource.String.status_disconnecting;
                        }
                        else
                        {
                            textResId = Resource.String.status_disconnected;
                        }
                        dotColorResId = Resource.Color.statusDisconnectedDot;
                        textColorResId = Resource.Color.statusDisconnectedText;
                        chipBgColorResId = Resource.Color.statusDisconnectedChipBg;
                    }

                    var resources = this.Context.Resources;
                    int dotColor = resources.GetColor(dotColorResId, this.Context.Theme);
                    int textColor = resources.GetColor(textColorResId, this.Context.Theme);
                    int chipBgColor = resources.GetColor(chipBgColorResId, this.Context.Theme);

                    var dotDrawable = (GradientDrawable)connectionStatusDot.Background;
                    dotDrawable.SetColor(dotColor);
                    connectionStatusText.Text = SeekerApplication.GetString(textResId);
                    connectionStatusText.SetTextColor(new Android.Graphics.Color(textColor));
                    var chipBgDrawable = (GradientDrawable)connectionStatusChip.Background;
                    chipBgDrawable.SetColor(chipBgColor);
                } catch (Exception e)
                {
                    Logger.FirebaseError("Update connection status ", e);
                }

            }
        }

        private void UpdateUnreadBadge()
        {
            int unreadCount = MessageController.GetTotalUnreadCount();
            if (unreadCount > 0)
            {
                messagesUnreadBadge.Text = string.Format(
                    SeekerApplication.GetString(Resource.String.unread_count), unreadCount);
                messagesUnreadBadge.Visibility = ViewStates.Visible;
            }
            else
            {
                messagesUnreadBadge.Visibility = ViewStates.Gone;
            }
        }

        private void OnMessageReceivedUpdateBadge(object sender, Message msg)
        {
            this.Activity?.RunOnUiThread(() => UpdateUnreadBadge());
        }

        private void OnMarkAsReadUpdateBadge(object sender, string username)
        {
            this.Activity?.RunOnUiThread(() => UpdateUnreadBadge());
        }

        public void ShowMustSelectDirectoryButton(EventHandler clickHandler)
        {
            var action = new Action(() =>
            {
                if (mustSelectDirButton != null)
                {
                    mustSelectDirButton.Visibility = ViewStates.Visible;
                    mustSelectDirButton.Click += clickHandler;
                }
            });
            if (SeekerApplication.OnUIThread())
            {
                action();
            }
            else
            {
                SeekerState.MainActivityRef.RunOnUiThread(action);
            }
        }

        public void HideMustSelectDirectoryButton()
        {
            var action = new Action(() =>
            {
                if (mustSelectDirButton != null)
                {
                    mustSelectDirButton.Visibility = ViewStates.Gone;
                }
            });
            if (SeekerApplication.OnUIThread())
            {
                action();
            }
            else
            {
                SeekerState.MainActivityRef.RunOnUiThread(action);
            }
        }

        // --- Login/Logout logic ---

        private static void EnableDisableLoginButton(EditText uname, EditText passwd, Button login, bool hasError)
        {
            if (string.IsNullOrEmpty(uname.Text) || string.IsNullOrEmpty(passwd.Text) || hasError)
            {
                login.Alpha = 0.5f;
                login.Clickable = false;
            }
            else
            {
                login.Alpha = 1.0f;
                login.Clickable = true;
            }
            try
            {
                SeekerState.MainActivityRef.Window.SetSoftInputMode(SoftInput.AdjustNothing);
            }
            catch (System.Exception err)
            {
                Logger.Firebase("MainActivity_FocusChange" + err.Message);
            }
        }

        private readonly int[] All_Ascii = Enumerable.Range('\x1', 127).ToArray();

        private void UsernameTextEdit_FocusChange(object sender, View.FocusChangeEventArgs e)
        {
            bool hasError = ValidateUsername(true);
            EnableDisableLoginButton(usernameTextEdit, passwordTextEdit, loginButton, hasError);
        }

        private void UsernamePasswordTextEdit_TextChanged(object sender, Android.Text.TextChangedEventArgs e)
        {
            // if typing in username do not validate trailing spaces (bc we dont want to flash errors while typing)
            bool hasError = ValidateUsername(passwordTextEdit == sender);
            EnableDisableLoginButton(usernameTextEdit, passwordTextEdit, loginButton, hasError);
        }

        /// <summary>
        /// We dont check trailing space when text is changing (i.e. actively typing) to avoid flashing errors
        /// </summary>
        /// <param name="checkTrailingSpace"></param>
        /// <returns></returns>
        private bool ValidateUsername(bool checkTrailingSpace)
        {
            bool hasError = false;
            if (!string.IsNullOrEmpty(usernameTextEdit.Text))
            {
                var uname = usernameTextEdit.Text.ToString();
                if (uname.Length > 30)
                {
                    usernameInputLayout.Error = this.GetString(Resource.String.user_too_long);
                    hasError = true;
                }
                else if (uname.StartsWith(' ') || (checkTrailingSpace && uname.EndsWith(' ')))
                {
                    // https://nicotine-plus.org/doc/SLSKPROTOCOL.html#invalidusername
                    usernameInputLayout.Error = this.GetString(Resource.String.user_no_leading_trailing_space);
                    hasError = true;
                }
                else
                {
                    foreach (char c in uname)
                    {
                        if (!All_Ascii.Contains(c))
                        {
                            usernameInputLayout.Error = this.GetString(Resource.String.user_invalid_char);
                            hasError = true;
                            break;
                        }
                    }
                }
            }

            if (!hasError)
            {
                usernameInputLayout.Error = null;
                usernameInputLayout.ErrorEnabled = false;
            }

            return hasError;
        }

        public void Settings_Click(object sender, EventArgs e)
        {
            Intent intent = new Intent(SeekerState.MainActivityRef, typeof(SettingsActivity));
            SeekerState.MainActivityRef.StartActivityForResult(intent, 140);
        }

        private void LogoutClick(object sender, EventArgs e)
        {
            try
            {
                SeekerState.SoulseekClient.Disconnect(message: LogoutMessage);
            }
            catch
            {
            }
            PreferencesState.ClearCredentials();
            PreferencesManager.SaveCredentials();
            ShowLoginForm(prefill: false);
        }

        public void LogInClick(object sender, EventArgs e)
        {
            if (ValidateUsername(true))
            {
                EnableDisableLoginButton(usernameTextEdit, passwordTextEdit, loginButton, hasError: true);
                return;
            }

            if (string.IsNullOrEmpty(usernameTextEdit.Text) || string.IsNullOrEmpty(passwordTextEdit.Text))
            {
                SeekerApplication.Toaster.ShowToast(SeekerApplication.GetString(Resource.String.no_empty_user_pass), ToastLength.Long);
                return;
            }

            try
            {
                Android.Views.InputMethods.InputMethodManager imm = (Android.Views.InputMethods.InputMethodManager)(this.Activity).GetSystemService(Context.InputMethodService);
                imm.HideSoftInputFromWindow(usernameTextEdit.WindowToken, 0);
            }
            catch (System.Exception)
            {
            }

            PreferencesState.SetCredentials(usernameTextEdit.Text, passwordTextEdit.Text);
            SeekerState.ManualResetEvent.Reset();

            Task login = SessionService.BeginLogin(
                LoginOrigin.Interactive, usernameTextEdit.Text, passwordTextEdit.Text);
            login?.ContinueWith(MainActivity.GetPostNotifPermissionTask());

            RenderFromState();
        }
    }
}
