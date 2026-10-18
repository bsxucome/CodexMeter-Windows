using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Documents;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed class IndicatorView : UserControl
    {
        private readonly DualRing rings;
        private readonly TextBlock percentage, caption, stale;
        private readonly Border surface;
        private readonly Grid row;
        private readonly StackPanel numbers;
        private readonly QuotaBars bars;
        private double lastQuota = -1;
        private string lastDigits;
        public Border Surface { get { return surface; } }
        public IndicatorView()
        {
            Height = 32; UseLayoutRounding = true;
            surface = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(3, 2, 3, 2), Cursor = Cursors.Hand };
            row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rings = new DualRing { Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(rings);
            bars = new QuotaBars { Width = 28, Height = 18, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            row.Children.Add(bars);
            numbers = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            percentage = new TextBlock { FontSize = 13, FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold,
                LineHeight = 15 };
            caption = new TextBlock { Text = "Codex", FontSize = 9, FontFamily = new FontFamily("Segoe UI"), LineHeight = 10 };
            numbers.Children.Add(percentage); numbers.Children.Add(caption); Grid.SetColumn(numbers, 1); row.Children.Add(numbers);
            stale = new TextBlock { Text = "•", Foreground = Palette.Brush("#EAB056"), FontSize = 14,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 0, 0, 0) };
            Grid.SetColumn(stale, 2); row.Children.Add(stale);
            surface.Child = row; Content = surface;
        }
        public void Render(UsageState state, Settings settings, bool preview, bool transparentBackground = false)
        {
            var p = Palette.ForTaskbar(settings.Theme, preview);
            var a = (settings.Appearance ?? new IndicatorAppearance()).Bounded();
            var selected = QuotaParser.Select(state.Windows, settings.SelectedWindow);
            var now = DateTimeOffset.UtcNow;
            var quotaBrush = selected == null || state.IsStale ? p.Muted : Palette.QuotaColor(selected.State(now), p.Dark);
            rings.Quota = selected == null ? (double?)null : selected.RemainingPercent;
            rings.RemainingTime = selected == null ? null : selected.TimeRemainingPercent(now);
            rings.Width = a.RingSize; rings.Height = a.RingSize;
            rings.OuterStroke = a.OuterStroke; rings.InnerStroke = a.InnerStroke; rings.RingGap = a.RingGap;
            rings.TimeBrush = Palette.TimeBrush(a.TimeColor);
            rings.QuotaBrush = quotaBrush; rings.TrackBrush = p.Line; rings.InvalidateVisual();
            string style = Settings.ValidStyle(settings.IndicatorStyle);
            bool noSymbol = style == "Percentage";
            rings.Visibility = style == "Rings" ? Visibility.Visible : Visibility.Collapsed;
            bars.Visibility = style == "Horizontal" || style == "Stacked" || style == "Progress" ? Visibility.Visible : Visibility.Collapsed;
            bars.Single = style == "Progress"; bars.Width = a.BarWidth + (bars.Single ? 10 : 0);
            bars.SideBySide = style == "Horizontal";
            numbers.Visibility = bars.Single ? Visibility.Collapsed : Visibility.Visible;
            bars.Quota = rings.Quota; bars.Time = rings.RemainingTime; bars.Color = quotaBrush; bars.Track = p.Line; bars.TimeBrush = Palette.TimeBrush(a.TimeColor); bars.InvalidateVisual();
            row.ColumnDefinitions[0].Width = new GridLength(noSymbol ? 0 : style == "Rings" ? a.RingSize + 2 : bars.Width + 2);
            numbers.Margin = new Thickness(noSymbol || bars.Single ? 0 : a.Spacing, 0, 0, 0);
            percentage.Text = selected == null ? "--" : selected.RemainingPercent.ToString("0") + "%";
            if (style == "Stacked") percentage.Text = "Q " + percentage.Text;
            percentage.FontSize = style == "Stacked" ? Math.Min(12, a.FontSize - 2) : a.FontSize;
            percentage.LineHeight = style == "Stacked" ? Math.Min(14, a.FontSize) : a.FontSize + 2;
            caption.Text = style == "Stacked" ? "T " + (rings.RemainingTime.HasValue ? rings.RemainingTime.Value.ToString("0") + "%" : "--") : "Codex";
            caption.FontSize = style == "Stacked" ? Math.Min(12, a.FontSize - 2) : a.CaptionSize; caption.LineHeight = style == "Stacked" ? Math.Min(14, a.FontSize) : a.CaptionSize + 1;
            caption.Visibility = (style == "Stacked" ? rings.RemainingTime.HasValue : style != "Percentage" && a.ShowCaption) ? Visibility.Visible : Visibility.Collapsed;
            var textColor = p.Text as SolidColorBrush;
            var brush = new SolidColorBrush(textColor == null ? Colors.White : textColor.Color);
            percentage.Foreground = brush;
            string label = percentage.Text;
            percentage.Inlines.Clear();
            for (int i = 0; i < label.Length; i++)
            {
                var digitBrush = new SolidColorBrush(brush.Color);
                if (selected != null && lastQuota >= 0 && selected.RemainingPercent < lastQuota && !state.IsStale && char.IsDigit(label[i]) &&
                    (lastDigits == null || lastDigits.Length != label.Length || lastDigits[i] != label[i]))
                {
                    digitBrush.Color = Colors.IndianRed;
                    digitBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Colors.IndianRed, brush.Color, TimeSpan.FromSeconds(2)) {
                        BeginTime = TimeSpan.FromMilliseconds(500), FillBehavior = FillBehavior.HoldEnd
                    });
                }
                percentage.Inlines.Add(new Run(label[i].ToString()) { Foreground = digitBrush });
            }
            lastDigits = label;
            lastQuota = selected == null ? -1 : selected.RemainingPercent;
            caption.Foreground = p.Muted; stale.Visibility = state.IsStale ? Visibility.Visible : Visibility.Collapsed;
            percentage.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            stale.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Width = Math.Ceiling(6 + row.ColumnDefinitions[0].Width.Value + numbers.Margin.Left +
                (bars.Single ? 0 : Math.Max(percentage.DesiredSize.Width, caption.DesiredSize.Width)) + stale.DesiredSize.Width);
            surface.Background = preview && !transparentBackground ? p.Background : Brushes.Transparent;
            ToolTip = selected == null ? "CodexMeter · " + (state.Error ?? state.Plan) :
                string.Format("{0} · 剩余 {1:0.#}%\n{2}\n点击展开；拖动调整位置", selected.Title, selected.RemainingPercent, selected.Countdown(now));
            AutomationProperties.SetHelpText(this, selected == null ? "等待额度数据" : selected.Title + "，剩余额度 " + percentage.Text);
            ToolTip = L.T((string)ToolTip);
            AutomationProperties.SetName(this, L.T("CodexMeter 任务栏额度"));
            AutomationProperties.SetHelpText(this, L.T(AutomationProperties.GetHelpText(this)));
            AutomationProperties.SetAutomationId(this, "IndicatorStyle." + style);
        }
    }
}
