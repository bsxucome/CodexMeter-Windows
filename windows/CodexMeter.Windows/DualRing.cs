using System;
using System.Windows;
using System.Windows.Media;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed class DualRing : FrameworkElement
    {
        public double? Quota { get; set; }
        public double? RemainingTime { get; set; }
        public Brush QuotaBrush { get; set; }
        public Brush TrackBrush { get; set; }
        public Brush TimeBrush { get; set; }
        public double OuterStroke = 3, InnerStroke = 2, RingGap = 5;
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            double size = Math.Min(ActualWidth, ActualHeight);
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double outer = (size - OuterStroke) / 2, inner = outer - RingGap;
            var track = TrackBrush ?? Brushes.Gray;
            drawing.DrawEllipse(null, new Pen(track, OuterStroke), center, outer, outer);
            Arc(drawing, center, outer, OuterStroke, Quota ?? 0, QuotaBrush ?? Brushes.Gray);
            if (RemainingTime.HasValue && inner > 0)
            {
                drawing.DrawEllipse(null, new Pen(track, InnerStroke), center, inner, inner);
                Arc(drawing, center, inner, InnerStroke, RemainingTime.Value, TimeBrush ?? Palette.Brush("#0A84FF"));
            }
        }
        private static void Arc(DrawingContext drawing, Point center, double radius, double width, double percent, Brush color)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            if (percent <= 0) return;
            if (percent >= 100) { drawing.DrawEllipse(null, new Pen(color, width), center, radius, radius); return; }
            double angle = percent * 2 * Math.PI / 100;
            var path = new StreamGeometry();
            using (var builder = path.Open())
            {
                builder.BeginFigure(new Point(center.X, center.Y - radius), false, false);
                builder.ArcTo(new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)),
                    new Size(radius, radius), 0, percent > 50, SweepDirection.Clockwise, true, false);
            }
            drawing.DrawGeometry(null, new Pen(color, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, path);
        }
    }
}
