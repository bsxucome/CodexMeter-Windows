using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using Microsoft.Win32;

namespace CodexMeter.Windows
{
    internal sealed class Palette
    {
        public Brush Background, Surface, Text, Muted, Line, Accent;
        public bool Dark;
        public static Palette For(string theme)
        {
            bool dark = theme == "Dark";
            if (theme == "System")
            {
                try
                {
                    var light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                        "AppsUseLightTheme", 1);
                    dark = Convert.ToInt32(light) == 0;
                }
                catch (System.Security.SecurityException) { }
            }
            return new Palette {
                Dark = dark, Background = Brush(dark ? "#292929" : "#F4F4F4"),
                Surface = Brush(dark ? "#333333" : "#FFFFFF"), Text = Brush(dark ? "#F2F2F2" : "#252525"),
                Muted = Brush(dark ? "#ACACAC" : "#777777"), Line = Brush(dark ? "#444444" : "#DCDCDC"),
                Accent = Brush("#0A84FF")
            };
        }
        public static Palette ForTaskbar(string appTheme, bool preview)
        {
            if (preview) return For(appTheme);
            bool dark = true;
            try
            {
                dark = Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "SystemUsesLightTheme", 0)) == 0;
            }
            catch (System.Security.SecurityException) { }
            var result = For(dark ? "Dark" : "Light");
            result.Background = Brush(dark ? "#202020" : "#EEEEEE");
            return result;
        }
        public static Brush QuotaColor(CodexMeter.Core.Attention state, bool dark)
        { return Brush(state == CodexMeter.Core.Attention.Critical ? "#F05C56" : state == CodexMeter.Core.Attention.Warning ? (dark ? "#FFC14D" : "#B78015") : "#32A66A"); }
        public static SolidColorBrush Brush(string color)
        { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        public static Brush TimeBrush(string choice)
        { return Brush(choice == "Cyan" ? "#00A7B5" : choice == "Purple" ? "#9A6CEE" : "#0A84FF"); }
        public TextBlock Label(string text, double size, Brush color)
        {
            return new TextBlock { Text = L.T(text), FontSize = size, Foreground = color,
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), TextWrapping = TextWrapping.Wrap };
        }
        public ComboBox Combo()
        {
            var combo = new ComboBox { Foreground = Text, Background = Surface, BorderBrush = Line, FontSize = 12,
                Padding = new Thickness(8, 6, 8, 6) };
            combo.Template = (ControlTemplate)XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ComboBox'>" +
                "<Grid><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='5'/>" +
                "<ContentPresenter Margin='10,6,28,6' VerticalAlignment='Center' IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/>" +
                "<ToggleButton Focusable='False' Foreground='{TemplateBinding Foreground}' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>" +
                "<ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='Transparent'><Path HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,10,0' Stroke='{TemplateBinding Foreground}' StrokeThickness='1.2' Data='M0,0 L4,4 L8,0'/></Border></ControlTemplate></ToggleButton.Template></ToggleButton>" +
                "<Popup Name='PART_Popup' Placement='Bottom' AllowsTransparency='True' IsOpen='{TemplateBinding IsDropDownOpen}' Focusable='False'>" +
                "<Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='5' Padding='3' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'>" +
                "<ScrollViewer MaxHeight='280' CanContentScroll='True'><ItemsPresenter/></ScrollViewer></Border></Popup></Grid></ControlTemplate>");
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Surface));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
            style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(
                "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ComboBoxItem'>" +
                "<Border Background='{TemplateBinding Background}' CornerRadius='4' Padding='{TemplateBinding Padding}'>" +
                "<ContentPresenter VerticalAlignment='Center'/></Border></ControlTemplate>")));
            var highlighted = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            highlighted.Setters.Add(new Setter(Control.BackgroundProperty, Line)); style.Triggers.Add(highlighted);
            var selected = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, Brush(Dark ? "#284665" : "#DCE9F7"))); style.Triggers.Add(selected);
            combo.ItemContainerStyle = style; return combo;
        }
        public Button Button(string text, RoutedEventHandler click)
        {
            var button = new Button {
                Content = L.T(text), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0),
                Background = Surface, Foreground = Text, BorderBrush = Line, BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand, FontSize = 13, MinHeight = 34
            };
            var frame = new FrameworkElementFactory(typeof(Border));
            frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            frame.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            frame.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = frame };
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45)); template.Triggers.Add(disabled);
            var hovered = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hovered.Setters.Add(new Setter(UIElement.OpacityProperty, 0.8)); template.Triggers.Add(hovered);
            button.Template = template;
            button.Click += click;
            return button;
        }
    }
}
