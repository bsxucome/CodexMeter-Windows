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
                className == "Shell_SecondaryTrayWnd" || className == "NotifyIconOverflowWindow" ||
                (processName == "explorer" && (className == "Windows.UI.Core.CoreWindow" ||
                    className == "XamlExplorerHostIslandWindow" || className == "TopLevelWindowForOverflowXamlIsland" ||
                    className == "Windows.UI.Composition.DesktopWindowContentBridge")) ||
                processName == "StartMenuExperienceHost" || processName == "SearchHost" || processName == "ShellExperienceHost";
        }
        public static ScreenRect PlacePopup(ScreenRect work, double anchorX, double anchorY,
            double width, double preferredHeight, double minimumHeight, double scale)
        {
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) scale = 1;
            double margin = 8 * scale, gap = 8 * scale;
            double availableWidth = Math.Max(1, work.Right - work.Left - margin * 2);
            double availableHeight = Math.Max(1, work.Bottom - work.Top - margin * 2);
            width = Math.Min(width * scale, availableWidth);
            double above = Math.Max(0, anchorY - work.Top - margin - gap);
            double below = Math.Max(0, work.Bottom - anchorY - margin - gap);
            bool onTop = above >= minimumHeight * scale || above >= below;
            double height = Math.Min(preferredHeight * scale, Math.Min(availableHeight,
                Math.Max(Math.Min(minimumHeight * scale, availableHeight), onTop ? above : below)));
            double x = Math.Max(work.Left + margin, Math.Min(anchorX - width / 2, work.Right - margin - width));
            double y = Math.Max(work.Top + margin, Math.Min(onTop ? anchorY - height - gap : anchorY + gap,
                work.Bottom - margin - height));
            return new ScreenRect(x, y, x + width, y + height);
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
