using System;
using System.Windows;
using System.Windows.Media;

namespace CodexMeter.Windows
{
    internal sealed class IconGlyph : FrameworkElement
    {
        public string Kind { get; set; }
        public Brush Color { get; set; }
        public IconGlyph(string kind, Brush color, double size)
        { Kind = kind; Color = color; Width = size; Height = size; VerticalAlignment = VerticalAlignment.Center; }
        protected override void OnRender(DrawingContext dc)
        {
            var color = Color ?? Brushes.Gray;
            var pen = new Pen(color, 1.15) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.PushTransform(new ScaleTransform(ActualWidth / 16, ActualHeight / 16));
            switch (Kind)
            {
                case "battery":
                    dc.DrawRoundedRectangle(null, pen, new Rect(1, 4, 12, 8), 1.7, 1.7);
                    dc.DrawLine(pen, new Point(15, 6.5), new Point(15, 9.5));
                    for (int i = 0; i < 3; i++) dc.DrawRoundedRectangle(color, null, new Rect(3 + i * 2.6, 6, 1.8, 4), .4, .4);
                    break;
                case "clock":
                    dc.DrawEllipse(null, pen, new Point(8, 8), 6, 6);
                    dc.DrawLine(pen, new Point(8, 4), new Point(8, 8));
                    dc.DrawLine(pen, new Point(8, 8), new Point(11, 9));
                    break;
                case "hourglass":
                    dc.DrawLine(pen, new Point(3, 2), new Point(13, 2));
                    dc.DrawLine(pen, new Point(3, 14), new Point(13, 14));
                    dc.DrawGeometry(null, pen, Geometry.Parse("M4,2 L4,5 Q4,6 8,8 Q12,10 12,11 L12,14 M12,2 L12,5 Q12,6 8,8 Q4,10 4,11 L4,14"));
                    break;
                case "refresh":
                    dc.DrawGeometry(null, pen, Geometry.Parse("M12.5,4.5 A5.5,5.5 0 1 0 13.4,9 M12.5,1.5 L12.5,5.5 L8.5,5.5"));
                    break;
                case "chevron":
                    dc.DrawGeometry(null, pen, Geometry.Parse("M6,3 L11,8 L6,13"));
                    break;
                case "gear":
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = i * Math.PI / 4;
                        dc.DrawLine(new Pen(color, 2), new Point(8 + 5 * Math.Cos(angle), 8 + 5 * Math.Sin(angle)),
                            new Point(8 + 6.6 * Math.Cos(angle), 8 + 6.6 * Math.Sin(angle)));
                    }
                    dc.DrawEllipse(null, new Pen(color, 1.6), new Point(8, 8), 4.7, 4.7);
                    dc.DrawEllipse(null, pen, new Point(8, 8), 1.8, 1.8);
                    break;
                case "check":
                    dc.DrawEllipse(color, null, new Point(8, 8), 6.5, 6.5);
                    dc.DrawGeometry(null, new Pen(Brushes.White, 1.3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                        Geometry.Parse("M4.5,8 L7,10.4 L11.5,5.5"));
                    break;
                case "warning":
                    dc.DrawGeometry(color, null, Geometry.Parse("M7,1.8 Q8,.3 9,1.8 L15,12.7 Q15.8,14.5 13.8,14.5 L2.2,14.5 Q.2,14.5 1,12.7 Z"));
                    dc.DrawLine(new Pen(Brushes.White, 1.2), new Point(8, 5), new Point(8, 9));
                    dc.DrawEllipse(Brushes.White, null, new Point(8, 11.5), .7, .7);
                    break;
                default:
                    dc.DrawEllipse(null, pen, new Point(8, 8), 6, 6);
                    dc.DrawLine(pen, new Point(8, 5), new Point(8, 9));
                    dc.DrawEllipse(color, null, new Point(8, 11.5), .6, .6);
                    break;
            }
            dc.Pop();
        }
    }
}
