using System.Windows;
using System.Windows.Markup;

namespace CodexMeter.Windows
{
    // Scoped to the compact preference windows; quota charts retain their own palette.
    internal static class PreferencesStyles
    {
        public static void Apply(Window window, Palette palette)
        {
            string xaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
<SolidColorBrush x:Key='Preferences.Surface' Color='$surface'/>
<SolidColorBrush x:Key='Preferences.Text' Color='$text'/>
<SolidColorBrush x:Key='Preferences.Line' Color='$line'/>
<SolidColorBrush x:Key='Preferences.Hover' Color='$hover'/>
<SolidColorBrush x:Key='Preferences.Selected' Color='$selected'/>
<SolidColorBrush x:Key='Preferences.Accent' Color='$accent'/>
<Style TargetType='ListBox'>
  <Setter Property='Padding' Value='4'/><Setter Property='BorderThickness' Value='1'/>
  <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
  <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBox'>
    <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8'>
      <ScrollViewer x:Name='PART_ScrollViewer' Padding='{TemplateBinding Padding}' Focusable='False' CanContentScroll='True'
          HorizontalScrollBarVisibility='{TemplateBinding ScrollViewer.HorizontalScrollBarVisibility}' VerticalScrollBarVisibility='{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}'>
        <ItemsPresenter/>
      </ScrollViewer>
    </Border>
  </ControlTemplate></Setter.Value></Setter>
</Style>
<Style TargetType='ListBoxItem'>
  <Setter Property='Padding' Value='9,6'/><Setter Property='Margin' Value='0,1'/>
  <Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Background' Value='Transparent'/>
  <Setter Property='BorderBrush' Value='Transparent'/><Setter Property='BorderThickness' Value='1'/>
  <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'>
    <Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='5' Padding='{TemplateBinding Padding}'>
      <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
    </Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Background' Value='{StaticResource Preferences.Hover}'/></Trigger>
      <Trigger Property='IsSelected' Value='True'><Setter TargetName='Frame' Property='Background' Value='{StaticResource Preferences.Selected}'/></Trigger>
      <Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{StaticResource Preferences.Accent}'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>
<Style x:Key='Preferences.Input' TargetType='Control'>
  <Setter Property='Background' Value='{StaticResource Preferences.Surface}'/><Setter Property='Foreground' Value='{StaticResource Preferences.Text}'/>
  <Setter Property='BorderBrush' Value='{StaticResource Preferences.Line}'/><Setter Property='BorderThickness' Value='1'/>
  <Setter Property='VerticalContentAlignment' Value='Center'/>
  <Style.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></Style.Triggers>
</Style>
<Style TargetType='TextBox' BasedOn='{StaticResource Preferences.Input}'>
  <Setter Property='CaretBrush' Value='{StaticResource Preferences.Text}'/><Setter Property='SelectionBrush' Value='{StaticResource Preferences.Accent}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
    <Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='7'>
      <ScrollViewer x:Name='PART_ContentHost' Focusable='False'/>
    </Border>
    <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{StaticResource Preferences.Accent}'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>
<Style TargetType='PasswordBox' BasedOn='{StaticResource Preferences.Input}'>
  <Setter Property='CaretBrush' Value='{StaticResource Preferences.Text}'/><Setter Property='SelectionBrush' Value='{StaticResource Preferences.Accent}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='PasswordBox'>
    <Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='7'>
      <ScrollViewer x:Name='PART_ContentHost' Focusable='False'/>
    </Border>
    <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{StaticResource Preferences.Accent}'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>
<Style TargetType='Slider'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>
    <Grid MinHeight='20'>
      <Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}'
          Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'
          Orientation='{TemplateBinding Orientation}' IsDirectionReversed='{TemplateBinding IsDirectionReversed}'>
        <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False'>
          <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='3' CornerRadius='1.5' Background='{StaticResource Preferences.Accent}' VerticalAlignment='Center'/></ControlTemplate></RepeatButton.Template>
        </RepeatButton></Track.DecreaseRepeatButton>
        <Track.Thumb><Thumb Width='13' Height='13' Background='{StaticResource Preferences.Surface}' BorderBrush='{StaticResource Preferences.Accent}' BorderThickness='2'>
          <Thumb.Template><ControlTemplate TargetType='Thumb'><Border CornerRadius='7' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'/></ControlTemplate></Thumb.Template>
        </Thumb></Track.Thumb>
        <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' Focusable='False'>
          <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='3' CornerRadius='1.5' Background='{StaticResource Preferences.Line}' VerticalAlignment='Center'/></ControlTemplate></RepeatButton.Template>
        </RepeatButton></Track.IncreaseRepeatButton>
      </Track>
    </Grid>
    <ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>
</ResourceDictionary>";
            xaml = xaml.Replace("$surface", palette.Surface.ToString()).Replace("$text", palette.Text.ToString())
                .Replace("$line", palette.Line.ToString()).Replace("$accent", palette.Accent.ToString())
                .Replace("$hover", palette.Dark ? "#3B3E44" : "#E0E4E9")
                .Replace("$selected", palette.Dark ? "#284665" : "#DCE9F7");
            window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
        }
    }
}
