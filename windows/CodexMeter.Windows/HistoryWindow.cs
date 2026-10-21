using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed class HistoryWindow : Window
    {
        private readonly HistoryStore history;
        private readonly bool preview;
        private readonly Func<UsageState> readState;
        private readonly Func<Settings> readSettings;
        private ComboBox range, window, tokenRange;
        private StackPanel charts, navigation;
        private TextBlock status, periodLabel;
        private Button previous, next;
        private string account, windowId;
        private bool rendering, fullScreen;
        private int tokenChoice = 1;
        private DateTime anchor = DateTime.Today;
        private DateTimeOffset start, end;
        private string tokenFrom, tokenTo;
        private string lastRenderKey;
        private DateTimeOffset lastRendered;
        public HistoryWindow(HistoryStore history, bool preview, Func<UsageState> state, Func<Settings> settings)
        {
            this.history = history; this.preview = preview; readState = state; readSettings = settings;
            Title = L.ProductName + " · 历史记录"; Width = 900; Height = 800; MinWidth = 560; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; UseLayoutRounding = true;
            PreviewKeyDown += (s, e) => {
                if (e.Key == Key.F11 || (fullScreen && e.Key == Key.Escape))
                {
                    fullScreen = !fullScreen;
                    WindowState = WindowState.Normal; WindowStyle = fullScreen ? WindowStyle.None : WindowStyle.SingleBorderWindow;
                    if (fullScreen) WindowState = WindowState.Maximized; e.Handled = true;
                }
            };
            Build();
        }
        private void Build()
        {
            var p = Palette.For(readSettings().Theme); Background = p.Background; Foreground = p.Text;
            var layout = new DockPanel { Margin = new Thickness(24) }; Content = layout;
            var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
            var title = p.Label("历史记录", 20, p.Text); title.FontWeight = FontWeights.SemiBold; header.Children.Add(title);
            header.Children.Add(p.Label(preview ? "预览模式 · 演示数据，不读取或写入本地历史。" : "当前账号的本地观测记录 · F11 全屏", 11, p.Muted));
            var filters = new Grid { Margin = new Thickness(0, 16, 0, 12) };
            filters.ColumnDefinitions.Add(new ColumnDefinition()); filters.ColumnDefinitions.Add(new ColumnDefinition());
            range = p.Combo(); range.Margin = new Thickness(0, 0, 12, 0);
            foreach (var item in new[] { "当前重置周期", "最近 7 天", "最近 14 天", "最近 30 天", "最近 90 天", "最近 365 天", "日历周", "日历月", "全部历史" }) range.Items.Add(item);
            range.SelectedIndex = 0;
            System.Windows.Automation.AutomationProperties.SetName(range, "历史时间范围");
            System.Windows.Automation.AutomationProperties.SetAutomationId(range, "QuotaRange");
            range.SelectionChanged += (s, e) => { if (!rendering) { anchor = DateTime.Today; RefreshData(); } }; filters.Children.Add(range);
            range.DropDownClosed += (s, e) => RefreshData();
            window = p.Combo(); Grid.SetColumn(window, 1); filters.Children.Add(window);
            System.Windows.Automation.AutomationProperties.SetName(window, "历史额度窗口");
            window.SelectionChanged += (s, e) => {
                if (rendering) return;
                var choice = window.SelectedItem as WindowChoice; windowId = choice == null ? null : choice.Id; RefreshData();
            };
            window.DropDownClosed += (s, e) => RefreshData();
            header.Children.Add(filters);
            navigation = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            previous = p.Button("上一期", (s, e) => Browse(-1)); navigation.Children.Add(previous);
            next = p.Button("下一期", (s, e) => Browse(1)); navigation.Children.Add(next);
            navigation.Children.Add(p.Button("回到本期", (s, e) => { anchor = DateTime.Today; RefreshData(); }));
            periodLabel = p.Label("", 11, p.Muted); periodLabel.VerticalAlignment = VerticalAlignment.Center; navigation.Children.Add(periodLabel);
            header.Children.Add(navigation);
            var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
            var actions = new WrapPanel();
            var export = p.Button("导出 CSV…", (s, e) => Export()); export.IsEnabled = !preview && history != null; actions.Children.Add(export);
            var clear = p.Button("清除当前账号记录…", (s, e) => Clear()); clear.IsEnabled = !preview && history != null; actions.Children.Add(clear);
            actions.Children.Add(p.Button("刷新", (s, e) => RefreshData())); bottom.Children.Add(actions);
            status = p.Label("", 11, p.Muted); status.Margin = new Thickness(0, 8, 0, 0); bottom.Children.Add(status);
            charts = new StackPanel(); layout.Children.Add(new ScrollViewer { Content = charts, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            RefreshData(); L.Apply(this);
        }
        private sealed class WindowChoice
        {
            public string Id, Title;
            public override string ToString() { return L.T(Title); }
        }
        private void Browse(int direction)
        {
            anchor = range.SelectedIndex == 7 ? anchor.AddMonths(direction) : anchor.AddDays(direction * 7);
            RefreshData();
        }
        public void RefreshData(bool force = true)
        {
            if (charts == null || rendering) return;
            var incoming = readState();
            if (account == incoming.AccountKey && (range.IsDropDownOpen || window.IsDropDownOpen || (tokenRange != null && tokenRange.IsDropDownOpen))) return;
            string key = incoming.AccountKey + "|" + incoming.UpdatedAt + "|" + incoming.TokensUpdatedAt + "|" + incoming.IsStale + "|" + incoming.HistoryError + "|" +
                (history == null ? -1 : history.Revision) + "|" + readSettings().HistoryEnabled;
            if (!force && lastRenderKey == key && DateTimeOffset.UtcNow - lastRendered < TimeSpan.FromMinutes(1)) return;
            rendering = true;
            try
            {
                var p = Palette.For(readSettings().Theme); var state = readState();
                if (account != state.AccountKey) { windowId = null; account = state.AccountKey; anchor = DateTime.Today; }
                var now = preview && state.UpdatedAt.HasValue ? state.UpdatedAt.Value : DateTimeOffset.UtcNow;
                var available = preview ? PreviewQuota(now).GroupBy(q => q.WindowId).Select(g => g.Last()).ToList() :
                    history != null && account != null ? history.Windows(account) : new List<QuotaSample>();
                var choices = available.Select(q => new WindowChoice { Id = q.WindowId, Title = q.Title }).ToList();
                foreach (var live in state.Windows)
                    if (!choices.Any(c => c.Id == live.Id)) choices.Add(new WindowChoice { Id = live.Id, Title = live.Title });
                var selected = choices.FirstOrDefault(c => c.Id == windowId) ?? choices.FirstOrDefault(c => c.Id == "codex:10080") ?? choices.FirstOrDefault();
                window.ItemsSource = choices; window.SelectedItem = selected; windowId = selected == null ? null : selected.Id;
                var liveWindow = state.Windows.FirstOrDefault(w => w.Id == windowId);
                var earliest = preview ? (DateTimeOffset?)now.AddDays(-30) : history != null && account != null && windowId != null ? history.EarliestQuota(account, windowId) : null;
                int mode = Math.Max(0, range.SelectedIndex);
                bool natural = mode == 6 || mode == 7;
                HistoryInterval interval;
                if (natural) interval = HistoryCalendar.Natural(anchor, mode == 7, TimeZoneInfo.Local);
                else if (mode == 0) interval = HistoryCalendar.Cycle(liveWindow, now);
                else interval = new HistoryInterval { From = mode == 8 ? earliest ?? now.AddDays(-7) :
                    now.AddDays(-new[] { 0, 7, 14, 30, 90, 365 }[mode]), To = now };
                start = interval.From; end = interval.To;
                if (end <= start) end = start.AddMinutes(1);
                navigation.Visibility = natural ? Visibility.Visible : Visibility.Collapsed;
                if (natural)
                {
                    previous.IsEnabled = earliest.HasValue && start > earliest.Value;
                    next.IsEnabled = HistoryCalendar.Natural(mode == 7 ? anchor.AddMonths(1) : anchor.AddDays(7), mode == 7, TimeZoneInfo.Local).From <= now;
                    periodLabel.Text = start.ToLocalTime().ToString("yyyy-MM-dd") + " – " + end.AddTicks(-1).ToLocalTime().ToString("yyyy-MM-dd");
                }
                var quota = preview ? PreviewQuota(now).Where(q => q.WindowId == windowId && q.At >= start && q.At <= end).ToList() :
                    history != null && account != null && windowId != null ? history.ReadQuota(account, start, end, windowId) : new List<QuotaSample>();
                charts.Children.Clear();
                charts.Children.Add(p.Label("剩余额度 · " + (selected == null ? "尚无窗口" : selected.Title), 13, p.Text));
                var analysis = HistoryAnalytics.Analyze(quota, start, end, now);
                if (mode != 0 && analysis.Samples > 0)
                {
                    var consumed = p.Label("观测消耗 · " + (analysis.IsLowerBound ? "至少 " : "") + analysis.Consumed.ToString("0.#") + "%", 12, p.Text);
                    consumed.Margin = new Thickness(0, 6, 0, 0); charts.Children.Add(consumed);
                    System.Windows.Automation.AutomationProperties.SetAutomationId(consumed, "ObservedConsumption");
                }
                if (!state.IsStale && analysis.ExhaustsAt.HasValue)
                {
                    var estimate = p.Label("按当前节奏预计耗尽于 " + analysis.ExhaustsAt.Value.ToLocalTime().ToString("MM/dd HH:mm"), 11, p.Muted);
                    System.Windows.Automation.AutomationProperties.SetAutomationId(estimate, "ExhaustionEstimate"); charts.Children.Add(estimate);
                }
                charts.Children.Add(new TrendChart(p, start, end, quota, null, false) { Margin = new Thickness(0, 8, 0, 6) });
                charts.Children.Add(p.Label("阴影表示超过 30 分钟未记录；虚线为显示趋势，重置基准为 100%。", 11, p.Muted));
                AddTokenChart(p, now);
                status.Text = preview ? "演示数据 · 导出和清除已禁用" : history == null ? "历史记录已关闭或数据库不可用。" :
                    account == null ? "连接账号后显示其历史记录。" : "额度采样 " + quota.Count + " 条 · 数据库 " + (history.SizeBytes / 1024.0).ToString("0.#") + " KB";
                if (!readSettings().HistoryEnabled && !preview) status.Text += " · 当前已暂停新增记录";
                if (state.HistoryError != null) status.Text += " · " + state.HistoryError;
                L.Apply(charts); status.Text = L.T(status.Text);
                lastRenderKey = key; lastRendered = DateTimeOffset.UtcNow;
            }
            catch (IOException) { charts.Children.Clear(); status.Text = L.T("历史记录无法读取，实时额度仍可使用。"); }
            catch (UnauthorizedAccessException) { charts.Children.Clear(); status.Text = L.T("没有读取历史记录的权限。"); }
            finally { rendering = false; }
        }
        private void AddTokenChart(Palette p, DateTimeOffset now)
        {
            int choice = tokenChoice;
            var tokenHeader = new DockPanel { Margin = new Thickness(0, 20, 0, 8) };
            tokenRange = p.Combo(); tokenRange.Width = 180; tokenRange.HorizontalAlignment = HorizontalAlignment.Right;
            System.Windows.Automation.AutomationProperties.SetAutomationId(tokenRange, "TokenRange");
            foreach (var label in new[] { "最近 7 天", "最近 30 天", "最近 90 天", "最近 365 天", "全部历史" }) tokenRange.Items.Add(L.T(label));
            tokenRange.SelectedIndex = choice;
            tokenRange.SelectionChanged += (s, e) => {
                if (rendering) return;
                var source = (ComboBox)s;
                if (source.SelectedIndex < 0) return;
                tokenChoice = source.SelectedIndex; RefreshData();
            };
            tokenRange.DropDownClosed += (s, e) => RefreshData();
            DockPanel.SetDock(tokenRange, Dock.Right); tokenHeader.Children.Add(tokenRange);
            var title = p.Label("Token 活动", 13, p.Text); title.VerticalAlignment = VerticalAlignment.Center; tokenHeader.Children.Add(title); charts.Children.Add(tokenHeader);
            int days = new[] { 7, 30, 90, 365, 0 }[choice];
            tokenFrom = days == 0 ? "0001-01-01" : now.UtcDateTime.Date.AddDays(1 - days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            tokenTo = now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var daily = preview ? PreviewTokens(now) : history != null && account != null ? history.ReadTokens(account, tokenFrom, tokenTo) : new List<DailyTokens>();
            daily = daily.Where(d => string.CompareOrdinal(d.Date, tokenFrom) >= 0 && string.CompareOrdinal(d.Date, tokenTo) <= 0).ToList();
            var grouped = TokenSeries.Group(daily, choice == 4 ? "Month" : choice == 3 ? "Week" : "Day");
            DateTime axisStart = days == 0 ? (grouped.Count == 0 ? now.UtcDateTime.Date : grouped.First().From.UtcDateTime) : TokenSeries.Date(tokenFrom).UtcDateTime;
            DateTime axisEnd = now.UtcDateTime.Date.AddDays(1);
            if (choice == 3) { axisStart = HistoryCalendar.WeekStart(axisStart); axisEnd = HistoryCalendar.WeekStart(now.UtcDateTime).AddDays(7); }
            if (choice == 4) axisEnd = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
            var summary = p.Label("每日 Token 活动 · 已记录 " + daily.Sum(d => (decimal)d.Tokens).ToString("N0") + " Tokens · " +
                (choice == 4 ? "按月汇总" : choice == 3 ? "按周汇总" : "按日显示"), 11, p.Muted);
            System.Windows.Automation.AutomationProperties.SetAutomationId(summary, "TokenSummary"); charts.Children.Add(summary);
            charts.Children.Add(new TrendChart(p, new DateTimeOffset(axisStart, TimeSpan.Zero), new DateTimeOffset(axisEnd, TimeSpan.Zero),
                null, null, false, grouped) { Margin = new Thickness(0, 8, 0, 6) });
            charts.Children.Add(p.Label("按服务返回的日期保存；没有返回的日期留空，不推算为零。", 11, p.Muted));
        }
        private void Export()
        {
            if (preview || history == null || account == null) return;
            var picker = new Microsoft.Win32.SaveFileDialog { Filter = L.T("CSV 文件|*.csv"), FileName = "CodexMeter-history-" + DateTime.Now.ToString("yyyyMMdd") + ".csv" };
            if (picker.ShowDialog(this) != true) return;
            try { history.ExportCsv(account, picker.FileName, start, end, tokenFrom, tokenTo); status.Text = L.T("已按额度与 Token 各自的时间范围导出原始记录。"); }
            catch (IOException) { status.Text = L.T("导出失败，请检查目标文件是否可写。"); }
            catch (UnauthorizedAccessException) { status.Text = L.T("没有写入目标文件的权限。"); }
        }
        private void Clear()
        {
            if (preview || history == null || account == null) return;
            if (L.Confirm(this, "删除当前账号在本机保存的额度与 Token 历史？其他账号记录会保留。", "清除历史记录") != MessageBoxResult.Yes) return;
            try { history.Clear(account); RefreshData(); }
            catch (IOException) { status.Text = L.T("清除失败，请检查本地数据目录。"); }
        }
        public static List<DailyTokens> PreviewTokens(DateTimeOffset now)
        {
            return Enumerable.Range(0, 450).Where(i => i % 19 != 9).Select(i => new DailyTokens {
                Date = now.UtcDateTime.Date.AddDays(i - 449).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Tokens = 12000 + (i * 7919 % 48000) }).ToList();
        }
        public static List<QuotaSample> PreviewQuota(DateTimeOffset now)
        {
            var currentStart = now.AddDays(-2.2);
            return Enumerable.Range(0, 2881).Where(i => i % 96 < 52 || i % 96 > 59).SelectMany(i => {
                var at = now.AddMinutes((i - 2880) * 15);
                var cycleStart = currentStart.AddDays(Math.Floor((at - currentStart).TotalDays / 7) * 7);
                var shortStart = now.AddHours(-1.8); shortStart = shortStart.AddHours(Math.Floor((at - shortStart).TotalHours / 5) * 5);
                return new[] {
                    new QuotaSample { At = at, WindowId = "codex:10080", Title = "每周", DurationMinutes = 10080,
                        ResetsAt = cycleStart.AddDays(7), Remaining = Math.Max(0, 100 - (at - cycleStart).TotalHours * (38 / 52.8)) },
                    new QuotaSample { At = at, WindowId = "codex:300", Title = "5 小时", DurationMinutes = 300,
                        ResetsAt = shortStart.AddHours(5), Remaining = Math.Max(0, 100 - (at - shortStart).TotalHours * (26 / 1.8)) }
                };
            }).ToList();
        }
    }
}
