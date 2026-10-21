using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed class TaskbarSnapshot
    {
        public NativeRect Bounds;
        public List<OccupiedSpan> Occupied = new List<OccupiedSpan>();
        public bool Available, ControlsKnown;
        public OccupiedSpan? StartButton;
        public uint Dpi = 96;
        public IntPtr Handle;
        public bool Matches(TaskbarSnapshot other)
        {
            return other != null && Handle == other.Handle && Available == other.Available && Dpi == other.Dpi &&
                Bounds.Left == other.Bounds.Left && Bounds.Top == other.Bounds.Top &&
                Bounds.Right == other.Bounds.Right && Bounds.Bottom == other.Bounds.Bottom;
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }
    internal static class TaskbarNative
    {
        public static TaskbarSnapshot Read(bool readControls = true)
        {
            var result = new TaskbarSnapshot();
            var taskbar = FindWindow("Shell_TrayWnd", null);
            result.Handle = taskbar;
            if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out result.Bounds) || !IsWindowVisible(taskbar)) return result;
            if (result.Bounds.Width <= result.Bounds.Height || result.Bounds.Height < 12) return result;
            var screenBounds = System.Windows.Forms.Screen.FromHandle(taskbar).Bounds;
            if (result.Bounds.Top >= screenBounds.Bottom - 3 || result.Bounds.Bottom <= screenBounds.Top + 3) return result;
            result.Available = true;
            try { result.Dpi = GetDpiForWindow(taskbar); } catch (EntryPointNotFoundException) { }
            if (result.Dpi == 0) result.Dpi = 96;
            var tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            NativeRect trayBounds;
            if (tray != IntPtr.Zero && GetWindowRect(tray, out trayBounds))
                result.Occupied.Add(new OccupiedSpan(trayBounds.Left, trayBounds.Right));
            var nativeStart = FindWindowEx(taskbar, IntPtr.Zero, "Start", null);
            NativeRect startBounds;
            if (nativeStart != IntPtr.Zero && GetWindowRect(nativeStart, out startBounds))
                result.StartButton = new OccupiedSpan(startBounds.Left, startBounds.Right);
            if (!readControls) return result;
            // Read bounds and stable control IDs, never account data or application titles.
            try
            {
                var root = AutomationElement.FromHandle(taskbar);
                var condition = new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
                var controls = root.FindAll(TreeScope.Descendants, condition);
                foreach (AutomationElement control in controls)
                {
                    var info = control.Current;
                    if (info.IsOffscreen) continue;
                    var bounds = info.BoundingRectangle;
                    if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Width >= result.Bounds.Width * .8) continue;
                    if (bounds.Bottom <= result.Bounds.Top || bounds.Top >= result.Bounds.Bottom) continue;
                    result.Occupied.Add(new OccupiedSpan(bounds.Left, bounds.Right));
                    var id = info.AutomationId ?? "";
                    if (bounds.Width <= 96 * result.Dpi / 96.0 &&
                        (id.Equals("StartButton", StringComparison.OrdinalIgnoreCase) ||
                         id.Equals("Start", StringComparison.OrdinalIgnoreCase) ||
                         id.EndsWith(".StartButton", StringComparison.OrdinalIgnoreCase)))
                        result.StartButton = new OccupiedSpan(bounds.Left, bounds.Right);
                    result.ControlsKnown = true;
                }
            }
            catch (ElementNotAvailableException) { result.ControlsKnown = false; }
            catch (InvalidOperationException) { result.ControlsKnown = false; }
            catch (COMException) { result.ControlsKnown = false; }
            if (tray == IntPtr.Zero)
                result.Occupied.Add(new OccupiedSpan(result.Bounds.Right - 230 * result.Dpi / 96.0, result.Bounds.Right));
            return result;
        }
        public static bool IsFullscreenOutsideTaskbar(NativeRect taskbar)
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            if (IsTaskbarWindow(foreground)) return false;
            var className = new StringBuilder(128);
            GetClassName(foreground, className, className.Capacity);
            string processName = null;
            uint processId;
            GetWindowThreadProcessId(foreground, out processId);
            try { using (var process = System.Diagnostics.Process.GetProcessById((int)processId)) processName = process.ProcessName; }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            if (WindowGeometry.IsShellSurface(className.ToString(), processName)) return false;
            NativeRect rect;
            if (!GetWindowRect(foreground, out rect)) return false;
            try
            {
                NativeRect visible;
                if (DwmGetWindowAttribute(foreground, 9, out visible, Marshal.SizeOf(typeof(NativeRect))) == 0) rect = visible;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(taskbar.Left + taskbar.Width / 2, taskbar.Top + taskbar.Height / 2)).Bounds;
            return WindowGeometry.CoversTaskbarMonitor(new ScreenRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
                new ScreenRect(screen.Left, screen.Top, screen.Right, screen.Bottom),
                new ScreenRect(taskbar.Left, taskbar.Top, taskbar.Right, taskbar.Bottom));
        }
        public static void SetNoActivate(IntPtr handle)
        {
            long style = GetWindowLongPtr(handle, -20).ToInt64();
            SetWindowLongPtr(handle, -20, new IntPtr(style | 0x08000000L | 0x00000080L));
        }
        public static uint WindowDpi(IntPtr handle)
        {
            try { uint dpi = GetDpiForWindow(handle); if (dpi > 0) return dpi; }
            catch (EntryPointNotFoundException) { }
            return 96;
        }
        public static void MoveHidden(IntPtr handle, int x, int y)
        { SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0010 | 0x0001 | 0x0004); }
        public static void PlacePopup(IntPtr handle, int x, int y, int width, int height)
        { Position(handle, x, y, width, height, false); }
        public static void Place(IntPtr handle, int x, int y, int width, int height)
        {
            Position(handle, x, y, width, height, true);
            RestoreAboveTaskbar(handle);
        }
        private static void Position(IntPtr handle, int x, int y, int width, int height, bool topmost)
        {
            NativeRect current;
            if (GetWindowRect(handle, out current) && current.Left == x && current.Top == y &&
                current.Width == width && current.Height == height) return;
            // Visibility belongs to WPF Show/Hide. Do not re-show a layered HWND on every placement tick.
            SetWindowPos(handle, topmost ? new IntPtr(-1) : IntPtr.Zero, x, y, width, height, topmost ? 0x0010u : 0x0014u);
        }
        public static void RestoreAboveTaskbar(IntPtr handle)
        {
            NativeRect rect;
            if (!IsWindowVisible(handle) || !GetWindowRect(handle, out rect)) return;
            var point = new NativePoint { X = rect.Left + rect.Width / 2, Y = rect.Top + rect.Height / 2 };
            if (IsTaskbarWindow(WindowFromPoint(point)))
                SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002);
        }
        private static bool IsTaskbarWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return false;
            var root = GetAncestor(handle, 2);
            var name = new StringBuilder(128);
            GetClassName(root == IntPtr.Zero ? handle : root, name, name.Capacity);
            return name.ToString() == "Shell_TrayWnd" || name.ToString() == "Shell_SecondaryTrayWnd";
        }
        public static IDisposable WatchTaskbarForeground(Action changed) { return new ForegroundWatch(changed); }
        private sealed class ForegroundWatch : IDisposable
        {
            private readonly WinEvent callback;
            private IntPtr hook;
            public ForegroundWatch(Action changed)
            {
                callback = (h, kind, window, idObject, idChild, thread, time) => {
                    if (hook != IntPtr.Zero && IsTaskbarWindow(window)) changed();
                };
                hook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 0);
            }
            public void Dispose() { if (hook == IntPtr.Zero) return; UnhookWinEvent(hook); hook = IntPtr.Zero; }
        }
        private delegate void WinEvent(IntPtr hook, uint kind, IntPtr window, int idObject, int idChild, uint thread, uint time);
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        public static bool TryGetBounds(IntPtr handle, out NativeRect rect) { return GetWindowRect(handle, out rect); }
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr handle, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out NativeRect rect, int size);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string name, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string name, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder name, int length);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
