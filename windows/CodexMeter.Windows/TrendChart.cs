using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed class TrendChart : FrameworkElement
    {
        private readonly Palette palette;
        private readonly IList<QuotaSample> quota;
        private readonly IList<TokenBucket> buckets;
        private readonly List<List<QuotaSample>> cycles;
        private readonly List<HistoryInterval> gaps = new List<HistoryInterval>();
        private List<List<QuotaSample>> displayCycles;
        private int displayColumns;
        private readonly DateTimeOffset from, to;
        private readonly bool compact, tokenMode;
        private QuotaSample hoveredQuota;
        private TokenBucket hoveredToken;
        private Point hoveredPosition;
        private static readonly DependencyProperty HoverFadeProperty = DependencyProperty.Register("HoverFade", typeof(double), typeof(TrendChart),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        private double LeftMargin { get { return compact && !tokenMode ? 2 : 38; } }
        private double Bottom { get { return compact && !tokenMode ? ActualHeight - 4 : ActualHeight - 22; } }
        public TrendChart(Palette p, DateTimeOffset from, DateTimeOffset to, IList<QuotaSample> quota, IList<DailyTokens> tokens, bool compact,
            IList<TokenBucket> grouped = null)
        {
            palette = p; this.from = from; this.to = to; this.compact = compact;
            tokenMode = tokens != null || grouped != null;
            this.quota = (quota ?? new List<QuotaSample>()).OrderBy(q => q.At).ToList();
            buckets = grouped ?? TokenSeries.Group(tokens ?? new List<DailyTokens>(), "Day");
            cycles = HistoryAnalytics.Cycles(this.quota);
            for (int i = 1; i < this.quota.Count; i++)
                if (this.quota[i].At - this.quota[i - 1].At > TimeSpan.FromMinutes(30)) gaps.Add(new HistoryInterval { From = this.quota[i - 1].At, To = this.quota[i].At });
            Height = compact ? 82 : 190; MinWidth = 140; ClipToBounds = true;
            System.Windows.Automation.AutomationProperties.SetHelpText(this, L.T("悬停查看采样日期和数值；空白表示没有观测数据。"));
            System.Windows.Automation.AutomationProperties.SetName(this, L.T(tokenMode ? "每日 Token 历史图" : "剩余额度历史图"));
            MouseMove += Hover;
            MouseLeave += (s, e) => { hoveredQuota = null; hoveredToken = null; InvalidateVisual(); };
        }
        private double X(DateTimeOffset at) { return LeftMargin + (ActualWidth - LeftMargin - 12) * (at - from).TotalSeconds / Math.Max(1, (to - from).TotalSeconds); }
        private double Y(double value, double maximum) { return 8 + (Bottom - 8) * (1 - value / maximum); }
        private static Brush Fade(Brush source, double opacity) { var result = source.Clone(); result.Opacity = opacity; return result; }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); if (ActualWidth <= 50 || ActualHeight <= 30) return;
            dc.DrawRectangle(palette.Surface, null, new Rect(0, 0, ActualWidth, ActualHeight));
            double maximum = tokenMode ? Math.Max(1, buckets.Count == 0 ? 1 : (double)buckets.Max(t => t.Tokens)) : 100;
            if (!compact || tokenMode)
                foreach (double value in new[] { 0.0, maximum / 2, maximum })
                {
                    double y = Y(value, maximum);
                    dc.DrawLine(new Pen(palette.Line, .5), new Point(LeftMargin, y), new Point(ActualWidth - 12, y));
                    Text(dc, tokenMode ? Short(value) : value.ToString("0") + "%", 0, y - 6, palette.Muted, 9);
                }
            dc.PushClip(new RectangleGeometry(new Rect(LeftMargin, 6, Math.Max(1, ActualWidth - LeftMargin - 12), Math.Max(1, Bottom - 4))));
            if (tokenMode) DrawTokens(dc, maximum); else DrawQuota(dc);
            if (hoveredQuota != null || hoveredToken != null)
            {
                var time = hoveredToken == null ? hoveredQuota.At : hoveredToken.From.AddTicks((hoveredToken.To - hoveredToken.From).Ticks / 2);
                double x = X(time);
                dc.DrawLine(new Pen(Fade(palette.Text, .6), 1) { DashStyle = DashStyles.Dash }, new Point(x, 7), new Point(x, Bottom));
                if (hoveredQuota != null) dc.DrawEllipse(palette.Accent, new Pen(palette.Surface, 1), new Point(x, Y(hoveredQuota.Remaining, 100)), 3, 3);
            }
            dc.Pop();
            if (!compact || tokenMode)
            {
                int count = compact ? 2 : 5;
                for (int i = 0; i < count; i++)
                {
                    var at = from.AddTicks((long)((to - from).Ticks * (i / (double)(count - 1))));
                    string format = (to - from).TotalDays > 180 ? "yyyy/MM" : "MM/dd";
                    var label = tokenMode ? at : at.ToLocalTime();
                    Text(dc, label.ToString(format, CultureInfo.InvariantCulture),
                        i == count - 1 ? ActualWidth - (format == "yyyy/MM" ? 52 : 40) : X(at), ActualHeight - 16, palette.Muted, 9);
                }
            }
            if ((tokenMode && buckets.Count == 0) || (!tokenMode && quota.Count == 0))
                Text(dc, "尚无记录", Math.Max(LeftMargin, ActualWidth / 2 - 28), ActualHeight / 2 - 8, palette.Muted, compact ? 10 : 12);
            DrawHover(dc);
        }
        private void DrawTokens(DrawingContext dc, double maximum)
        {
            foreach (var bucket in buckets)
            {
                double width = Math.Max(1, (X(bucket.To) - X(bucket.From)) * .7);
                double x = X(bucket.From.AddTicks((bucket.To - bucket.From).Ticks / 2)), y = Y((double)bucket.Tokens, maximum);
                dc.DrawRoundedRectangle(hoveredToken == null || hoveredToken == bucket ? palette.Accent : Fade(palette.Accent, 1 - .55 * (double)GetValue(HoverFadeProperty)),
                    null, new Rect(x - width / 2, y, width, Math.Max(1, Bottom - y)), 1, 1);
            }
        }
        private void Shade(DrawingContext dc, DateTimeOffset first, DateTimeOffset last)
        {
            if (last - first <= TimeSpan.FromMinutes(30)) return;
            double x = Math.Max(LeftMargin, X(first)), right = Math.Min(ActualWidth - 12, X(last));
            if (right > x) dc.DrawRectangle(Fade(palette.Muted, .10), null, new Rect(x, 7, right - x, Bottom - 7));
        }
        private void DrawQuota(DrawingContext dc)
        {
            int columns = Math.Max(1, (int)(ActualWidth - LeftMargin - 12));
            if (displayCycles == null || displayColumns != columns)
            { displayColumns = columns; displayCycles = cycles.Select(c => HistorySeries.DisplayPoints(c, from, to, columns)).ToList(); }
            foreach (var gap in gaps) Shade(dc, gap.From, gap.To);
            for (int cycleIndex = 0; cycleIndex < cycles.Count; cycleIndex++)
            {
                var cycle = cycles[cycleIndex];
                var visual = new List<QuotaSample>(displayCycles[cycleIndex]);
                var first = cycle[0];
                if (first.ResetsAt.HasValue && first.DurationMinutes > 0)
                {
                    var boundary = first.ResetsAt.Value.AddMinutes(-first.DurationMinutes.Value);
                    var previous = cycleIndex == 0 ? null : cycles[cycleIndex - 1];
                    if (boundary >= from && boundary < first.At && (previous == null || boundary >= previous.Last().At))
                    {
                        visual.Insert(0, new QuotaSample { At = boundary, Remaining = 100, ResetsAt = first.ResetsAt, DurationMinutes = first.DurationMinutes, WindowId = first.WindowId });
                        Shade(dc, boundary, first.At);
                    }
                }
                if (first.ResetsAt.HasValue && first.DurationMinutes > 0)
                {
                    var reset = first.ResetsAt.Value; var start = reset.AddMinutes(-first.DurationMinutes.Value);
                    var idealFrom = start > from ? start : from; var idealTo = reset < to ? reset : to;
                    if (idealTo > idealFrom)
                        dc.DrawLine(new Pen(Fade(palette.Muted, .35), .75) { DashStyle = DashStyles.Dash },
                            new Point(X(idealFrom), Y((reset - idealFrom).TotalMinutes / first.DurationMinutes.Value * 100, 100)),
                            new Point(X(idealTo), Y((reset - idealTo).TotalMinutes / first.DurationMinutes.Value * 100, 100)));
                }
                var raw = new StreamGeometry();
                using (var builder = raw.Open())
                {
                    builder.BeginFigure(new Point(X(visual[0].At), Bottom), true, true);
                    foreach (var point in visual) builder.LineTo(new Point(X(point.At), Y(point.Remaining, 100)), true, false);
                    builder.LineTo(new Point(X(visual.Last().At), Bottom), true, false);
                }
                var accent = ((SolidColorBrush)palette.Accent).Color;
                var top = accent; top.A = compact ? (byte)56 : (byte)71;
                var bottom = accent; bottom.A = 0;
                dc.DrawGeometry(new LinearGradientBrush(top, bottom, 90), null, raw);
                var values = HistoryAnalytics.Smooth(visual);
                var tangents = HistoryAnalytics.Tangents(visual, values);
                var curve = new StreamGeometry();
                using (var builder = curve.Open())
                {
                    builder.BeginFigure(new Point(X(visual[0].At), Y(values[0], 100)), false, false);
                    for (int i = 0; i < visual.Count - 1; i++)
                    {
                        double minutes = (visual[i + 1].At - visual[i].At).TotalMinutes;
                        builder.BezierTo(new Point(X(visual[i].At.AddMinutes(minutes / 3)), Y(values[i] + tangents[i] * minutes / 3, 100)),
                            new Point(X(visual[i + 1].At.AddMinutes(-minutes / 3)), Y(values[i + 1] - tangents[i + 1] * minutes / 3, 100)),
                            new Point(X(visual[i + 1].At), Y(values[i + 1], 100)), true, false);
                    }
                }
                var pen = new Pen(Fade(palette.Accent, .55), compact ? 1 : 1.25) {
                    DashStyle = new DashStyle(compact ? new double[] { 1, 3 } : new double[] { 2, 4 }, 0),
                    DashCap = PenLineCap.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawGeometry(null, pen, curve);
                if (visual.Count == 1) dc.DrawEllipse(palette.Accent, null, new Point(X(first.At), Y(first.Remaining, 100)), 2, 2);
            }
            if (quota.Count > 0) Shade(dc, quota.Last().At, DateTimeOffset.UtcNow < to ? DateTimeOffset.UtcNow : to);
        }
        private static void Text(DrawingContext dc, string text, double x, double y, Brush color, double size)
        {
            dc.DrawText(new FormattedText(L.T(text), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI, Microsoft YaHei UI"), size, color, 1), new Point(x, y));
        }
        public static string Short(double value)
        {
            return value >= 1000000000 ? (value / 1000000000).ToString("0.#") + "B" :
                value >= 1000000 ? (value / 1000000).ToString("0.#") + "M" : value >= 1000 ? (value / 1000).ToString("0.#") + "K" : value.ToString("0");
        }
        private void Hover(object sender, MouseEventArgs e)
        {
            var previous = hoveredToken;
            hoveredPosition = e.GetPosition(this); double x = hoveredPosition.X;
            hoveredQuota = null; hoveredToken = null;
            if (x < LeftMargin || x > ActualWidth - 12) { InvalidateVisual(); return; }
            var at = from.AddSeconds((to - from).TotalSeconds * (x - LeftMargin) / Math.Max(1, ActualWidth - LeftMargin - 12));
            if (tokenMode)
            {
                hoveredToken = buckets.FirstOrDefault(b => at >= b.From && at < b.To);
                if (hoveredToken != previous && hoveredToken != null)
                    BeginAnimation(HoverFadeProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
            }
            else if (quota.Count > 0)
            {
                hoveredQuota = HistorySeries.Nearest(quota, at);
                if (Math.Abs((hoveredQuota.At - at).TotalMinutes) > 30) hoveredQuota = null;
            }
            InvalidateVisual();
        }
        private void DrawHover(DrawingContext dc)
        {
            if (hoveredQuota == null && hoveredToken == null) return;
            string label = hoveredToken != null ? hoveredToken.From.ToString("yyyy-MM-dd") +
                (hoveredToken.To - hoveredToken.From > TimeSpan.FromDays(1) ? " – " + hoveredToken.To.AddDays(-1).ToString("yyyy-MM-dd") : "") +
                "\n" + hoveredToken.Tokens.ToString("N0") + " Tokens · " + L.T("已记录日期 ") + hoveredToken.ObservedDays :
                hoveredQuota.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + "\n" + L.T("剩余额度 ") + hoveredQuota.Remaining.ToString("0.##") + "%";
            var formatted = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI, Microsoft YaHei UI"), 10, palette.Text, 1) { MaxTextWidth = Math.Max(1, ActualWidth - 24) };
            double width = Math.Min(ActualWidth - 4, formatted.Width + 16), height = Math.Min(ActualHeight - 4, formatted.Height + 12);
            double left = Math.Max(2, Math.Min(hoveredPosition.X + 12, ActualWidth - width - 2));
            double top = Math.Max(2, Math.Min(hoveredPosition.Y - height - 8, ActualHeight - height - 2));
            dc.DrawRoundedRectangle(palette.Background, new Pen(palette.Line, 1), new Rect(left, top, width, height), 4, 4);
            dc.DrawText(formatted, new Point(left + 8, top + 6));
        }
    }
}
