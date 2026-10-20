using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CodexMeter.Core;

namespace CodexMeter.Windows
{
    internal sealed class AppearanceWindow : CompactWindow
    {
        private sealed class Binding
        {
            public Slider Slider; public TextBlock Label;
            public string Name; public Func<IndicatorAppearance, double> Get; public Action<IndicatorAppearance, double> Set;
        }
        private readonly Settings settings;
        private readonly Action<IndicatorAppearance> apply;
        private IndicatorAppearance working;
        private readonly List<Binding> bindings = new List<Binding>();
        private readonly IndicatorView indicator = new IndicatorView();
        private Slider quota, time;
        private ComboBox color;
        private CheckBox caption;
        private TextBlock message;
        private bool updating, noReset, stale;
        public AppearanceWindow(Settings settings, Action<IndicatorAppearance> apply) : base("高级外观", settings.Theme, true)
        {
            this.settings = settings.Copy(); this.apply = apply; working = (settings.Appearance ?? new IndicatorAppearance()).Bounded();
            var p = Theme; var body = Body;
            Viewport.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            Viewport.Padding = new Thickness(0);
            var previewHeader = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            previewHeader.Children.Add(p.Label("此窗口仅使用演示数据；不会修改账号数据或写入历史。", 11, p.Muted));
            var preview = new Border { Background = p.Surface, BorderBrush = p.Line, BorderThickness = new Thickness(1),
                Padding = new Thickness(10), Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(6),
                Child = indicator, HorizontalAlignment = HorizontalAlignment.Left };
            previewHeader.Children.Add(preview); Header.Children.Insert(Header.Children.Count - 1, previewHeader);
            message = p.Label("应用后请在设置中保存。", 11, p.Muted); message.Margin = new Thickness(0, 0, 0, 8); Actions.Children.Add(message);
            var applyButton = ActionButton("应用到设置", (s, e) => { this.apply(working.Bounded()); Close(); });
            applyButton.Margin = new Thickness(0, 0, 0, 6); Actions.Children.Add(applyButton);
            var files = new Grid(); files.ColumnDefinitions.Add(new ColumnDefinition()); files.ColumnDefinitions.Add(new ColumnDefinition());
            var export = ActionButton("导出外观 JSON…", (s, e) => Export()); export.Margin = new Thickness(0, 0, 3, 0); files.Children.Add(export);
            var import = ActionButton("导入外观 JSON…", (s, e) => Import()); import.Margin = new Thickness(3, 0, 0, 0);
            Grid.SetColumn(import, 1); files.Children.Add(import); Actions.Children.Add(files);
            Section("外观预设");
            var presets = new Grid(); body.Children.Add(presets);
            int presetIndex = 0;
            foreach (var preset in new[] { new[] { "默认外观", "Default" }, new[] { "紧凑", "Compact" }, new[] { "醒目", "Bold" } })
            {
                presets.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(presetIndex == 0 ? 2 : 1, GridUnitType.Star) });
                string key = preset[1];
                var button = ActionButton(preset[0], (s, e) => { working = IndicatorAppearance.Preset(key); LoadControls(); });
                button.Margin = new Thickness(0, 0, presetIndex < 2 ? 6 : 0, 0);
                Grid.SetColumn(button, presetIndex++); presets.Children.Add(button);
            }
            Section("图形尺寸");
            Add(body, p, "双环尺寸", 16, 24, a => a.RingSize, (a, v) => a.RingSize = v);
            Add(body, p, "外环粗细", 1, 4, a => a.OuterStroke, (a, v) => a.OuterStroke = v);
            Add(body, p, "内环粗细", 1, 3, a => a.InnerStroke, (a, v) => a.InnerStroke = v);
            Add(body, p, "双环间距", 2, 6, a => a.RingGap, (a, v) => a.RingGap = v);
            Add(body, p, "图形与数字间距", 0, 8, a => a.Spacing, (a, v) => a.Spacing = v);
            Add(body, p, "横条宽度", 16, 48, a => a.BarWidth, (a, v) => a.BarWidth = v);
            Section("文字与颜色");
            Add(body, p, "主字号", 10, 15, a => a.FontSize, (a, v) => a.FontSize = v);
            Add(body, p, "名称字号", 8, 11, a => a.CaptionSize, (a, v) => a.CaptionSize = v);
            caption = new CheckBox { Content = "显示 Codex 名称", Foreground = p.Text, IsChecked = working.ShowCaption, Margin = new Thickness(0, 4, 0, 10) };
            caption.Click += (s, e) => Update(); body.Children.Add(caption);
            body.Children.Add(p.Label("时间颜色", 12, p.Text));
            color = p.Combo(); color.Margin = new Thickness(0, 6, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(color, L.T("时间颜色"));
            foreach (var label in new[] { "蓝色", "青色", "紫色" }) color.Items.Add(label);
            color.SelectionChanged += (s, e) => Update(); body.Children.Add(color);
            Section("演示预览");
            var scenarios = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) }; body.Children.Add(scenarios);
            foreach (var scenario in new[] { "节奏正常", "消耗偏快", "低额度", "无重置时间", "数据过期" })
            {
                string key = scenario; var button = ActionButton(key, (s, e) => Scenario(key));
                button.Margin = new Thickness(0, 0, 6, 6); scenarios.Children.Add(button);
            }
            body.Children.Add(p.Label("演示剩余额度", 12, p.Text)); quota = DemoSlider(74); body.Children.Add(quota);
            body.Children.Add(p.Label("演示剩余时间", 12, p.Text)); time = DemoSlider(64); body.Children.Add(time);
            var historyButton = ActionButton("预览 30 天历史…", (s, e) => {
                var state = DemoState();
                state.Windows = new List<QuotaWindow> { new QuotaWindow { Id = "codex:10080", Bucket = "codex", UsedPercent = 38,
                    DurationMinutes = 10080, ResetsAt = state.UpdatedAt.Value.AddDays(4.8) } };
                new HistoryWindow(null, true, () => state, () => this.settings) { Owner = this }.Show();
            }); historyButton.Margin = new Thickness(0, 0, 0, 6); body.Children.Add(historyButton);
            var updateButton = ActionButton("预览更新状态…", (s, e) => {
                var updater = new WindowsUpdateService(() => this.settings, true, () => { }); updater.Simulate("available");
                var about = new AboutWindow(this.settings, true, updater, next => { }) { Owner = this };
                about.Closed += (closed, value) => updater.Dispose(); about.Show();
            }); updateButton.Margin = new Thickness(0); body.Children.Add(updateButton);
            LoadControls(); FinishLayout();
        }
        private Slider DemoSlider(double initial)
        {
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = initial, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 8) };
            slider.ValueChanged += (s, e) => { if (time != null && quota != null) RenderPreview(); }; return slider;
        }
        private void Add(Panel body, Palette p, string name, double min, double max, Func<IndicatorAppearance, double> get, Action<IndicatorAppearance, double> set)
        {
            var label = p.Label("", 12, p.Text); body.Children.Add(label);
            var slider = new Slider { Minimum = min, Maximum = max, Value = get(working), TickFrequency = .5, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 8) };
            System.Windows.Automation.AutomationProperties.SetName(slider, L.T(name));
            bindings.Add(new Binding { Slider = slider, Label = label, Name = name, Get = get, Set = set });
            slider.ValueChanged += (s, e) => Update(); body.Children.Add(slider);
        }
        private void LoadControls()
        {
            updating = true; working = working.Bounded();
            foreach (var b in bindings) { b.Slider.Value = b.Get(working); b.Label.Text = L.T(b.Name) + " · " + b.Get(working).ToString("0.#"); }
            caption.IsChecked = working.ShowCaption;
            color.SelectedIndex = working.TimeColor == "Cyan" ? 1 : working.TimeColor == "Purple" ? 2 : 0;
            updating = false; RenderPreview();
        }
        private void Update()
        {
            if (updating || color == null || caption == null) return;
            foreach (var b in bindings) b.Set(working, b.Slider.Value);
            working.ShowCaption = caption.IsChecked == true;
            working.TimeColor = color.SelectedIndex == 1 ? "Cyan" : color.SelectedIndex == 2 ? "Purple" : "Blue";
            LoadControls();
        }
        private UsageState DemoState()
        {
            var now = DateTimeOffset.UtcNow;
            return new UsageState { Plan = "预览模式 · 演示数据", UpdatedAt = now, IsStale = stale,
                Windows = new List<QuotaWindow> { new QuotaWindow { Id = "codex:300", Bucket = "codex", UsedPercent = 100 - quota.Value,
                    DurationMinutes = 300, ResetsAt = noReset ? null : (DateTimeOffset?)now.AddMinutes(time.Value * 3) } } };
        }
        private void RenderPreview()
        {
            if (quota == null || time == null) return;
            settings.Appearance = working.Bounded(); indicator.Render(DemoState(), settings, true);
        }
        private void Scenario(string key)
        {
            noReset = key == "无重置时间"; stale = key == "数据过期";
            quota.Value = key == "低额度" ? 12 : key == "消耗偏快" ? 42 : 74;
            time.Value = 64; RenderPreview();
        }
        private void Export()
        {
            var picker = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "CodexMeter-appearance.json" };
            if (picker.ShowDialog(this) != true) return;
            try { File.WriteAllText(picker.FileName, working.Export(), new UTF8Encoding(false)); message.Text = L.T("已导出外观参数，不包含账号数据。"); }
            catch (IOException) { message.Text = L.T("导出失败，请检查目标文件是否可写。"); }
            catch (UnauthorizedAccessException) { message.Text = L.T("没有写入目标文件的权限。"); }
        }
        private void Import()
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
            if (picker.ShowDialog(this) != true) return;
            try
            {
                if (new FileInfo(picker.FileName).Length > 16384) throw new InvalidDataException();
                working = IndicatorAppearance.Import(File.ReadAllText(picker.FileName, Encoding.UTF8));
                LoadControls(); message.Text = L.T("已载入并限制到安全范围；应用后请保存设置。");
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex.GetType().Name == "JsonException")) throw;
                message.Text = L.T("外观文件无效或无法读取，请选择本应用导出的 JSON。");
            }
        }
    }
}
