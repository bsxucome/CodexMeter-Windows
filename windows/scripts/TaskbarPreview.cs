using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexMeter.Core;

// Documentation artwork only: real indicator controls on a synthetic taskbar.
// No desktop capture, production preferences, accounts, CLI or shell access.
public static class TaskbarPreview
{
    private static readonly Brush Ink = Brush("#20262E"), Muted = Brush("#657180"), Line = Brush("#DEE3EA");
    private static Canvas canvas;
    private static Assembly desktop;
    private static UsageState demo;
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 2) return 2;
        desktop = Assembly.LoadFrom(args[0]);
        desktop.GetType("CodexMeter.Windows.L", true).GetField("Language").SetValue(null, "zh-CN");
        demo = new UsageState { Plan = "Demo", UpdatedAt = DateTimeOffset.UtcNow,
            Windows = new List<QuotaWindow> { new QuotaWindow { Id = "codex:300", Bucket = "codex",
                UsedPercent = 26, DurationMinutes = 300, ResetsAt = DateTimeOffset.UtcNow.AddMinutes(192) } } };
        canvas = new Canvas { Width = 1120, Height = 520, Background = Brush("#F6F7F9"), UseLayoutRounding = true };
        Text(40, 28, "任务栏双环，额度一眼可见", 26, Ink, true);
        Text(40, 68, "Taskbar quota at a glance", 13, Muted, false);
        Box(958, 30, 122, 28, "#E7F1FF", null, 14);
        Text(974, 36, "演示数据 · DEMO", 11, Brush("#1764B0"), true);

        Box(40, 110, 1040, 184, "#FFFFFF", Line, 16);
        Text(64, 126, "双环与百分比 / RINGS", 12, Muted, true);
        Box(64, 158, 992, 62, "#F1F3F6", Line, 10);
        for (int row = 0; row < 2; row++) for (int column = 0; column < 2; column++)
            Box(86 + column * 15, 175 + row * 15, 13, 13, "#0878D4", null, 1);
        Box(130, 166, 112, 46, "#FFFFFF", Line, 6);
        Indicator("Rings", 146, 169, 1.25);
        Box(268, 178, 28, 24, "#E3B34E", null, 4);
        Box(268, 174, 14, 7, "#E3B34E", null, 2);
        Box(316, 175, 28, 28, "#414B58", null, 5);
        Text(320, 178, ">_", 13, Brushes.White, true);
        Text(852, 181, "Windows 任务栏示意", 12, Muted, false);
        Dot(64, 251, "#2FA46D"); Text(80, 241, "外环：剩余额度 74%", 14, Ink, false);
        Dot(360, 251, "#0A84FF"); Text(376, 241, "内环：剩余时间 64%", 14, Ink, false);
        Text(852, 243, "点击双环展开详情", 12, Muted, false);

        Text(40, 322, "更多显示样式 / OTHER STYLES", 12, Muted, true);
        string[] styles = { "Horizontal", "Stacked", "Percentage", "Progress" };
        string[] labels = { "并排横条 / Horizontal", "上下进度条 / Stacked", "纯百分比 / Percentage", "纯进度条 / Progress" };
        for (int i = 0; i < styles.Length; i++)
        {
            double left = 40 + i * 266;
            Box(left, 352, 242, 100, "#FFFFFF", Line, 12);
            Text(left + 18, 367, labels[i], 12, Ink, true);
            Indicator(styles[i], left + 20, 402, 1.25);
        }
        Text(40, 482, "示意布局 · 全部为固定演示数据 · 不含账号、桌面或本地路径", 11, Muted, false);
        canvas.Measure(new Size(1120, 520)); canvas.Arrange(new Rect(0, 0, 1120, 520)); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap(2240, 1040, 192, 192, PixelFormats.Pbgra32); bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(output));
        using (var stream = File.Create(output)) encoder.Save(stream);
        Console.WriteLine("Saved sanitized taskbar demo (74% quota, 64% time; five real indicator styles).");
        return 0;
    }
    private static void Indicator(string style, double x, double y, double scale)
    {
        var settings = new Settings { Theme = "Light", IndicatorStyle = style };
        var control = (FrameworkElement)Activator.CreateInstance(desktop.GetType("CodexMeter.Windows.IndicatorView", true));
        control.GetType().GetMethod("Render").Invoke(control, new object[] { demo, settings, true, true });
        control.LayoutTransform = new ScaleTransform(scale, scale); Place(control, x, y);
    }
    private static void Text(double x, double y, string value, double size, Brush color, bool bold)
    {
        Place(new TextBlock { Text = value, FontSize = size, Foreground = color, FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal }, x, y);
    }
    private static void Box(double x, double y, double width, double height, string fill, Brush border, double radius)
    {
        Place(new Border { Width = width, Height = height, Background = Brush(fill), BorderBrush = border,
            BorderThickness = new Thickness(border == null ? 0 : 1), CornerRadius = new CornerRadius(radius) }, x, y);
    }
    private static void Dot(double x, double y, string color)
    { Place(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Brush(color) }, x, y); }
    private static void Place(FrameworkElement element, double x, double y)
    { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element); }
    private static Brush Brush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
}
