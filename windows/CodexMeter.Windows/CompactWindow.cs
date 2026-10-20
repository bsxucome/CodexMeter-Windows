using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;

namespace CodexMeter.Windows
{
    internal class CompactWindow : Window
    {
        protected readonly Palette Theme;
        protected readonly StackPanel Header = new StackPanel();
        protected readonly StackPanel Body = new StackPanel();
        protected readonly StackPanel Actions = new StackPanel();
        protected readonly ScrollViewer Viewport;
        public CompactWindow(string title, string theme, bool preview)
        {
            Theme = Palette.For(theme); Title = "CodexMeter · " + L.T(title);
            Width = 380; Height = Math.Min(700, SystemParameters.WorkArea.Height - 32); MinHeight = 360;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Theme.Background;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 12; Foreground = Theme.Text;
            UseLayoutRounding = true; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            var shell = new Border { Background = Theme.Background, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16) };
            var layout = new Grid(); Content = shell; shell.Child = layout;
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = Header;
            var heading = new DockPanel();
            var close = Theme.Button("×", (s, e) => Close()); close.Width = 26; close.MinHeight = 24; close.Padding = new Thickness(0); close.Margin = new Thickness(0);
            close.Background = Brushes.Transparent; close.BorderThickness = new Thickness(0);
            System.Windows.Automation.AutomationProperties.SetName(close, "关闭"); DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
            var titles = new StackPanel(); var name = Theme.Label("CodexMeter", 14, Theme.Text); name.FontWeight = FontWeights.SemiBold;
            titles.Children.Add(name); titles.Children.Add(Theme.Label(title, 11, Theme.Muted)); heading.Children.Add(titles); header.Children.Add(heading);
            header.Children.Add(Line(14, 12)); layout.Children.Add(header);
            heading.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource is TextBlock) DragMove(); };
            Viewport = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, ClipToBounds = true, Padding = new Thickness(0, 0, 4, 0) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(Viewport, "Compact.Scroll");
            Resources[SystemParameters.VerticalScrollBarWidthKey] = 6.0;
            Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = XamlReader.Parse(
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ScrollBar'><Setter Property='Width' Value='6'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Grid Width='6' HorizontalAlignment='Center' Background='Transparent'><Track Name='PART_Track' Orientation='{TemplateBinding Orientation}' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}' IsDirectionReversed='True'><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Width='4' Background='#777777' Opacity='0.45' CornerRadius='2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>");
            Grid.SetRow(Viewport, 1); layout.Children.Add(Viewport);
            Actions.Children.Add(Line(12, 10)); Grid.SetRow(Actions, 2); layout.Children.Add(Actions);
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            SourceInitialized += (s, e) => { if (!preview) PopupMaterial.Apply(this, Theme.Dark); };
            Loaded += (s, e) => { UpdateLayout(); Height = Math.Max(MinHeight, Math.Min(Height, Viewport.ExtentHeight + ActualHeight - Viewport.ActualHeight + 2)); };
        }
        protected Border Line(double before, double after) { return new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, before, 0, after) }; }
        protected void Section(string title) { if (Body.Children.Count > 0) Body.Children.Add(Line(12, 12)); var label = Theme.Label(title, 13, Theme.Text); label.FontWeight = FontWeights.SemiBold; label.Margin = new Thickness(0, 0, 0, 6); Body.Children.Add(label); }
        protected Button ActionButton(string title, RoutedEventHandler action)
        { var button = Theme.Button(title, action); button.MinHeight = 28; button.FontSize = 12; button.Padding = new Thickness(8, 4, 8, 4); return button; }
        protected void FinishLayout()
        {
            CompactControls(Body); CompactControls(Actions); L.Apply(this);
        }
        private static void CompactControls(DependencyObject parent)
        {
            var control = parent as Control;
            if (control != null) { if (control.FontSize >= 12) control.FontSize = 12; if (control.MinHeight >= 34) control.MinHeight = 28; }
            var text = parent as TextBlock;
            if (text != null && text.FontSize > 14) { text.FontSize = 13; text.FontWeight = FontWeights.SemiBold; }
            var button = parent as Button; if (button != null) button.Padding = new Thickness(8, 4, 8, 4);
            var element = parent as FrameworkElement;
            if (element != null) { var margin = element.Margin; if (margin.Bottom >= 18) margin.Bottom = 12; element.Margin = margin; }
            foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>().ToArray()) CompactControls(child);
        }
    }
}
