using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexMeter.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CodexMeter.Windows
{
    internal sealed partial class TrayApplication : IDisposable
    {
        private readonly Application app;
        private readonly string[] args;
        private readonly bool preview;
        private readonly bool dismissOnDeactivate;
        private readonly SettingsStore store = SettingsStore.Default();
        private Settings configuration;
        private readonly UsageService service;
        private Forms.NotifyIcon tray;
        private DetailWindow details;
        private TaskbarIndicator indicator;
        private SettingsWindow settings;
        private HistoryStore history;
        private HistoryWindow historyWindow;
        private AppearanceWindow appearancePreview;
        private DispatcherTimer timer;
        private HwndSource messageWindow;
        private readonly uint showMessage = Program.RegisterWindowMessage(Program.ShowMessageName);
        private readonly uint taskbarCreated = Program.RegisterWindowMessage("TaskbarCreated");
        private Icon icon;
        private string iconKey;
        private UsageState previewState;
        private string settingsWarning;
        private bool quitting, disposed;
        private int ticks;
        private long presentation;
        private readonly QuotaNotifications notificationRules = new QuotaNotifications();
        private readonly Queue<QuotaNotice> pendingNotices = new Queue<QuotaNotice>();
        private string noticeSession;
        private DateTimeOffset nextNotice;

        public TrayApplication(Application app, string[] args)
        {
            this.app = app; this.args = args;
            preview = args.Contains("--preview") || args.Contains("--smoke");
            dismissOnDeactivate = !preview || args.Contains("--interaction-check");
            configuration = preview ? new Settings() : store.Load(out settingsWarning);
            if (args.Contains("--dark")) configuration.Theme = "Dark";
            if (preview && args.Contains("--english")) configuration.Language = "en";
            if (preview && args.Contains("--traditional")) configuration.Language = "zh-TW";
            if (preview)
                foreach (var style in new[] { "Rings", "Horizontal", "Stacked", "Percentage", "Progress" })
                    if (args.Contains("--style=" + style)) configuration.IndicatorStyle = style;
            L.Language = configuration.Language;
            service = new UsageService(new CliLocator(), new RpcFactory());
            ConfigureHistory();
            service.Configure(configuration.CliPath);
            service.Changed += OnChanged;
            InitializeFeatures();
        }
        public void Start()
        {
            details = new DetailWindow(Refresh, ShowSettings, ShowHistory, Quit, WeeklySamples);
            details.Closing += (s, e) => { if (!quitting) { e.Cancel = true; details.Hide(); } };
            details.Deactivated += (s, e) => {
                long epoch = presentation;
                app.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
                    if (dismissOnDeactivate && !quitting && epoch == presentation && !details.IsActive) details.Hide();
                }));
            };
            details.IsVisibleChanged += (s, e) => { if (indicator != null) indicator.SetPopupVisible(details.IsVisible); };
            new WindowInteropHelper(details).EnsureHandle();
            var source = HwndSource.FromHwnd(new WindowInteropHelper(details).Handle);
            source.AddHook(WindowMessage);
            // A hidden top-level HWND receives TaskbarCreated and second-instance broadcasts.
            messageWindow = new HwndSource(new HwndSourceParameters("CodexMeter.MessageWindow") {
                Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000)
            });
            messageWindow.AddHook(WindowMessage);
            if (preview)
            {
                previewState = new UsageState {
                    Plan = "ChatGPT · Plus", UpdatedAt = DateTimeOffset.UtcNow,
                    Credits = new CreditBalance { Balance = "25.50", HasCredits = true, Unlimited = false },
                    ResetCredits = new ResetCredits { AvailableCount = 2, DetailsAvailable = true, Credits = new List<ResetCredit> {
                        new ResetCredit { Id = "demo-reset-1", Status = "available", ResetType = "codexRateLimits", Title = "额度重置机会",
                            GrantedAt = DateTimeOffset.UtcNow.AddDays(-12), ExpiresAt = DateTimeOffset.UtcNow.AddDays(18) },
                        new ResetCredit { Id = "demo-reset-2", Status = "available", ResetType = "codexRateLimits", Title = "额度重置机会",
                            GrantedAt = DateTimeOffset.UtcNow.AddDays(-28), ExpiresAt = DateTimeOffset.UtcNow.AddDays(2) } } },
                    Tokens = new TokenUsage { Available = true, DailyAvailable = true, LifetimeTokens = 20000000,
                        PeakDailyTokens = 60000, CurrentStreakDays = 8, LongestStreakDays = 14,
                        LongestRunningTurnSeconds = 540, Daily = HistoryWindow.PreviewTokens(DateTimeOffset.UtcNow) },
                    TokensUpdatedAt = DateTimeOffset.UtcNow,
                    Windows = new List<QuotaWindow> {
                        new QuotaWindow { Id = "codex:300", Bucket = "codex", UsedPercent = 26, DurationMinutes = 300,
                            ResetsAt = DateTimeOffset.UtcNow.AddHours(3.2) },
                        new QuotaWindow { Id = "codex:10080", Bucket = "codex", UsedPercent = 38, DurationMinutes = 10080,
                            ResetsAt = DateTimeOffset.UtcNow.AddDays(4.8) }
                    }
                };
            }
            else
            {
                var menu = new Forms.ContextMenuStrip();
                menu.Items.Add("查看额度", null, (s, e) => ShowDetails());
                menu.Items.Add("立即刷新", null, (s, e) => Refresh());
                menu.Items.Add("历史记录", null, (s, e) => ShowHistory());
                menu.Items.Add("设置", null, (s, e) => ShowSettings());
                menu.Items.Add("关于与更新", null, (s, e) => ShowAbout());
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add("退出", null, (s, e) => Quit());
                tray = new Forms.NotifyIcon { ContextMenuStrip = menu, Text = "CodexMeter · 正在连接" };
                tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) ShowDetails(); };
                tray.BalloonTipClicked += (s, e) => ShowDetails();
                UpdateTray(service.State);
                tray.Visible = true;
                SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
                SystemEvents.SessionSwitch += OnSessionSwitch;
            }
            indicator = new TaskbarIndicator(preview, ShowDetailsFromIndicator, ShowSettings, Quit, SaveTaskbarPosition,
                preview && args.Contains("--smoke"));
            new WindowInteropHelper(indicator).EnsureHandle();
            Render();
            indicator.UpdatePlacement(configuration.TaskbarIndicator);
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += OnTick; timer.Start();
            if (preview || args.Contains("--show")) ShowDetails();
            if (preview && args.Contains("--settings")) ShowSettings();
            if (preview && args.Contains("--history")) ShowHistory();
            if (preview && args.Contains("--remote")) ShowRemote();
            if (preview && args.Contains("--data")) ShowData();
            if (preview && args.Contains("--about")) ShowAbout();
            if (preview && args.Contains("--appearance"))
            {
                appearancePreview = new AppearanceWindow(configuration, value => { configuration.Appearance = value.Copy(); Render(); });
                appearancePreview.Closed += (s, e) => appearancePreview = null;
                appearancePreview.Show();
            }
            if (!preview) Refresh();
            if (args.Contains("--smoke"))
            {
                var capture = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                capture.Tick += (s, e) => {
                    capture.Stop();
                    try
                    {
                        var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
                        Directory.CreateDirectory(directory);
                        Window target = aboutWindow != null ? (Window)aboutWindow : remoteWindow != null ? (Window)remoteWindow : dataWindow != null ? (Window)dataWindow : appearancePreview != null ? (Window)appearancePreview : historyWindow != null ? (Window)historyWindow : settings != null ? (Window)settings : details;
                        target.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)target.ActualWidth, (int)target.ActualHeight, 96, 96,
                            System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(target);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var stream = File.Create(Path.Combine(directory, (aboutWindow != null ? "about-" : remoteWindow != null ? "remote-" : dataWindow != null ? "data-" : appearancePreview != null ? "appearance-" : historyWindow != null ? "history-" : settings != null ? "settings-" : "") + (configuration.Theme == "Dark" ? "dark.png" : "light.png"))))
                            encoder.Save(stream);
                        if (indicator != null && indicator.IsVisible)
                        {
                            indicator.UpdateLayout();
                            var meter = new RenderTargetBitmap((int)indicator.ActualWidth, (int)indicator.ActualHeight, 96, 96,
                                System.Windows.Media.PixelFormats.Pbgra32);
                            meter.Render(indicator);
                            var meterEncoder = new PngBitmapEncoder(); meterEncoder.Frames.Add(BitmapFrame.Create(meter));
                            using (var stream = File.Create(Path.Combine(directory, configuration.Theme == "Dark" ? "indicator-dark.png" : "indicator-light.png")))
                                meterEncoder.Save(stream);
                        }
                    }
                    finally { Quit(); }
                };
                capture.Start();
            }
        }
        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((uint)message == showMessage) { app.Dispatcher.BeginInvoke(new Action(ShowDetails)); handled = true; }
            if ((uint)message == taskbarCreated && tray != null)
            {
                tray.Visible = false; tray.Visible = true;
                if (indicator != null) indicator.InvalidatePlacement();
            }
            return IntPtr.Zero;
        }
        private async void Refresh()
        {
            if (preview || quitting || dataBusy) return;
            try { await service.RefreshAsync(true); }
            catch (ObjectDisposedException) { }
        }
        private async void OnTick(object sender, EventArgs e)
        {
            if (quitting) return;
            if (!preview && !dataBusy)
            {
                service.SetVisible(details.IsVisible);
                try { await service.RefreshAsync(false); }
                catch (ObjectDisposedException) { if (!quitting) throw; }
            }
            if (quitting) return;
            bool idle = !dataBusy && !details.IsVisible && settings == null && historyWindow == null && dataWindow == null && remoteWindow == null && aboutWindow == null && !service.State.IsLoading;
            updater.Automatic(idle); updater.InstallIfIdle(idle);
            if (indicator != null) indicator.UpdatePlacement(configuration.TaskbarIndicator);
            DeliverNotice();
            if (++ticks % 15 == 0) Render();
        }
        private void OnChanged()
        {
            if (quitting || app.Dispatcher.HasShutdownStarted) return;
            app.Dispatcher.BeginInvoke(new Action(Render));
        }
        private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) { OnChanged(); }
        private void OnDisplayChanged(object sender, EventArgs e)
        {
            if (quitting || app.Dispatcher.HasShutdownStarted) return;
            app.Dispatcher.BeginInvoke(new Action(() => { if (!quitting && indicator != null) { indicator.InvalidatePlacement(); if (details.IsVisible) details.PositionNearCursor(); } }));
        }
        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        { if (!quitting && !app.Dispatcher.HasShutdownStarted && e.Reason == SessionSwitchReason.SessionUnlock) app.Dispatcher.BeginInvoke(new Action(() => { if (!quitting) { indicator.InvalidatePlacement(); Refresh(); } })); }
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        { if (!quitting && !app.Dispatcher.HasShutdownStarted && e.Mode == PowerModes.Resume) app.Dispatcher.BeginInvoke(new Action(() => { if (!quitting) { indicator.InvalidatePlacement(); Refresh(); } })); }
        private void Render()
        {
            if (quitting || details == null) return;
            var state = preview ? previewState : service.State;
            if (!preview)
            {
                EvaluateRemote(state);
                if (noticeSession != state.SessionId) { pendingNotices.Clear(); noticeSession = state.SessionId; }
                foreach (var notice in notificationRules.Evaluate(state, configuration, DateTimeOffset.UtcNow))
                    if (pendingNotices.Count < 32) pendingNotices.Enqueue(notice);
            }
            if (settingsWarning != null && state.Error == null) state.Error = settingsWarning;
            if (details.IsVisible || !details.IsLoaded) details.Render(state, configuration, preview);
            if (tray != null) UpdateTray(state);
            if (indicator != null) { indicator.Render(state, configuration); indicator.UpdatePlacement(configuration.TaskbarIndicator); }
            if (!dataBusy && historyWindow != null && historyWindow.IsVisible) historyWindow.RefreshData(false);
        }
        private void SaveTaskbarPosition(string dock, double position)
        {
            configuration.TaskbarPosition = position;
            configuration.TaskbarDock = dock;
            if (!preview)
            {
                try { store.Save(configuration); }
                catch (IOException) { settingsWarning = "位置已临时调整，但设置未能保存。"; }
                catch (UnauthorizedAccessException) { settingsWarning = "位置已临时调整，但没有保存设置的权限。"; }
            }
        }
        private IList<QuotaSample> WeeklySamples(UsageState state)
        {
            var weekly = state.Windows.FirstOrDefault(w => w.Id == "codex:10080");
            var interval = HistoryCalendar.Cycle(weekly, DateTimeOffset.UtcNow);
            if (preview) return HistoryWindow.PreviewQuota(previewState.UpdatedAt.Value).Where(p => p.WindowId == "codex:10080" && p.At >= interval.From && p.At <= interval.To).ToList();
            if (dataBusy || history == null || state.AccountKey == null) return new List<QuotaSample>();
            try { return history.ReadQuota(state.AccountKey, interval.From, interval.To, "codex:10080"); }
            catch (IOException) { state.HistoryError = "历史记录无法读取，实时额度仍可使用。"; }
            catch (UnauthorizedAccessException) { state.HistoryError = "没有读取历史记录的权限。"; }
            return new List<QuotaSample>();
        }
        private void ShowDetails()
        { ShowDetailsAt(null); }
        private void ShowDetailsFromIndicator()
        { ShowDetailsAt(indicator.PopupAnchor); }
        private void ShowDetailsAt(System.Drawing.Point? anchor)
        {
            if (quitting) return;
            presentation++;
            var state = preview ? previewState : service.State;
            if (settingsWarning != null && state.Error == null) state.Error = settingsWarning;
            details.Render(state, configuration, preview);
            details.PositionNearCursor(anchor); details.Show(); details.Activate();
            if (indicator != null) indicator.SetPopupVisible(true);
            if (!preview) service.SetVisible(true);
        }
        private void ShowSettings()
        {
            if (dataBusy) return;
            if (!preview) details.Hide();
            if (settings != null) { settings.Activate(); return; }
            var current = configuration.Copy();
            StartupRegistration startup = null;
            string startupDescription = "预览模式不会修改登录启动或发送系统通知。";
            bool startupAvailable = preview;
            if (!preview)
            {
                try
                {
                    startup = new StartupRegistration(new RegistryStartupValueStore(), System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                    var status = startup.Read(); current.LaunchAtLogin = status.Registered;
                    startupDescription = status.Description; startupAvailable = true;
                }
                catch (UnauthorizedAccessException) { startupDescription = "无法读取 Windows 登录启动状态。"; }
                catch (System.Security.SecurityException) { startupDescription = "无法读取 Windows 登录启动状态。"; }
                catch (IOException) { startupDescription = "无法读取 Windows 登录启动状态。"; }
            }
            settings = new SettingsWindow(current, (preview ? previewState : service.State).Windows, next => {
                CopyUpdateSettings(configuration, next);
                if (!preview)
                {
                    // Apply only an explicit checkbox change; never re-enable a system-disabled entry on launch.
                    bool wasRegistered = current.LaunchAtLogin;
                    bool changed = startupAvailable && next.LaunchAtLogin != wasRegistered;
                    string previousCommand = changed ? startup.CaptureCommand() : null;
                    try { if (changed) startup.Set(next.LaunchAtLogin); store.Save(next); }
                    catch { if (changed) startup.RestoreCommand(previousCommand); throw; }
                }
                configuration = next; settingsWarning = null;
                L.Language = next.Language;
                ConfigureHistory();
                if (historyWindow != null) historyWindow.Close();
                service.Configure(next.CliPath); Render();
                if (!preview) Refresh();
            }, TestNotification, preview, startupDescription, startupAvailable, ShowRemote, ShowData, ShowAbout);
            settings.Closed += (s, e) => { settings = null; Render(); };
            settings.Show(); settings.Activate();
        }
        private void ConfigureHistory()
        {
            if (preview) return;
            try
            {
                var historyPath = Path.Combine(Path.GetDirectoryName(store.FilePath), "history.sqlite3");
                if (history == null && (configuration.HistoryEnabled || File.Exists(historyPath))) history = new HistoryStore(historyPath);
                service.ConfigureHistory(history, configuration.HistoryRetentionDays, configuration.HistoryEnabled);
            }
            catch (IOException) { settingsWarning = "本地历史数据库无法打开，实时额度仍可使用。"; }
            catch (UnauthorizedAccessException) { settingsWarning = "没有访问本地历史数据库的权限，实时额度仍可使用。"; }
            catch (FormatException) { settingsWarning = "历史账号标识无法读取，实时额度仍可使用。"; }
            catch (DllNotFoundException) { settingsWarning = "系统 SQLite 不可用，实时额度仍可使用。"; }
            catch (EntryPointNotFoundException) { settingsWarning = "系统 SQLite 版本不受支持，实时额度仍可使用。"; }
        }
        private void ShowHistory()
        {
            if (quitting || dataBusy) return;
            if (!preview) details.Hide();
            if (historyWindow != null) { historyWindow.Activate(); return; }
            historyWindow = new HistoryWindow(history, preview, () => preview ? previewState : service.State, () => configuration);
            historyWindow.Closed += (s, e) => { historyWindow = null; };
            historyWindow.Show(); historyWindow.Activate();
        }
        private void UpdateTray(UsageState state)
        {
            foreach (Forms.ToolStripItem item in tray.ContextMenuStrip.Items)
            {
                if (item.Tag == null) item.Tag = item.Text;
                item.Text = L.T((string)item.Tag);
            }
            var selected = QuotaParser.Select(state.Windows, configuration.SelectedWindow);
            var status = selected == null ? Attention.Unknown : selected.State(DateTimeOffset.UtcNow);
            bool dark = Palette.For(configuration.Theme).Dark;
            string key = status + "|" + state.IsStale + "|" + dark;
            if (key != iconKey)
            {
                var replacement = CreateIcon(status, state.IsStale, dark);
                tray.Icon = replacement;
                if (icon != null) icon.Dispose();
                icon = replacement; iconKey = key;
            }
            string text = selected == null ? "CodexMeter · " + (state.Error ?? state.Plan) :
                string.Format("CodexMeter · {0} 剩余 {1:0.#}%{2}\n更新 {3}", selected.Title, selected.RemainingPercent,
                    state.IsStale ? "（已过期）" : "", state.UpdatedAt.HasValue ? state.UpdatedAt.Value.ToLocalTime().ToString("HH:mm:ss") : "--");
            text = L.T(text); tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }
        private void DeliverNotice()
        {
            if (preview || quitting || tray == null || DateTimeOffset.UtcNow < nextNotice) return;
            var state = service.State; var now = DateTimeOffset.UtcNow;
            if (state.IsLoading) return;
            while (pendingNotices.Count > 0)
            {
                var notice = pendingNotices.Dequeue();
                if (!QuotaNotifications.StillApplies(notice, state, configuration, now)) continue;
                string title = notice.Kind == "Low" ? "额度不足提醒" : "消耗偏快提醒";
                string body = L.T(notice.WindowTitle) + " · " + L.T("剩余额度 ") + notice.Remaining.ToString("0.#") + "%";
                body = body.Replace('\r', ' ').Replace('\n', ' '); if (body.Length > 220) body = body.Substring(0, 219) + "…";
                tray.ShowBalloonTip(5000, L.T(title), body, Forms.ToolTipIcon.Warning);
                nextNotice = now.AddSeconds(8); break;
            }
        }
        private void TestNotification()
        {
            if (preview || quitting || tray == null) return;
            tray.ShowBalloonTip(5000, "CodexMeter", L.T("这是一条测试通知；实际通知显示由 Windows 通知设置控制。"), Forms.ToolTipIcon.Info);
        }
        private static Icon CreateIcon(Attention state, bool stale, bool dark)
        {
            using (var image = new Bitmap(64, 64))
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.Clear(Color.Transparent);
                Color color = stale || state == Attention.Unknown ? Color.FromArgb(143, 149, 160) :
                    state == Attention.Critical ? Color.FromArgb(218, 84, 84) :
                    state == Attention.Warning ? Color.FromArgb(222, 168, 64) :
                    dark ? Color.FromArgb(137, 201, 172) : Color.FromArgb(53, 129, 99);
                using (var track = new Pen(Color.FromArgb(100, color), 8)) graphics.DrawEllipse(track, 8, 8, 48, 48);
                using (var ring = new Pen(color, 8)) graphics.DrawArc(ring, 8, 8, 48, 48, -90, state == Attention.Unknown ? 120 : 285);
                using (var brush = new SolidBrush(color))
                using (var font = new Font("Segoe UI", 23, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    graphics.DrawString(stale ? "!" : state == Attention.Critical ? "!" : state == Attention.Warning ? "↑" : state == Attention.Unknown ? "?" : "C", font, brush, new RectangleF(0, 0, 64, 64), format);
                    format.Dispose();
                }
                var handle = image.GetHicon();
                try { using (var original = Icon.FromHandle(handle)) return (Icon)original.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        public void Quit()
        {
            if (quitting || dataBusy) return;
            quitting = true;
            CloseFeatures();
            if (timer != null) timer.Stop();
            if (tray != null) tray.Visible = false;
            service.Dispose();
            if (settings != null) settings.Close();
            if (historyWindow != null) historyWindow.Close();
            if (appearancePreview != null) appearancePreview.Close();
            if (indicator != null) indicator.Stop();
            details.Close(); app.Shutdown();
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; quitting = true;
            remoteEpoch.Cancel(); updater.Dispose(); remoteDelivery.Dispose();
            if (timer != null) timer.Stop();
            SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            service.Changed -= OnChanged; service.Dispose();
            if (history != null) history.Dispose();
            if (indicator != null) indicator.Stop();
            if (tray != null) { tray.Visible = false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
            if (icon != null) icon.Dispose();
            if (messageWindow != null) messageWindow.Dispose();
        }
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);
    }
}
