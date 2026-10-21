using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed class DetailWindow : Window
    {
        private readonly Action refresh, settings, history, quit;
        private readonly Func<UsageState, IList<QuotaSample>> weeklySamples;
        private ScrollViewer scroller;
        private double offset, preferredHeight = 400;
        private System.Drawing.Point? positionedAnchor;
        public DetailWindow(Action refresh, Action settings, Action history, Action quit, Func<UsageState, IList<QuotaSample>> weeklySamples)
        {
            this.refresh = refresh; this.settings = settings; this.history = history; this.quit = quit;
            this.weeklySamples = weeklySamples;
            Title = L.ProductName; Width = 380; Height = preferredHeight; MinHeight = 240;
            AutomationProperties.SetName(this, Title);
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            Background = Brushes.Transparent;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Hide(); };
            Loaded += (s, e) => { UpdateLayout(); if (positionedAnchor.HasValue) PositionNearCursor(positionedAnchor); };
        }
        public void PrepareForOpen(System.Drawing.Point anchor)
        {
            positionedAnchor = anchor;
            // Establish target-monitor DPI while hidden, before measuring or displaying content.
            var handle = new WindowInteropHelper(this).EnsureHandle();
            if (!IsVisible)
            {
                // A taskbar anchor is at the screen edge; a full-size hidden HWND there may mostly
                // overlap a different monitor. Prime with a tiny rect entirely inside the target.
                var work = System.Windows.Forms.Screen.FromPoint(anchor).WorkingArea;
                MinHeight = 1; Width = 1; Height = 1;
                TaskbarNative.MoveHidden(handle, work.Left + 8, work.Top + 8);
                Width = 380;
            }
        }
        public void PositionNearCursor(System.Drawing.Point? screenAnchor = null)
        {
            var cursor = screenAnchor ?? positionedAnchor ?? System.Windows.Forms.Cursor.Position;
            positionedAnchor = cursor;
            var area = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;
            var handle = new WindowInteropHelper(this).EnsureHandle();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                uint dpi = TaskbarNative.WindowDpi(handle);
                double scale = dpi / 96.0;
                MinHeight = Math.Min(240, Math.Max(1, area.Height / scale - 16));
                var bounds = WindowGeometry.PlacePopup(new ScreenRect(area.Left, area.Top, area.Right, area.Bottom),
                    cursor.X, cursor.Y, 380, preferredHeight, MinHeight, scale);
                Width = (bounds.Right - bounds.Left) / scale; Height = (bounds.Bottom - bounds.Top) / scale;
                Left = bounds.Left / scale; Top = bounds.Top / scale;
                TaskbarNative.PlacePopup(handle, (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
                    (int)Math.Round(bounds.Right - bounds.Left), (int)Math.Round(bounds.Bottom - bounds.Top));
                if (TaskbarNative.WindowDpi(handle) == dpi) break;
            }
        }
        public void Render(UsageState state, Settings configuration, bool preview)
        {
            if (scroller != null) offset = scroller.VerticalOffset;
            var p = Palette.For(configuration.Theme);
            Brush material = p.Background;
            if (!preview && PopupMaterial.Apply(this, p.Dark))
            {
                var tint = ((SolidColorBrush)p.Background).Color;
                tint.A = p.Dark ? (byte)185 : (byte)210;
                material = new SolidColorBrush(tint);
            }
            var shell = new Border { Background = material, BorderBrush = p.Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(16) };
            var layout = new Grid(); shell.Child = layout; Content = shell;
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new StackPanel();
            var heading = new DockPanel();
            var reload = PlainButton(new IconGlyph("refresh", p.Muted, 14), "刷新", p, (s, e) => refresh());
            reload.IsEnabled = !state.IsLoading && !preview;
            reload.ToolTip = state.IsLoading ? "正在刷新" : "刷新";
            reload.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(reload, Dock.Right);
            heading.Children.Add(reload);
            var titleGroup = new StackPanel();
            var title = p.Label(L.ProductName, 14, p.Text); title.FontWeight = FontWeights.SemiBold;
            titleGroup.Children.Add(title);
            var account = p.Label(state.Plan, 11, p.Muted); account.Margin = new Thickness(0, 3, 0, 0);
            titleGroup.Children.Add(account); heading.Children.Add(titleGroup); header.Children.Add(heading);
            if (preview)
            {
                var banner = LabelWithIcon("info", "预览模式 · 演示数据", 11, Palette.Brush(p.Dark ? "#FFBC69" : "#AD640A"), p);
                banner.Margin = new Thickness(0, 12, 0, 0); header.Children.Add(banner);
            }
            header.Children.Add(Divider(p, 14, 0)); layout.Children.Add(header);
            var body = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            // Hidden keeps wheel, touchpad and keyboard scrolling without reserving a scrollbar gutter.
            scroller = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, ClipToBounds = true };
            AutomationProperties.SetAutomationId(scroller, "Detail.Scroll");
            Grid.SetRow(scroller, 1); layout.Children.Add(scroller);
            if (state.Error != null || state.IsStale)
            {
                var warning = LabelWithIcon("warning", (state.IsStale ? "数据可能已过期。 " : "") + (state.Error ?? ""), 11,
                    Palette.Brush(p.Dark ? "#FFBD75" : "#A36012"), p);
                warning.Margin = new Thickness(0, 0, 0, 14); body.Children.Add(warning);
            }
            if (state.Windows.Count == 0)
            {
                var waiting = p.Label(state.IsLoading ? "正在读取额度…" : "尚无可用额度", 13, p.Text);
                waiting.Margin = new Thickness(0, 8, 0, 8); body.Children.Add(waiting);
                body.Children.Add(p.Label(state.Credits != null || state.ResetCredits != null ? "当前账号未提供额度窗口。" :
                    "请先在本机 Codex CLI 中完成登录。可在设置中指定安装位置。", 11, p.Muted));
            }
            var now = DateTimeOffset.UtcNow;
            var visibleWindows = configuration.VisibleWindows(state.Windows);
            var contentBody = body;
            var sections = new Dictionary<string, StackPanel>();
            body = new StackPanel(); sections["Quota"] = body;
            for (int index = 0; index < visibleWindows.Count; index++)
            {
                var window = visibleWindows[index];
                var quotaColor = Palette.QuotaColor(window.State(now), p.Dark);
                var section = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var time = window.TimeRemainingPercent(now);
                bool over = time.HasValue && window.RemainingPercent < time.Value;
                var paceColor = !time.HasValue ? p.Muted : over ? Palette.Brush(p.Dark ? "#FFAC40" : "#D18316") :
                    Palette.Brush("#34A853");
                var stateLabel = LabelWithIcon(!time.HasValue ? "info" : over ? "warning" : "check",
                    !time.HasValue ? "节奏未知" : over ? "消耗偏快" : "节奏正常", 11, paceColor, p);
                DockPanel.SetDock(stateLabel, Dock.Right); if (configuration.ShowPace) row.Children.Add(stateLabel);
                var name = p.Label(window.Title, 13, p.Text); name.FontWeight = FontWeights.SemiBold;
                row.Children.Add(name); section.Children.Add(row);
                section.Children.Add(Metric("battery", "剩余额度", window.RemainingPercent, quotaColor, p));
                if (time.HasValue && configuration.ShowTime)
                {
                    var metric = Metric("clock", "剩余时间", time.Value, Palette.TimeBrush((configuration.Appearance ?? new IndicatorAppearance()).TimeColor), p);
                    metric.Margin = new Thickness(0, 10, 0, 0); section.Children.Add(metric);
                }
                var resetRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
                if (window.ResetsAt.HasValue)
                {
                    var date = p.Label(window.ResetsAt.Value.ToLocalTime().ToString("MM/dd HH:mm"), 11, p.Muted);
                    date.FontFamily = new FontFamily("Segoe UI"); Typography.SetNumeralAlignment(date, FontNumeralAlignment.Tabular);
                    date.ToolTip = "重置于 " + window.ResetsAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    DockPanel.SetDock(date, Dock.Right); resetRow.Children.Add(date);
                }
                resetRow.Children.Add(LabelWithIcon("hourglass", window.Countdown(now), 11, p.Muted, p));
                if (configuration.ShowReset) section.Children.Add(resetRow); body.Children.Add(section);
                if (index < visibleWindows.Count - 1) body.Children.Add(Divider(p, 12, 12));
            }
            if (configuration.ShowCredits && state.Credits != null) sections["Credits"] = CreditSection(state.Credits, p);
            if (configuration.ShowResetCredits && state.ResetCredits != null) sections["Resets"] = ResetSection(state.ResetCredits, now, p);
            if (configuration.ShowWeeklyHistory)
            {
                body = new StackPanel(); sections["History"] = body;
                var weekly = state.Windows.FirstOrDefault(w => w.Id == "codex:10080");
                var link = PlainButton("每周额度历史  ›", "每周额度历史", p, (s, e) => history());
                link.HorizontalContentAlignment = HorizontalAlignment.Left; link.FontWeight = FontWeights.SemiBold;
                link.Padding = new Thickness(0); body.Children.Add(link);
                if (weekly != null)
                {
                    var interval = HistoryCalendar.Cycle(weekly, now);
                    body.Children.Add(new TrendChart(p, interval.From, interval.To, weeklySamples(state), null, true) { Margin = new Thickness(0, 8, 0, 0) });
                }
                else body.Children.Add(p.Label("标准周额度不可用", 11, p.Muted));
            }
            body = new StackPanel();
            if (configuration.ShowTokens) sections["Tokens"] = body;
            if (configuration.ShowTokens && state.Tokens != null && state.Tokens.Available)
            {
                var usage = state.Tokens;
                var tokenTitle = p.Label("Token 活动", 13, p.Text); tokenTitle.FontWeight = FontWeights.SemiBold;
                body.Children.Add(tokenTitle);
                body.Children.Add(TokenMetric("累计 Tokens", usage.LifetimeTokens, p));
                body.Children.Add(TokenMetric("单日峰值", usage.PeakDailyTokens, p));
                body.Children.Add(TokenMetric("当前连续天数", usage.CurrentStreakDays, p));
                body.Children.Add(TokenMetric("最长连续天数", usage.LongestStreakDays, p));
                body.Children.Add(TokenMetric("最长任务（秒）", usage.LongestRunningTurnSeconds, p));
                if (usage.DailyAvailable)
                {
                    var today = DateTimeOffset.UtcNow.UtcDateTime.Date;
                    var from = today.AddDays(-29).ToString("yyyy-MM-dd"); var to = today.ToString("yyyy-MM-dd");
                    var daily = new System.Collections.Generic.List<DailyTokens>();
                    foreach (var day in usage.Daily)
                        if (string.CompareOrdinal(day.Date, from) >= 0 && string.CompareOrdinal(day.Date, to) <= 0) daily.Add(day);
                    body.Children.Add(new TrendChart(p, new DateTimeOffset(today.AddDays(-29), TimeSpan.Zero),
                        new DateTimeOffset(today.AddDays(1), TimeSpan.Zero), null, daily, true) { Margin = new Thickness(0, 10, 0, 0) });
                    body.Children.Add(p.Label("最近 30 天 · 按服务日期", 10, p.Muted));
                }
                if (state.TokensStale) body.Children.Add(p.Label("Token 活动可能已过期。", 11, p.Muted));
            }
            if (configuration.ShowTokens && state.TokenError != null) body.Children.Add(p.Label(state.TokenError, 11, p.Muted));
            bool hasSection = false;
            foreach (var key in Settings.ValidSections(configuration.SectionOrder))
            {
                StackPanel section;
                if (!sections.TryGetValue(key, out section) || section.Children.Count == 0) continue;
                if (hasSection) contentBody.Children.Add(Divider(p, 12, 12));
                AutomationProperties.SetAutomationId(section, "Section." + key);
                contentBody.Children.Add(section); hasSection = true;
            }
            if (state.HistoryError != null) contentBody.Children.Add(p.Label(state.HistoryError, 11, p.Muted));
            scroller.ScrollToVerticalOffset(offset);
            var footer = new StackPanel();
            footer.Children.Add(Divider(p, 14, 7));
            var historyButton = PlainButton("查看历史记录  ›", "查看历史记录", p, (s, e) => history());
            historyButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            historyButton.HorizontalAlignment = HorizontalAlignment.Stretch; historyButton.MinHeight = 30;
            historyButton.Padding = new Thickness(0); footer.Children.Add(historyButton);
            var settingsRow = new StackPanel { Orientation = Orientation.Horizontal };
            settingsRow.Children.Add(new IconGlyph("chevron", p.Muted, 10) { Margin = new Thickness(0, 0, 7, 0) });
            settingsRow.Children.Add(new IconGlyph("gear", p.Text, 14) { Margin = new Thickness(0, 0, 7, 0) });
            settingsRow.Children.Add(p.Label("设置", 12, p.Text));
            var preferences = PlainButton(settingsRow, "设置", p, (s, e) => settings());
            preferences.HorizontalContentAlignment = HorizontalAlignment.Left;
            preferences.HorizontalAlignment = HorizontalAlignment.Stretch; preferences.MinHeight = 32;
            preferences.Padding = new Thickness(0); footer.Children.Add(preferences);
            footer.Children.Add(Divider(p, 7, 12));
            var footerLine = new DockPanel();
            var exit = PlainButton("退出", "退出", p, (s, e) => quit()); exit.FontSize = 11;
            exit.Padding = new Thickness(8, 0, 0, 0); exit.Foreground = p.Muted;
            DockPanel.SetDock(exit, Dock.Right); footerLine.Children.Add(exit);
            var updated = p.Label(state.UpdatedAt.HasValue ? "更新于 " + state.UpdatedAt.Value.ToLocalTime().ToString("HH:mm:ss") :
                state.IsLoading ? "正在连接…" : "等待更新", 10, p.Muted);
            updated.Opacity = .75; updated.VerticalAlignment = VerticalAlignment.Center;
            footerLine.Children.Add(updated); footer.Children.Add(footerLine);
            Grid.SetRow(footer, 2); layout.Children.Add(footer);
            L.Apply(this);
            shell.Measure(new Size(Width, double.PositiveInfinity));
            preferredHeight = Math.Max(240, Math.Ceiling(shell.DesiredSize.Height));
            if (positionedAnchor.HasValue) PositionNearCursor(positionedAnchor);
            else Height = Math.Min(preferredHeight, SystemParameters.WorkArea.Height - 20);
        }
        private static StackPanel CreditSection(CreditBalance credits, Palette p)
        {
            var section = new StackPanel();
            var title = p.Label("额度余额", 13, p.Text); title.FontWeight = FontWeights.SemiBold; section.Children.Add(title);
            string text = credits.Unlimited == true ? "不限量" : credits.Balance ??
                (credits.HasCredits.HasValue ? credits.HasCredits.Value ? "有可用余额，具体数量未提供" : "无可用余额" : "余额不可用");
            section.Children.Add(p.Label(text, 12, p.Text));
            return section;
        }
        private static StackPanel ResetSection(ResetCredits summary, DateTimeOffset now, Palette p)
        {
            var section = new StackPanel();
            var heading = new DockPanel();
            var count = p.Label(L.T("可用次数：") + summary.AvailableCount, 12, p.Text);
            DockPanel.SetDock(count, Dock.Right); heading.Children.Add(count);
            var title = p.Label("重置机会", 13, p.Text); title.FontWeight = FontWeights.SemiBold; heading.Children.Add(title); section.Children.Add(heading);
            int shown = 0;
            foreach (var credit in summary.UsableCredits(now))
            {
                // Each opportunity is read-only; IDs are never rendered or persisted.
                var row = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
                string description = (credit.Title ?? "") + "\n" + (credit.Description ?? "") + "\n" +
                    (credit.GrantedAt.HasValue ? L.T("获得于 ") + credit.GrantedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "");
                row.ToolTip = description.Trim();
                double? lifetime = credit.RemainingLifetime(now);
                if (lifetime.HasValue)
                {
                    var color = Palette.QuotaColor(lifetime < 10 ? Attention.Critical : lifetime < 20 ? Attention.Warning : Attention.Normal, p.Dark);
                    row.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = lifetime.Value, Height = 4,
                        Foreground = color, Background = p.Line, BorderThickness = new Thickness(0) });
                }
                var expiration = p.Label(credit.ExpiresAt.HasValue ? L.T("到期于 ") + credit.ExpiresAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "未提供到期时间", 11, p.Muted);
                expiration.Margin = new Thickness(0, 4, 0, 0); row.Children.Add(expiration);
                section.Children.Add(row); shown++;
            }
            if (summary.AvailableCount > shown) section.Children.Add(p.Label("部分重置机会未提供可用明细。", 11, p.Muted));
            return section;
        }
        private static Button PlainButton(object content, string name, Palette p, RoutedEventHandler click)
        {
            var button = p.Button("", click); button.Content = content;
            button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0);
            button.Padding = new Thickness(4); button.Margin = new Thickness(0);
            button.MinHeight = 20; button.FontSize = 12;
            AutomationProperties.SetName(button, name); return button;
        }
        private static DockPanel TokenMetric(string title, long? value, Palette p)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var number = p.Label(value.HasValue ? value.Value.ToString("N0") : "—", 11, p.Text);
            Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
            DockPanel.SetDock(number, Dock.Right); row.Children.Add(number); row.Children.Add(p.Label(title, 11, p.Muted));
            return row;
        }
        private static DockPanel LabelWithIcon(string kind, string text, double size, Brush color, Palette p)
        {
            var row = new DockPanel();
            var icon = new IconGlyph(kind, color, 12) { Margin = new Thickness(0, 0, 5, 0) };
            DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
            var label = p.Label(text, size, color); label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label); return row;
        }
        private static Border Divider(Palette p, double before, double after)
        { return new Border { Height = 1, Background = p.Line, Margin = new Thickness(0, before, 0, after) }; }
        private static StackPanel Metric(string symbol, string label, double value, Brush color, Palette p)
        {
            var stack = new StackPanel();
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var percentage = p.Label(value.ToString("0") + "%", 11, p.Text);
            percentage.FontFamily = new FontFamily("Segoe UI"); Typography.SetNumeralAlignment(percentage, FontNumeralAlignment.Tabular);
            DockPanel.SetDock(percentage, Dock.Right); row.Children.Add(percentage);
            row.Children.Add(LabelWithIcon(symbol, label, 11, p.Muted, p));
            stack.Children.Add(row);
            var grid = new Grid { Height = 4 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.00001, value), GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(.00001, 100 - value), GridUnitType.Star) });
            var track = new Border { Background = p.Line, CornerRadius = new CornerRadius(2) };
            Grid.SetColumnSpan(track, 2); grid.Children.Add(track);
            grid.Children.Add(new Border { Background = color, CornerRadius = new CornerRadius(2),
                Visibility = value > 0 ? Visibility.Visible : Visibility.Collapsed });
            stack.Children.Add(grid); return stack;
        }
    }
}
