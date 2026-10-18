using System;
using System.Windows;
using System.Windows.Media;

namespace CodexMeter.Windows
{
    internal sealed class QuotaBars : FrameworkElement
    {
        public double? Quota, Time;
        public Brush Color, Track;
        public Brush TimeBrush;
        public bool Single;
        public bool SideBySide;
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double half = Math.Max(1, (ActualWidth - 3) / 2);
            if (SideBySide && Time.HasValue)
            {
                Draw(dc, 0, half, 5, 8, Quota, Color);
                Draw(dc, half + 3, half, 5, 8, Time, TimeBrush ?? Palette.Brush("#0A84FF"));
            }
            else
            {
                Draw(dc, 0, ActualWidth, Single || !Time.HasValue ? 5 : 3, Single || !Time.HasValue ? 8 : 4, Quota, Color);
                if (!Single && Time.HasValue) Draw(dc, 0, ActualWidth, 11, 3, Time, TimeBrush ?? Palette.Brush("#0A84FF"));
            }
        }
        private void Draw(DrawingContext dc, double left, double available, double top, double height, double? value, Brush brush)
        {
            dc.DrawRoundedRectangle(Track, null, new Rect(left, top, available, height), 2, 2);
            double width = available * Math.Max(0, Math.Min(100, value ?? 0)) / 100;
            if (width > 0) dc.DrawRoundedRectangle(brush, null, new Rect(left, top, width, height), 2, 2);
        }
    }
}
