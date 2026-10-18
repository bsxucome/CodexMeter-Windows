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
        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; public int Height { get { return Bottom - Top; } } }
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    }
}
#endif
