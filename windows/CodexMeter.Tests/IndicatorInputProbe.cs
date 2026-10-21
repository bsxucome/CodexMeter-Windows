#if INDICATOR_INPUT_TEST
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMeter.Core;

namespace CodexMeter.Tests
{
    internal static class IndicatorInputProbe
    {
        private static int opened;
        [STAThread]
        private static int Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Window indicator = null, underneath = null;
            try
            {
                var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
                if (args.Length > 1 && args[1] == "--shell-probe")
                {
                    var native = assembly.GetType("CodexMeter.Windows.TaskbarNative", true).GetMethod("Read");
                    var snapshot = native.Invoke(null, new object[] { false }); var snapshotType = snapshot.GetType();
                    bool available = (bool)snapshotType.GetField("Available").GetValue(snapshot);
                    if (!available) { Console.WriteLine("UNVERIFIED: Explorer taskbar is not visible on this execution desktop."); return 0; }
                    var task = Task.Run(() => native.Invoke(null, new object[] { true }));
                    if (!task.Wait(4000)) { Console.WriteLine("UNVERIFIED: native taskbar bounds available; Explorer UI Automation did not finish within four seconds."); return 0; }
                    snapshot = task.Result;
                    Console.WriteLine("SHELL: native taskbar available, DPI=" + snapshotType.GetField("Dpi").GetValue(snapshot) +
                        ", controls=" + snapshotType.GetField("ControlsKnown").GetValue(snapshot) + ", Start=" + (snapshotType.GetField("StartButton").GetValue(snapshot) != null));
                    Console.WriteLine("UNVERIFIED: read-only shell geometry does not exercise physical clicks, fullscreen, auto-hide or mixed DPI."); return 0;
                }
                var type = assembly.GetType("CodexMeter.Windows.TaskbarIndicator", true);
                var constructor = type.GetConstructors()[0];
                // Old releases have five parameters; the fixed implementation adds an optional preview flag.
                object[] values = constructor.GetParameters().Length == 6 ? new object[] {
                    false, new Action(() => opened++), new Action(() => { }), new Action(() => { }), new Action<string, double>((s, p) => { }), false
                } : new object[] {
                    false, new Action(() => opened++), new Action(() => { }), new Action(() => { }), new Action<string, double>((s, p) => { })
                };
                indicator = (Window)constructor.Invoke(values);
                underneath = new Window { Width = 400, Height = 110, Left = 300, Top = 250, WindowStyle = WindowStyle.None,
                    ShowActivated = false, ShowInTaskbar = false, Topmost = true, Background = Brushes.LightSteelBlue };
                underneath.Show();
                var state = new UsageState { Plan = "Interaction test", Windows = new List<QuotaWindow> {
                    new QuotaWindow { Id = "codex:10080", Bucket = "codex", DurationMinutes = 10080, UsedPercent = 38,
                        ResetsAt = DateTimeOffset.UtcNow.AddDays(4) } } };
                var settings = new Settings();
                type.GetMethod("Render").Invoke(indicator, new object[] { state, settings });
                indicator.Left = 330; indicator.Top = 280; indicator.Show(); Pump(250);
                IntPtr handle = new WindowInteropHelper(indicator).Handle;
                Rect rect; GetWindowRect(handle, out rect);
                double scale = rect.Height / 32.0;
                int[,] points = { { 12, 16 }, { 1, 1 }, { 24, 2 }, { 28, 27 } };
                int misses = 0;
                for (int i = 0; i < points.GetLength(0); i++)
                {
                    var point = new NativePoint { X = rect.Left + (int)(points[i, 0] * scale), Y = rect.Top + (int)(points[i, 1] * scale) };
                    if (WindowFromPoint(point) != handle) misses++;
                }
                bool baseline = args.Length > 1 && args[1] == "--expect-holes";
                if (baseline)
                {
                    if (misses == 0) throw new Exception("Baseline did not reproduce transparent hit-test holes.");
                    Console.WriteLine("PASS old release reproduces click-through holes: " + misses); return 0;
                }
                if (misses != 0) throw new Exception("Transparent pixels still pass clicks through: " + misses);
                Console.WriteLine("PASS real layered-window hit testing covers ring center, corners and glyph gaps");
                for (int i = 0; i < points.GetLength(0); i++)
                {
                    if (indicator.InputHitTest(new Point(points[i, 0], points[i, 1])) == null)
                        throw new Exception("WPF has no input target inside the indicator.");
                }
                Console.WriteLine("PASS WPF hit testing has an input target throughout the tested area");
                var request = type.GetMethod("RequestDetails", BindingFlags.Instance | BindingFlags.NonPublic);
                request.Invoke(indicator, null); request.Invoke(indicator, null); Pump(80);
                if (opened != 1) throw new Exception("Queued open requests did not merge.");
                for (int i = 0; i < 5; i++) { request.Invoke(indicator, null); Pump(80); }
                if (opened != 6 || !indicator.IsVisible) throw new Exception("Repeated requests lost the indicator or details.");
                Console.WriteLine("PASS queued detail requests merge and repeated opens retain the indicator");
                CheckPlacement(assembly, indicator, state, settings, args.Length > 1 && args[1] == "--expect-placement-bug");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL indicator input probe: " + ex.GetType().Name + ": " + ex.Message); return 1;
            }
            finally
            {
                if (indicator != null) indicator.Close(); if (underneath != null) underneath.Close(); app.Shutdown();
            }
        }
        private static void CheckPlacement(Assembly assembly, Window indicator, UsageState state, Settings settings, bool baseline)
        {
            var native = assembly.GetType("CodexMeter.Windows.TaskbarNative", true);
            var place = native.GetMethod("Place");
            IntPtr handle = new WindowInteropHelper(indicator).Handle;
            Rect original; GetWindowRect(handle, out original);
            int changes = 0, shows = 0;
            HwndSourceHook watch = delegate(IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled) {
                if (message == 0x46) {
                    changes++;
                    var position = (WindowPosition)Marshal.PtrToStructure(l, typeof(WindowPosition));
                    if ((position.Flags & 0x40) != 0) shows++;
                }
                return IntPtr.Zero;
            };
            var source = HwndSource.FromHwnd(handle); source.AddHook(watch);
            try
            {
                for (int i = 0; i < 20; i++) place.Invoke(null, new object[] { handle, original.Left, original.Top, original.Width, original.Height });
                int idleChanges = changes, idleShows = shows;
                indicator.Hide();
                place.Invoke(null, new object[] { handle, original.Left + 10, original.Top, original.Width, original.Height });
                bool leakedVisible = IsWindowVisible(handle);
                if (baseline) {
                    if (idleShows == 0 || !leakedVisible) throw new Exception("Baseline did not reproduce repeated native showing and hidden-window exposure.");
                    Console.WriteLine("PASS old placement repeats SWP_SHOWWINDOW and exposes the hidden indicator"); return;
                }
                if (idleChanges != 0 || idleShows != 0 || leakedVisible) throw new Exception("Stable placement still sends show/move messages or exposes a hidden HWND.");
                Console.WriteLine("PASS twenty stable placements send no redundant position/show messages; hidden windows stay hidden");

                var type = indicator.GetType();
                var snapshotType = assembly.GetType("CodexMeter.Windows.TaskbarSnapshot", true);
                var boundsType = assembly.GetType("CodexMeter.Windows.NativeRect", true);
                var snapshot = Activator.CreateInstance(snapshotType, true);
                var bounds = Activator.CreateInstance(boundsType);
                foreach (var pair in new[] { new[] { "Left", "300" }, new[] { "Top", "280" }, new[] { "Right", "900" }, new[] { "Bottom", "340" } })
                    boundsType.GetField(pair[0]).SetValue(bounds, int.Parse(pair[1]));
                snapshotType.GetField("Bounds").SetValue(snapshot, bounds);
                snapshotType.GetField("Available").SetValue(snapshot, true);
                snapshotType.GetField("ControlsKnown").SetValue(snapshot, true);
                snapshotType.GetField("Handle").SetValue(snapshot, new IntPtr(1234));
                snapshotType.GetField("Dpi").SetValue(snapshot, (uint)native.GetMethod("WindowDpi").Invoke(null, new object[] { handle }));
                var cache = type.GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic); cache.SetValue(indicator, snapshot);
                var partial = Activator.CreateInstance(snapshotType, true);
                foreach (var field in snapshotType.GetFields()) field.SetValue(partial, field.GetValue(snapshot));
                snapshotType.GetField("ControlsKnown").SetValue(partial, false);
                var accept = type.GetMethod("AcceptMeasurement", BindingFlags.Instance | BindingFlags.NonPublic);
                if ((bool)accept.Invoke(indicator, new[] { partial, snapshot }) || !object.ReferenceEquals(cache.GetValue(indicator), snapshot))
                    throw new Exception("Incomplete Explorer measurement erased stable placement.");
                if (!(bool)accept.Invoke(indicator, new[] { snapshot, snapshot })) throw new Exception("Matching complete measurement was rejected.");
                Console.WriteLine("PASS incomplete Explorer measurements preserve the last complete placement");

                type.GetMethod("SetPopupVisible").Invoke(indicator, new object[] { true });
                indicator.Left = 10; indicator.Top = 10;
                Rect first = new Rect(); int visibleChanges = 0;
                DependencyPropertyChangedEventHandler firstShow = delegate {
                    if (indicator.IsVisible) { visibleChanges++; GetWindowRect(handle, out first); }
                };
                indicator.IsVisibleChanged += firstShow;
                type.GetMethod("Place", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(indicator, new object[] { false }); Pump(100);
                indicator.IsVisibleChanged -= firstShow;
                Rect settled; GetWindowRect(handle, out settled);
                if (visibleChanges != 1 || first.Left != settled.Left || first.Top != settled.Top || first.Top < 280 || first.Bottom > 340)
                    throw new Exception("Indicator becomes visible before its taskbar coordinates settle.");
                Console.WriteLine("PASS the first visible indicator frame already has its final taskbar bounds");
                CheckFirstPopup(assembly, native, indicator, state, settings);
            }
            finally { source.RemoveHook(watch); }
        }
        private static void CheckFirstPopup(Assembly assembly, Type native, Window indicator, UsageState state, Settings settings)
        {
            var type = assembly.GetType("CodexMeter.Windows.DetailWindow", true);
            var detail = (Window)type.GetConstructors()[0].Invoke(new object[] { new Action(delegate { }), new Action(delegate { }),
                new Action(delegate { }), new Action(delegate { }), new Func<UsageState, IList<QuotaSample>>(s => new List<QuotaSample>()) });
            try {
                var anchor = (System.Drawing.Point)indicator.GetType().GetProperty("PopupAnchor").GetValue(indicator, null);
                type.GetMethod("PrepareForOpen").Invoke(detail, new object[] { anchor });
                type.GetMethod("Render").Invoke(detail, new object[] { state, settings, true });
                type.GetMethod("PositionNearCursor").Invoke(detail, new object[] { anchor });
                var handle = new WindowInteropHelper(detail).Handle;
                Rect first = new Rect();
                detail.IsVisibleChanged += delegate { if (detail.IsVisible) GetWindowRect(handle, out first); };
                detail.ShowActivated = false; detail.Show(); Pump(150);
                Rect settled; GetWindowRect(handle, out settled);
                var work = System.Windows.Forms.Screen.FromPoint(anchor).WorkingArea;
                double scale = (uint)native.GetMethod("WindowDpi").Invoke(null, new object[] { handle }) / 96.0;
                if (detail.Title != "CodexMeter for Windows" || first.Left != settled.Left || first.Top != settled.Top ||
                    first.Width != settled.Width || first.Height != settled.Height || Math.Abs(settled.Width - 380 * scale) > 1 ||
                    settled.Left < work.Left || settled.Top < work.Top || settled.Right > work.Right || settled.Bottom > work.Bottom)
                    throw new Exception("First popup frame has stale DPI, wrong branding or unsettled/out-of-work-area bounds.");
                Console.WriteLine("PASS the first popup frame has full branding, target DPI and stable visible bounds");
                var eventInfo = indicator.GetType().GetEvent("PlacementChanged");
                int followed = 0;
                Action follow = delegate {
                    followed++;
                    var movedAnchor = (System.Drawing.Point)indicator.GetType().GetProperty("PopupAnchor").GetValue(indicator, null);
                    type.GetMethod("PositionNearCursor").Invoke(detail, new object[] { movedAnchor });
                };
                eventInfo.AddEventHandler(indicator, follow);
                try {
                    var field = indicator.GetType().GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic);
                    var snapshot = field.GetValue(indicator); var snapshotType = snapshot.GetType();
                    var bounds = snapshotType.GetField("Bounds").GetValue(snapshot); var boundsType = bounds.GetType();
                    foreach (var name in new[] { "Top", "Bottom" }) boundsType.GetField(name).SetValue(bounds, (int)boundsType.GetField(name).GetValue(bounds) + 40);
                    snapshotType.GetField("Bounds").SetValue(snapshot, bounds);
                    var place = indicator.GetType().GetMethod("Place", BindingFlags.Instance | BindingFlags.NonPublic);
                    place.Invoke(indicator, new object[] { false }); Pump(100); place.Invoke(indicator, new object[] { false });
                    var movedAnchor = (System.Drawing.Point)indicator.GetType().GetProperty("PopupAnchor").GetValue(indicator, null);
                    Rect moved; GetWindowRect(handle, out moved);
                    if (followed != 1 || Math.Abs(moved.Bottom - (movedAnchor.Y - 8 * scale)) > 1)
                        throw new Exception("Open details did not follow the indicator's changed placement exactly once.");
                    Console.WriteLine("PASS open details follow changed indicator bounds; stable placement emits no extra anchor events");
                }
                finally { eventInfo.RemoveEventHandler(indicator, follow); }
            }
            finally { detail.Close(); }
        }
        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; public int Height { get { return Bottom - Top; } } public int Width { get { return Right - Left; } } }
        [StructLayout(LayoutKind.Sequential)] private struct WindowPosition { public IntPtr Window, After; public int X, Y, Width, Height; public uint Flags; }
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out Rect rect);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    }
}
#endif
