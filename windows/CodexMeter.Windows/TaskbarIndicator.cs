using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed class TaskbarIndicator : Window
    {
        private readonly Action showDetails, openSettings, quit;
        private readonly Action<string, double> savePosition;
        private readonly bool preview;
        private readonly bool solidPreview;
        private readonly IndicatorGesture gesture = new IndicatorGesture();
        private readonly IndicatorView view;
        private readonly Border surface;
        private TaskbarSnapshot snapshot;
        private bool probing, stopped, enabled, openQueued, popupVisible;
        private double position = -1;
        private string dock = "Auto";
        private DateTime nextProbe = DateTime.MinValue;
        public TaskbarIndicator(bool preview, Action showDetails, Action openSettings, Action quit, Action<string, double> savePosition, bool solidPreview = false)
        {
            this.preview = preview; this.showDetails = showDetails; this.openSettings = openSettings;
            this.quit = quit; this.savePosition = savePosition;
            this.solidPreview = solidPreview;
            Title = "CodexMeter 任务栏额度"; Width = 76; Height = 32;
            UseLayoutRounding = true;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            // Alpha-zero pixels in a layered HWND pass clicks to Explorer before WPF hit testing.
            AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            AutomationProperties.SetName(this, "CodexMeter 任务栏额度");
            view = new IndicatorView(); surface = view.Surface; Content = view;
            SourceInitialized += (s, e) => {
                var handle = new WindowInteropHelper(this).Handle;
                TaskbarNative.SetNoActivate(handle);
                HwndSource.FromHwnd(handle).AddHook(NoActivate);
            };
            AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnDragStart), true);
            AddHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler(OnDragMove), true);
            AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnDragEnd), true);
            surface.LostMouseCapture += (s, e) => { if (!surface.IsMouseCaptured) gesture.Cancel(); };
            MouseRightButtonUp += (s, e) => ShowMenu();
        }
        private IntPtr NoActivate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
            if (message == 0x02E0) Dispatcher.BeginInvoke(new Action(InvalidatePlacement)); // WM_DPICHANGED
            return IntPtr.Zero;
        }
        public void Render(UsageState state, Settings settings)
        {
            if (!gesture.IsPressed) { position = settings.TaskbarPosition; dock = settings.TaskbarDock; }
            view.Render(state, settings, preview, !solidPreview); Width = view.Width; ToolTip = view.ToolTip;
            Title = L.T("CodexMeter 任务栏额度"); AutomationProperties.SetName(this, Title);
            AutomationProperties.SetHelpText(this, AutomationProperties.GetHelpText(view));
            AutomationProperties.SetAutomationId(this, "IndicatorStyle." + Settings.ValidStyle(settings.IndicatorStyle));
        }
        public async void UpdatePlacement(bool enabled)
        {
            if (stopped) return;
            this.enabled = enabled;
            if (!enabled) { gesture.Cancel(); surface.ReleaseMouseCapture(); Hide(); return; }
            if (gesture.IsPressed) return;
            if (preview)
            {
                if (!IsVisible) { Left = SystemParameters.WorkArea.Right - Width - 24; Top = SystemParameters.WorkArea.Bottom - Height - 12; Show(); }
                return;
            }
            // Native bounds remain responsive even when Explorer's UI Automation provider stalls.
            var native = TaskbarNative.Read(false);
            if (snapshot == null || snapshot.Handle != native.Handle || snapshot.Available != native.Available || !native.Available ||
                snapshot.Bounds.Left != native.Bounds.Left || snapshot.Bounds.Top != native.Bounds.Top ||
                snapshot.Bounds.Right != native.Bounds.Right || snapshot.Bounds.Bottom != native.Bounds.Bottom || snapshot.Dpi != native.Dpi)
                snapshot = native;
            Place();
            if (DateTime.UtcNow >= nextProbe && !probing)
            {
                probing = true;
                try
                {
                    var measured = await Task.Run(() => TaskbarNative.Read());
                    var current = TaskbarNative.Read(false);
                    if (measured.Handle == current.Handle && measured.Available == current.Available &&
                        measured.Bounds.Left == current.Bounds.Left && measured.Bounds.Top == current.Bounds.Top &&
                        measured.Bounds.Right == current.Bounds.Right && measured.Bounds.Bottom == current.Bounds.Bottom && measured.Dpi == current.Dpi)
                        snapshot = measured;
                }
                catch (Exception ex) { if (ex is OutOfMemoryException) throw; }
                finally { probing = false; nextProbe = DateTime.UtcNow.AddSeconds(3); }
            }
            if (stopped || !this.enabled) return;
            Place();
        }
        public void InvalidatePlacement()
        {
            if (stopped) return;
            snapshot = null; nextProbe = DateTime.MinValue;
            UpdatePlacement(enabled);
        }
        private void Place(bool duringDrag = false)
        {
            if (gesture.IsPressed && !duringDrag) return;
            if (snapshot == null || !snapshot.Available) { Hide(); return; }
            var bounds = snapshot.Bounds;
            if (!popupVisible && !openQueued && TaskbarNative.IsFullscreenOutsideTaskbar(bounds)) { Hide(); return; }
            double scale = snapshot.Dpi / 96.0;
            int width = (int)Math.Round(Width * scale), height = Math.Min(bounds.Height - 4, (int)Math.Round(32 * scale));
            bool nearStart = dock == "StartLeft" || dock == "StartRight";
            double? left = null;
            if (snapshot.ControlsKnown)
            {
                if (nearStart && snapshot.StartButton.HasValue)
                    left = TaskbarLayout.FindStartSlot(bounds.Left, bounds.Right, width, snapshot.Occupied,
                        snapshot.StartButton.Value, dock == "StartLeft", 4 * scale);
                else if (!nearStart)
                    left = TaskbarLayout.FindSlot(bounds.Left, bounds.Right, width, snapshot.Occupied, position);
            }
            // Keep a Start-anchored fallback close to Start instead of jumping across the taskbar.
            int fallback = nearStart && snapshot.StartButton.HasValue ?
                (int)(dock == "StartLeft" ? snapshot.StartButton.Value.Start - width - 4 * scale :
                    snapshot.StartButton.Value.End + 4 * scale) :
                position >= 0 ? (int)(bounds.Left + position * bounds.Width - width / 2.0) :
                    bounds.Right - width - (int)(240 * scale);
            int x = left.HasValue ? (int)left.Value : Math.Max(bounds.Left + 2, Math.Min(bounds.Right - width - 2, fallback));
            int y = left.HasValue ? bounds.Top + (bounds.Height - height) / 2 : bounds.Top - height - 4;
            if (!IsVisible) Show();
            TaskbarNative.Place(new WindowInteropHelper(this).Handle, Math.Max(bounds.Left, x), y, width, height);
        }
        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            var cursor = PointToScreen(e.GetPosition(this));
            var source = PresentationSource.FromVisual(this);
            double scale = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformToDevice.M11 : 1;
            gesture.Begin(cursor.X, cursor.Y, SystemParameters.MinimumHorizontalDragDistance * scale, SystemParameters.MinimumVerticalDragDistance * scale);
            if (!surface.CaptureMouse()) gesture.Cancel();
            e.Handled = true;
        }
        private void OnDragMove(object sender, MouseEventArgs e)
        {
            if (!gesture.IsPressed) return;
            if (e.LeftButton != MouseButtonState.Pressed) { gesture.Cancel(); surface.ReleaseMouseCapture(); return; }
            var cursor = PointToScreen(e.GetPosition(this)); gesture.Move(cursor.X, cursor.Y);
            if (!gesture.IsDragging || snapshot == null || !snapshot.Available) return;
            var bounds = snapshot.Bounds;
            position = Math.Max(0, Math.Min(1, (cursor.X - bounds.Left) / (double)Math.Max(1, bounds.Width)));
            dock = "Custom";
            if (snapshot.StartButton.HasValue)
            {
                var start = snapshot.StartButton.Value;
                double center = (start.Start + start.End) / 2;
                if (Math.Abs(cursor.X - center) <= 128 * snapshot.Dpi / 96.0)
                    dock = cursor.X < center ? "StartLeft" : "StartRight";
            }
            Place(true); e.Handled = true;
        }
        private void OnDragEnd(object sender, MouseButtonEventArgs e)
        {
            if (!gesture.IsPressed) return;
            var cursor = PointToScreen(e.GetPosition(this)); bool wasDragging = gesture.IsDragging;
            bool click = gesture.End(cursor.X, cursor.Y);
            surface.ReleaseMouseCapture();
            if (click) RequestDetails();
            else if (wasDragging && snapshot != null && snapshot.Available) savePosition(dock, position);
            e.Handled = true;
        }
        internal void RequestDetails()
        {
            if (stopped || openQueued) return;
            gesture.Cancel(); surface.ReleaseMouseCapture(); openQueued = true;
            // Complete mouse-up/capture processing before showing an activating popup.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => {
                if (stopped) { openQueued = false; return; }
                try { showDetails(); }
                finally { openQueued = false; }
            }));
        }
        public void SetPopupVisible(bool value) { popupVisible = value; }
        public System.Drawing.Point PopupAnchor
        {
            get
            {
                NativeRect rect;
                return TaskbarNative.TryGetBounds(new WindowInteropHelper(this).Handle, out rect) ?
                    new System.Drawing.Point(rect.Left + rect.Width / 2, rect.Top) : System.Windows.Forms.Cursor.Position;
            }
        }
        protected override AutomationPeer OnCreateAutomationPeer() { return new IndicatorPeer(this); }
        private sealed class IndicatorPeer : WindowAutomationPeer, IInvokeProvider
        {
            private readonly TaskbarIndicator indicator;
            public IndicatorPeer(TaskbarIndicator owner) : base(owner) { indicator = owner; }
            public override object GetPattern(PatternInterface pattern)
            { return pattern == PatternInterface.Invoke ? this : base.GetPattern(pattern); }
            public void Invoke() { indicator.Dispatcher.BeginInvoke(new Action(indicator.RequestDetails)); }
        }
        private void ShowMenu()
        {
            var menu = new ContextMenu();
            var detailsItem = new MenuItem { Header = "查看额度" }; detailsItem.Click += (s, e) => RequestDetails(); menu.Items.Add(detailsItem);
            AddDock(menu, "紧靠 Windows 开始按钮左侧", "StartLeft");
            AddDock(menu, "紧靠 Windows 开始按钮右侧", "StartRight");
            AddDock(menu, "自动靠右", "Auto");
            var reset = new MenuItem { Header = "重置任务栏位置" };
            reset.Click += (s, e) => ChangeDock("Auto"); menu.Items.Add(reset);
            var settingsItem = new MenuItem { Header = "设置" }; settingsItem.Click += (s, e) => openSettings(); menu.Items.Add(settingsItem);
            var exit = new MenuItem { Header = "退出" }; exit.Click += (s, e) => quit(); menu.Items.Add(exit);
            L.Apply(menu); menu.IsOpen = true;
        }
        private void AddDock(ContextMenu menu, string label, string value)
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = dock == value };
            item.Click += (s, e) => ChangeDock(value); menu.Items.Add(item);
        }
        private void ChangeDock(string value)
        {
            dock = value; position = -1; savePosition(dock, position); Place();
        }
        public void Stop() { if (stopped) return; stopped = true; Close(); }
    }
}
