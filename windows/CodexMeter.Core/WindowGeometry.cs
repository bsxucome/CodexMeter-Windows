using System;

namespace CodexMeter.Core
{
    public struct ScreenRect
    {
        public double Left, Top, Right, Bottom;
        public ScreenRect(double left, double top, double right, double bottom)
        { Left = left; Top = top; Right = right; Bottom = bottom; }
    }
    public static class WindowGeometry
    {
        public static bool IsShellSurface(string className, string processName)
        {
            return className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd" ||
                processName == "StartMenuExperienceHost" || processName == "SearchHost" || processName == "ShellExperienceHost";
        }
        public static bool CoversTaskbarMonitor(ScreenRect window, ScreenRect monitor, ScreenRect taskbar)
        {
            // A fullscreen window on another display must not hide the primary indicator.
            bool sameMonitor = taskbar.Right > monitor.Left && taskbar.Left < monitor.Right &&
                taskbar.Bottom > monitor.Top && taskbar.Top < monitor.Bottom;
            return sameMonitor && window.Left <= monitor.Left + 1 && window.Top <= monitor.Top + 1 &&
                window.Right >= monitor.Right - 1 && window.Bottom >= monitor.Bottom - 1;
        }
    }
}
