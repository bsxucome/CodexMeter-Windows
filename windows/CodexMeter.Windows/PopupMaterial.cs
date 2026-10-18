using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexMeter.Windows
{
    internal static class PopupMaterial
    {
        public static bool Apply(Window window, bool dark)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return false;
            try
            {
                int corner = 2, mode = dark ? 1 : 0, backdrop = 3;
                DwmSetWindowAttribute(handle, 33, ref corner, 4);
                DwmSetWindowAttribute(handle, 20, ref mode, 4);
                if (DwmSetWindowAttribute(handle, 38, ref backdrop, 4) != 0) return false;
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                if (DwmExtendFrameIntoClientArea(handle, ref margins) != 0) return false;
                var source = HwndSource.FromHwnd(handle);
                if (source != null && source.CompositionTarget != null)
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref Margins margins);
    }
}
