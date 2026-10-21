using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed class SettingsWindow : CompactWindow
    {
        private AppearanceWindow appearanceWindow;
        private sealed class Choice
        {
            public string Key { get; set; }
            public string Title { get; set; }
            public override string ToString() { return L.T(Title); }
        }
        public SettingsWindow(Settings current, IList<QuotaWindow> windows, Action<Settings> save,
            Action testNotification = null, bool preview = false, string startupDescription = null, bool startupAvailable = true,
            Action remoteSettings = null, Action dataSettings = null, Action about = null) : base("设置", current.Theme, preview)
        {
            var p = Theme; var root = Body; var actions = Actions;
            Viewport.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            Viewport.Padding = new Thickness(0);
            Section("连接与账号");
            root.Children.Add(p.Label("Codex CLI 路径", 13, p.Text));
            var path = new TextBox { Text = current.CliPath, Margin = new Thickness(0, 8, 0, 8), Padding = new Thickness(8),
                Background = p.Surface, Foreground = p.Text, BorderBrush = p.Line };
            root.Children.Add(path);
            root.Children.Add(p.Button("选择文件…", (s, e) => {
                var picker = new Microsoft.Win32.OpenFileDialog { Title = L.T("选择 Codex CLI"),
                    Filter = L.T("Codex 启动入口|codex.exe;codex.cmd;codex.ps1;codex.js|所有文件|*.*") };
                if (picker.ShowDialog(this) == true) path.Text = picker.FileName;
            }));
            var hint = p.Label("留空自动查找。登录由 Codex CLI 管理，本应用不读取登录凭据。", 12, p.Muted);
            hint.Margin = new Thickness(0, 8, 0, 18); root.Children.Add(hint);
            Section("任务栏显示");
            var taskbar = new CheckBox { Content = "在任务栏常驻显示双环和百分比", IsChecked = current.TaskbarIndicator,
                Foreground = p.Text, Margin = new Thickness(0, 0, 0, 6), FontSize = 13 };
            root.Children.Add(taskbar);
            var placement = p.Label("点击展开额度，拖动调整位置。右键可重置位置。", 11, p.Muted);
            placement.Margin = new Thickness(0, 0, 0, 18); root.Children.Add(placement);
            root.Children.Add(p.Label("任务栏位置", 13, p.Text));
            var docking = p.Combo(); docking.Margin = new Thickness(0, 8, 0, 18);
            System.Windows.Automation.AutomationProperties.SetName(docking, "任务栏位置");
            var dockValues = new[] { "Auto", "StartLeft", "StartRight", "Custom" };
            foreach (var label in new[] { "自动靠右", "紧靠 Windows 开始按钮左侧", "紧靠 Windows 开始按钮右侧", "自定义拖动位置" }) docking.Items.Add(label);
            docking.SelectedIndex = Math.Max(0, Array.IndexOf(dockValues, current.TaskbarDock));
            root.Children.Add(docking);
            root.Children.Add(p.Label("任务栏显示样式", 13, p.Text));
            var styles = p.Combo(); styles.Margin = new Thickness(0, 8, 0, 18);
            var styleValues = new[] { "Rings", "Horizontal", "Stacked", "Percentage", "Progress" };
            foreach (var label in new[] { "双环与百分比", "双横条与百分比", "上下进度条与百分比", "仅百分比", "仅进度条" }) styles.Items.Add(label);
            styles.SelectedIndex = Math.Max(0, Array.IndexOf(styleValues, current.IndicatorStyle));
            System.Windows.Automation.AutomationProperties.SetName(styles, "任务栏显示样式");
            System.Windows.Automation.AutomationProperties.SetAutomationId(styles, "IndicatorStyle");
            root.Children.Add(styles);
            root.Children.Add(p.Button("高级外观…", (s, e) => {
                if (appearanceWindow != null) { appearanceWindow.Activate(); return; }
                var previewSettings = current.Copy(); previewSettings.IndicatorStyle = styleValues[styles.SelectedIndex];
                appearanceWindow = new AppearanceWindow(previewSettings, value => current.Appearance = value.Copy()) { Owner = this };
                appearanceWindow.Closed += (closedSender, closedArgs) => appearanceWindow = null;
                appearanceWindow.Show();
            }));
            root.Children.Add(p.Label("任务栏与托盘代表的额度窗口", 13, p.Text));
            var selection = p.Combo(); selection.Margin = new Thickness(0, 8, 0, 18);
            selection.Items.Add(new Choice { Key = "", Title = "默认（优先标准周额度）" });
            bool found = false;
            foreach (var item in windows)
            {
                selection.Items.Add(new Choice { Key = item.Id, Title = item.Title });
                if (item.Id == current.SelectedWindow) { selection.SelectedIndex = selection.Items.Count - 1; found = true; }
            }
            if (!found && !string.IsNullOrEmpty(current.SelectedWindow))
            {
                selection.Items.Add(new Choice { Key = current.SelectedWindow, Title = "保留之前选择（当前不可用）" });
                selection.SelectedIndex = selection.Items.Count - 1;
            }
            if (selection.SelectedIndex < 0) selection.SelectedIndex = 0;
            root.Children.Add(selection);
            Section("外观与语言");
            root.Children.Add(p.Label("外观", 13, p.Text));
            var theme = p.Combo(); theme.Margin = new Thickness(0, 8, 0, 18);
            theme.Items.Add("跟随系统"); theme.Items.Add("浅色"); theme.Items.Add("深色");
            theme.SelectedIndex = current.Theme == "Dark" ? 2 : current.Theme == "Light" ? 1 : 0;
            root.Children.Add(theme);
            root.Children.Add(p.Label("语言", 13, p.Text));
            var language = p.Combo(); language.Margin = new Thickness(0, 8, 0, 18);
            foreach (var label in new[] { "简体中文", "繁體中文", "English" }) language.Items.Add(label);
            language.SelectedIndex = current.Language == "en" ? 2 : current.Language == "zh-TW" ? 1 : 0;
            System.Windows.Automation.AutomationProperties.SetName(language, "语言"); root.Children.Add(language);
            System.Windows.Automation.AutomationProperties.SetAutomationId(language, "Language");
            Section("弹窗内容");
            var showTokens = Toggle(root, p, "Token 活动与图表", current.ShowTokens);
            var showCredits = Toggle(root, p, "额度余额", current.ShowCredits);
            var showResets = Toggle(root, p, "重置机会", current.ShowResetCredits);
            var showWeekly = Toggle(root, p, "每周额度小图", current.ShowWeeklyHistory);
            var showTime = Toggle(root, p, "剩余时间进度条", current.ShowTime);
            var showPace = Toggle(root, p, "消耗节奏提示", current.ShowPace);
            var showReset = Toggle(root, p, "重置倒计时与日期", current.ShowReset);
            root.Children.Add(p.Label("弹窗区域顺序", 13, p.Text));
            var sections = new ListBox { Background = p.Surface, Foreground = p.Text, BorderBrush = p.Line,
                Margin = new Thickness(0, 8, 0, 8), MaxHeight = 190 };
            System.Windows.Automation.AutomationProperties.SetAutomationId(sections, "SectionOrder");
            ScrollViewer.SetVerticalScrollBarVisibility(sections, ScrollBarVisibility.Hidden);
            sections.PreviewMouseWheel += ForwardListWheel;
            foreach (var key in Settings.ValidSections(current.SectionOrder))
                sections.Items.Add(new ListBoxItem { Content = L.T(SectionTitle(key)), Tag = key });
            root.Children.Add(sections);
            var sectionActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
            sectionActions.Children.Add(p.Button("区域上移", (s, e) => Move(sections, -1)));
            sectionActions.Children.Add(p.Button("区域下移", (s, e) => Move(sections, 1))); root.Children.Add(sectionActions);
            root.Children.Add(p.Label("额度窗口显示与顺序", 13, p.Text));
            var rows = new ListBox { Background = p.Surface, Foreground = p.Text, BorderBrush = p.Line,
                Margin = new Thickness(0, 8, 0, 8), MaxHeight = 170, MinHeight = 46 };
            System.Windows.Automation.AutomationProperties.SetAutomationId(rows, "QuotaWindowOrder");
            ScrollViewer.SetVerticalScrollBarVisibility(rows, ScrollBarVisibility.Hidden);
            rows.PreviewMouseWheel += ForwardListWheel;
            var ordered = (current.WindowOrder ?? new string[0]).Concat(windows.Select(w => w.Id)).Concat(current.HiddenWindows ?? new string[0]).Distinct();
            foreach (var id in ordered)
            {
                var foundWindow = windows.FirstOrDefault(w => w.Id == id);
                rows.Items.Add(new CheckBox { Content = foundWindow == null ? id : foundWindow.Title, Tag = id,
                    IsChecked = foundWindow == null ? !(current.HiddenWindows ?? new string[0]).Contains(id) : current.IsWindowVisible(foundWindow), Foreground = p.Text, Margin = new Thickness(0) });
            }
            root.Children.Add(rows);
            var orderButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
            orderButtons.Children.Add(p.Button("上移", (s, e) => Move(rows, -1)));
            orderButtons.Children.Add(p.Button("下移", (s, e) => Move(rows, 1))); root.Children.Add(orderButtons);
            Section("历史与数据");
            var history = new CheckBox { Content = "在本机记录额度历史和 Token 活动", IsChecked = current.HistoryEnabled,
                Foreground = p.Text, Margin = new Thickness(0, 0, 0, 8), FontSize = 13 };
            root.Children.Add(history);
            root.Children.Add(p.Label("只保存额度、日期和加盐账号标识，不保存邮箱或登录凭据。关闭后停止新增，已有记录可在历史窗口清除。", 11, p.Muted));
            root.Children.Add(p.Label("历史保留期限（适用于全部本地账号）", 13, p.Text));
            var retention = p.Combo(); retention.Margin = new Thickness(0, 8, 0, 18);
            var retentionValues = new[] { 0, 7, 30, 90, 365 };
            foreach (var label in new[] { "永久", "7 天", "30 天", "90 天", "365 天" }) retention.Items.Add(label);
            retention.SelectedIndex = Math.Max(0, Array.IndexOf(retentionValues, current.HistoryRetentionDays));
            root.Children.Add(retention);
            if (dataSettings != null) root.Children.Add(ActionButton("备份、恢复与账号管理…", (s, e) => dataSettings()));
            Section("通知与启动");
            var notifications = Toggle(root, p, "启用本地通知", current.Notifications);
            var lowQuota = Toggle(root, p, "低额度提醒", current.NotifyLowQuota);
            var overPace = Toggle(root, p, "消耗偏快提醒", current.NotifyOverPace);
            var thresholdLabel = p.Label("", 12, p.Muted); root.Children.Add(thresholdLabel);
            var threshold = new Slider { Minimum = 1, Maximum = 99, Value = current.NotificationThreshold,
                TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 8, 0, 12) };
            System.Windows.Automation.AutomationProperties.SetName(threshold, "低额度阈值");
            threshold.ValueChanged += (s, e) => thresholdLabel.Text = L.T("低额度阈值：") + threshold.Value.ToString("0") + "%";
            thresholdLabel.Text = L.T("低额度阈值：") + threshold.Value.ToString("0") + "%"; root.Children.Add(threshold);
            root.Children.Add(p.Label("同一窗口同类提醒的冷却时间", 12, p.Muted));
            var cooldown = p.Combo(); cooldown.Margin = new Thickness(0, 8, 0, 12);
            System.Windows.Automation.AutomationProperties.SetAutomationId(cooldown, "NotificationCooldown");
            var cooldownValues = new[] { 1, 5, 15, 30, 60, 120 };
            foreach (var minutes in cooldownValues) cooldown.Items.Add(new Choice { Key = minutes.ToString(), Title = minutes + " 分钟" });
            int savedCooldown = Array.IndexOf(cooldownValues, current.NotificationCooldownMinutes);
            if (savedCooldown < 0) cooldown.Items.Add(new Choice { Key = current.NotificationCooldownMinutes.ToString(), Title = current.NotificationCooldownMinutes + " 分钟" });
            cooldown.SelectedIndex = savedCooldown < 0 ? cooldown.Items.Count - 1 : savedCooldown; root.Children.Add(cooldown);
            var test = p.Button("测试通知", (s, e) => { if (testNotification != null) testNotification(); });
            test.IsEnabled = !preview && testNotification != null; root.Children.Add(test);
            root.Children.Add(p.Label("仅新鲜数据触发提醒；Windows 勿扰或通知设置可能阻止显示。", 11, p.Muted));
            if (remoteSettings != null) root.Children.Add(ActionButton("远程补充提醒…", (s, e) => remoteSettings()));
            var startup = Toggle(root, p, "登录 Windows 时启动", current.LaunchAtLogin); startup.IsEnabled = startupAvailable;
            root.Children.Add(p.Label(startupDescription ?? "", 11, p.Muted));
            var startupSettings = p.Button("打开 Windows 启动应用设置", (s, e) => {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) { }
            });
            startupSettings.IsEnabled = !preview; root.Children.Add(startupSettings);
            Section("关于与更新");
            root.Children.Add(p.Label("Windows " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3), 11, p.Muted));
            if (about != null) root.Children.Add(ActionButton("关于与检查更新…", (s, e) => about()));
            var error = p.Label("", 12, p.Accent); actions.Children.Add(error);
            actions.Children.Add(p.Button("保存设置", (s, e) => {
                var selectedPath = path.Text.Trim().Trim('"');
                if (selectedPath.Length > 0 && !File.Exists(Environment.ExpandEnvironmentVariables(selectedPath)))
                { error.Text = L.T("文件不存在，请重新选择。"); return; }
                try
                {
                    if (selectedPath.Length > 0) new CliLocator().Locate(selectedPath);
                    var next = current.Copy();
                    next.CliPath = selectedPath; next.SelectedWindow = ((Choice)selection.SelectedItem).Key;
                    next.Theme = theme.SelectedIndex == 2 ? "Dark" : theme.SelectedIndex == 1 ? "Light" : "System";
                    next.TaskbarIndicator = taskbar.IsChecked == true; next.TaskbarDock = dockValues[docking.SelectedIndex];
                    next.HistoryEnabled = history.IsChecked == true;
                    next.HistoryRetentionDays = retentionValues[retention.SelectedIndex];
                    next.IndicatorStyle = styleValues[styles.SelectedIndex];
                    next.Language = language.SelectedIndex == 2 ? "en" : language.SelectedIndex == 1 ? "zh-TW" : "zh-CN";
                    next.ShowTokens = showTokens.IsChecked == true; next.ShowTime = showTime.IsChecked == true;
                    next.ShowWeeklyHistory = showWeekly.IsChecked == true;
                    next.ShowCredits = showCredits.IsChecked == true; next.ShowResetCredits = showResets.IsChecked == true;
                    next.SectionOrder = sections.Items.Cast<ListBoxItem>().Select(item => (string)item.Tag).ToArray();
                    next.Notifications = notifications.IsChecked == true; next.NotifyLowQuota = lowQuota.IsChecked == true;
                    next.NotifyOverPace = overPace.IsChecked == true; next.NotificationThreshold = (int)threshold.Value;
                    next.NotificationCooldownMinutes = int.Parse(((Choice)cooldown.SelectedItem).Key);
                    next.LaunchAtLogin = startup.IsChecked == true;
                    next.ShowPace = showPace.IsChecked == true; next.ShowReset = showReset.IsChecked == true;
                    next.WindowOrder = rows.Items.Cast<CheckBox>().Select(c => (string)c.Tag).ToArray();
                    next.HiddenWindows = rows.Items.Cast<CheckBox>().Where(c => c.IsChecked != true).Select(c => (string)c.Tag).ToArray();
                    if (next.HistoryRetentionDays != 0 && (current.HistoryRetentionDays == 0 || next.HistoryRetentionDays < current.HistoryRetentionDays) &&
                        L.Confirm(this, "缩短保留期限会删除全部本地账号中超过期限的历史记录，是否继续？", "历史保留期限") != MessageBoxResult.Yes) return;
                    save(next);
                    Close();
                }
                catch (InvalidOperationException) { error.Text = L.T("无法识别所选 CLI，请选择 codex.exe 或 npm 的 codex.cmd。"); }
                catch (IOException) { error.Text = L.T("设置无法保存，请检查本地数据目录。"); }
                catch (UnauthorizedAccessException) { error.Text = L.T("没有权限保存设置，请检查本地数据目录。"); }
                catch (System.Security.SecurityException) { error.Text = L.T("没有权限更新 Windows 登录启动设置。"); }
            }));
            FinishLayout();
        }
        public static string SectionTitle(string key)
        { return key == "Quota" ? "额度详情" : key == "Credits" ? "额度余额" : key == "Resets" ? "重置机会" : key == "History" ? "每周额度历史" : "Token 活动"; }
        private static CheckBox Toggle(Panel parent, Palette p, string label, bool value)
        {
            var toggle = new CheckBox { Content = label, IsChecked = value, Foreground = p.Text, FontSize = 12, Margin = new Thickness(0, 8, 0, 8) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(toggle, label);
            parent.Children.Add(toggle); return toggle;
        }
        private static void Move(ListBox rows, int direction)
        {
            int index = rows.SelectedIndex;
            if (index < 0 || index + direction < 0 || index + direction >= rows.Items.Count) return;
            object selected = rows.Items[index]; rows.Items.RemoveAt(index); rows.Items.Insert(index + direction, selected); rows.SelectedItem = selected;
        }
        private void ForwardListWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || e.Delta == 0) return;
            var inner = FindScrollViewer((DependencyObject)sender);
            // Let overflowing lists scroll normally. At either boundary (or
            // when all items fit), avoid trapping the wheel inside the list.
            if (inner != null && inner.ScrollableHeight > 0 &&
                (e.Delta > 0 ? inner.VerticalOffset > 0.01 : inner.VerticalOffset < inner.ScrollableHeight - 0.01)) return;
            e.Handled = true;
            Viewport.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) {
                RoutedEvent = Mouse.MouseWheelEvent, Source = Viewport
            });
        }
        private static ScrollViewer FindScrollViewer(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var result = child as ScrollViewer ?? FindScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
