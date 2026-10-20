#if SETTINGS_SCROLL_TEST
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMeter.Core;

internal static class SettingsScrollProbe
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var assembly = Assembly.LoadFrom(args[0]);
            bool baseline = args.Length > 1 && args[1] == "--expect-trap";
            foreach (var theme in new[] { "Light", "Dark" }) Check(assembly, theme, baseline);
            if (!baseline)
                foreach (var theme in new[] { "Light", "Dark" })
                    foreach (var language in new[] { "zh-CN", "en" }) CheckAppearance(assembly, theme, language);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Check(Assembly assembly, string theme, bool baseline)
    {
        var quotas = new List<QuotaWindow>();
        for (int i = 0; i < 30; i++) quotas.Add(new QuotaWindow { Id = "fixture:" + i, Bucket = "fixture", DurationMinutes = 300 + i });
        var type = assembly.GetType("CodexMeter.Windows.SettingsWindow", true);
        var window = (Window)type.GetConstructors()[0].Invoke(new object[] {
            new Settings { Theme = theme }, quotas, new Action<Settings>(delegate { throw new Exception("Preview must not save."); }),
            null, true, null, false, null, null, null
        });
        try
        {
            window.ShowActivated = false; window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000; window.Top = -10000; window.Show(); Pump(window);
            var viewport = Find<ScrollViewer>(window, "Compact.Scroll");
            var sections = Find<ListBox>(window, "SectionOrder");
            var rows = baseline ? null : Find<ListBox>(window, "QuotaWindowOrder");
            Need(viewport != null && sections != null && viewport.ScrollableHeight > 0, "Settings must overflow.");
            var innerSections = Find<ScrollViewer>(sections, null);
            Need(innerSections != null && innerSections.ScrollableHeight == 0, "Section list must fit without internal scrolling.");
            viewport.ScrollToVerticalOffset(300); Pump(window);
            sections.SelectedIndex = 2; Pump(window);
            double before = viewport.VerticalOffset;
            Wheel((UIElement)sections.ItemContainerGenerator.ContainerFromIndex(2), -120); Pump(window);
            if (baseline)
            {
                Need(viewport.VerticalOffset == before, "Expected old list to trap the wheel.");
                Console.WriteLine("PASS " + theme + " baseline reproduces the settings wheel trap"); return;
            }
            Need(viewport.VerticalOffset > before, "Wheel over a section item must scroll the page down.");
            before = viewport.VerticalOffset; Wheel(sections, 120); Pump(window);
            Need(viewport.VerticalOffset < before, "Wheel over list whitespace must scroll the page up.");
            before = viewport.VerticalOffset; Wheel(sections, -30); Pump(window);
            Need(viewport.VerticalOffset > before, "Small wheel deltas must reach the page.");
            Need(sections.SelectedIndex == 2 && sections.Items.Count == 5, "Scrolling must preserve list selection and ordering.");
            Need(viewport.VerticalScrollBarVisibility == ScrollBarVisibility.Hidden && viewport.ComputedVerticalScrollBarVisibility == Visibility.Collapsed && viewport.Padding.Right == 0, "Settings scrollbar and reserved gutter must be hidden.");
            Console.WriteLine("PASS " + theme + " section items/whitespace scroll both ways; small deltas and selection retained; no scrollbar gutter");
            var inner = Find<ScrollViewer>(rows, null);
            Need(inner != null && inner.ScrollableHeight > 0, "Large quota list must overflow internally.");
            inner.ScrollToTop(); Pump(window);
            before = viewport.VerticalOffset;
            Wheel((UIElement)rows.ItemContainerGenerator.ContainerFromIndex(0), -120); Pump(window);
            Need(inner.VerticalOffset > 0 && viewport.VerticalOffset == before, "An overflowing list must consume its own wheel movement.");
            inner.ScrollToBottom(); Pump(window);
            Wheel(rows, -120); Pump(window);
            Need(viewport.VerticalOffset > before, "List bottom must hand downward scrolling to the page.");
            inner.ScrollToTop(); Pump(window);
            before = viewport.VerticalOffset; Wheel(rows, 120); Pump(window);
            Need(viewport.VerticalOffset < before, "List top must hand upward scrolling to the page.");
            before = viewport.VerticalOffset;
            viewport.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.PageDown) { RoutedEvent = Keyboard.KeyDownEvent });
            Pump(window); Need(viewport.VerticalOffset > before, "PageDown must remain available with the scrollbar hidden.");
            Console.WriteLine("PASS " + theme + " overflowing quota list scrolls internally, hands off at both boundaries, and retains PageDown");
        }
        finally { window.Close(); }
    }
    private static void CheckAppearance(Assembly assembly, string theme, string language)
    {
        var languageField = assembly.GetType("CodexMeter.Windows.L", true).GetField("Language");
        var previousLanguage = languageField.GetValue(null); languageField.SetValue(null, language);
        IndicatorAppearance applied = null;
        var type = assembly.GetType("CodexMeter.Windows.AppearanceWindow", true);
        var window = (Window)type.GetConstructors()[0].Invoke(new object[] {
            new Settings { Theme = theme }, new Action<IndicatorAppearance>(value => applied = value)
        });
        try
        {
            window.ShowActivated = false; window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000; window.Top = -10000; window.Show(); Pump(window);
            var viewport = Find<ScrollViewer>(window, "Compact.Scroll");
            Need(window.ActualWidth == 380 && viewport != null && viewport.ScrollableHeight > 0, "Compact appearance must retain scrollable content.");
            Need(viewport.VerticalScrollBarVisibility == ScrollBarVisibility.Hidden && viewport.ComputedVerticalScrollBarVisibility == Visibility.Collapsed && viewport.Padding.Right == 0, "Appearance must hide the scrollbar without reserving a gutter.");
            var apply = FindButton(window, TextCatalog.Transform("应用到设置", language));
            var export = FindButton(window, TextCatalog.Transform("导出外观 JSON…", language));
            var import = FindButton(window, TextCatalog.Transform("导入外观 JSON…", language));
            var preview = FindIndicator(window);
            Need(apply != null && export != null && import != null && preview != null, "Appearance actions and preview must be present.");
            double footerTop = apply.TranslatePoint(new Point(), window).Y, previewTop = preview.TranslatePoint(new Point(), window).Y;
            var slider = Find<Slider>(window, null); double value = slider.Value;
            Wheel(slider, -120); Pump(window);
            Need(viewport.VerticalOffset > 0 && slider.Value == value, "Wheel over an appearance slider must scroll without editing it.");
            double before = viewport.VerticalOffset; Wheel(viewport, 120); Pump(window);
            Need(viewport.VerticalOffset < before, "Appearance wheel must scroll up as well as down.");
            viewport.ScrollToBottom(); Pump(window);
            Need(apply.TranslatePoint(new Point(), window).Y == footerTop && preview.TranslatePoint(new Point(), window).Y == previewTop, "Appearance preview and footer must stay fixed during scrolling.");
            var last = FindButton(window, TextCatalog.Transform("预览更新状态…", language));
            Need(last != null && last.TranslatePoint(new Point(), viewport).Y + last.ActualHeight <= viewport.ActualHeight + 1, "Final preview action must be reachable.");
            before = viewport.VerticalOffset;
            viewport.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.PageUp) { RoutedEvent = Keyboard.KeyDownEvent });
            Pump(window); Need(viewport.VerticalOffset < before, "Hidden appearance scrollbar must retain PageUp.");
            window.Height = 520; Pump(window);
            foreach (var button in new[] { apply, export, import })
            {
                var position = button.TranslatePoint(new Point(), window);
                Need(position.X >= 0 && position.Y >= 0 && position.X + button.ActualWidth <= window.ActualWidth && position.Y + button.ActualHeight <= window.ActualHeight, "Footer actions must fit the compact window, including English and short work areas.");
            }
            FindButton(window, TextCatalog.Transform("醒目", language)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(window);
            Need(slider.Value == 22, "Compact appearance must preserve presets.");
            slider.Value = 24;
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Need(applied != null && applied.RingSize == 24, "Compact appearance must still apply edited values.");
            Console.WriteLine("PASS appearance " + theme + "/" + language + " compact layout, hidden scrollbar, wheel/PageUp, fixed preview/footer, reachable final action, presets and apply");
        }
        finally { window.Close(); languageField.SetValue(null, previousLanguage); }
    }
    private static Button FindButton(DependencyObject root, string title)
    {
        var button = root as Button; if (button != null && (string)button.Content == title) return button;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var result = FindButton(VisualTreeHelper.GetChild(root, i), title); if (result != null) return result; }
        return null;
    }
    private static FrameworkElement FindIndicator(DependencyObject root)
    {
        if (root.GetType().Name == "IndicatorView") return (FrameworkElement)root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var result = FindIndicator(VisualTreeHelper.GetChild(root, i)); if (result != null) return result; }
        return null;
    }
    private static void Wheel(UIElement source, int delta)
    {
        Need(source != null, "Wheel target was not realized.");
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        source.RaiseEvent(preview);
        if (!preview.Handled) source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.MouseWheelEvent });
    }
    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { }));
        window.UpdateLayout();
    }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject
    {
        var match = root as T;
        if (match != null && (id == null || AutomationProperties.GetAutomationId(root) == id)) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = Find<T>(VisualTreeHelper.GetChild(root, i), id); if (result != null) return result;
        }
        return null;
    }
    private static void Need(bool value, string message) { if (!value) throw new Exception(message); }
}
#endif
