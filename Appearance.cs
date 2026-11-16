using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Markup;
using Microsoft.Win32;

namespace NewStart;
internal static class Appearance
{
    static ResourceDictionary? sharedStyles;
    internal static void ApplySurface(Window window,Border frame,bool dark,double transparency,double blur)
    {
        bool allowed=NativeBackdrop.TransparencyAllowed();
        if(!allowed)CustomBackdrop.Hide(window);
        bool native=allowed&&CustomBackdrop.Apply(window,blur);window.Background=Brushes.Transparent;
        byte alpha=native?(byte)Math.Max(1,Math.Round(255*(1-transparency))):(byte)255;
        frame.Background=SystemParameters.HighContrast?SystemColors.WindowBrush:new SolidColorBrush(dark?Color.FromArgb(alpha,35,35,35):Color.FromArgb(alpha,243,243,243));
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    internal static bool IsDark(int mode)
    {
        if(SystemParameters.HighContrast)return false;
        if(mode!=0)return mode==2;
        try {using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");return key?.GetValue("AppsUseLightTheme") is int value&&value==0;}
        catch {return false;}
    }
    internal static void Apply(Window window,bool dark)
    {
        var foreground=SystemParameters.HighContrast?SystemColors.WindowTextBrush:new SolidColorBrush(dark?Color.FromRgb(245,245,245):Color.FromRgb(25,25,25));
        var background=SystemParameters.HighContrast?SystemColors.WindowBrush:new SolidColorBrush(dark?Color.FromRgb(32,32,32):Color.FromRgb(248,248,248));
        window.Background=background;window.Foreground=foreground;
        window.Resources["Text"]=foreground;
        window.Resources["Surface"]=new SolidColorBrush(dark?Color.FromRgb(48,48,48):Colors.White);
        window.Resources["Outline"]=new SolidColorBrush(dark?Color.FromRgb(100,100,100):Color.FromRgb(170,170,170));
        window.Resources["HoverSurface"]=new SolidColorBrush(dark?Color.FromRgb(65,65,65):Color.FromRgb(235,235,235));
        window.Resources["Accent"]=new SolidColorBrush(SystemParameters.WindowGlassColor);
        var styles=sharedStyles??=(ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="TextBlock"><Setter Property="Foreground" Value="{DynamicResource Text}"/></Style>
          <Style TargetType="CheckBox"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox"><Grid Background="Transparent"><Grid.ColumnDefinitions><ColumnDefinition Width="18"/><ColumnDefinition/></Grid.ColumnDefinitions><Border x:Name="Box" Width="18" Height="18" BorderThickness="1" BorderBrush="{DynamicResource Outline}" Background="{DynamicResource Surface}" CornerRadius="3"/><Path x:Name="Check" Visibility="Collapsed" Data="M 4,9 L 8,13 L 14,5" Stroke="White" StrokeThickness="2" Width="18" Height="18"/><ContentPresenter Grid.Column="1" Margin="10,0,0,0" VerticalAlignment="Center"/></Grid><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Box" Property="Background" Value="{DynamicResource Accent}"/><Setter TargetName="Check" Property="Visibility" Value="Visible"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Box" Property="BorderBrush" Value="{DynamicResource Text}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="Slider"><Setter Property="Height" Value="24"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider"><Grid Background="Transparent"><Border Height="3" CornerRadius="2" Background="{DynamicResource Outline}" VerticalAlignment="Center" Margin="7,0"/><Track x:Name="PART_Track"><Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="3" Background="{DynamicResource Accent}" VerticalAlignment="Center" CornerRadius="2"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width="16" Height="16"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{DynamicResource Accent}" BorderBrush="{DynamicResource Text}" BorderThickness="2" CornerRadius="8"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ScrollBar"><Setter Property="Width" Value="10"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Track x:Name="PART_Track" IsDirectionReversed="True"><Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0"/></Track.DecreaseRepeatButton><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{DynamicResource Outline}" CornerRadius="3" Margin="2"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0"/></Track.IncreaseRepeatButton></Track></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="Expander"><Setter Property="Foreground" Value="{DynamicResource Text}"/></Style>
          <Style TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Padding" Value="10,6"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="Surface" Background="{DynamicResource Surface}" BorderBrush="{DynamicResource Outline}" BorderThickness="1" CornerRadius="4" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
              <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="{DynamicResource HoverSurface}"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="DodgerBlue"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="Item" Background="{DynamicResource Surface}" Padding="10,5"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Item" Property="Background" Value="{DynamicResource HoverSurface}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Item" Property="Background" Value="{DynamicResource HoverSurface}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ComboBox"><Setter Property="Foreground" Value="{DynamicResource Text}"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid>
            <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{DynamicResource Surface}" BorderBrush="{DynamicResource Outline}" BorderThickness="1" CornerRadius="4"/></ControlTemplate></ToggleButton.Template></ToggleButton>
            <Grid Margin="10,6" IsHitTestVisible="False"><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="20"/></Grid.ColumnDefinitions><ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}" ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}"/><TextBlock Grid.Column="1" Text="⌄" HorizontalAlignment="Right"/></Grid>
            <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False"><Border Background="{DynamicResource Surface}" BorderBrush="{DynamicResource Outline}" BorderThickness="1" MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}"><ScrollViewer MaxHeight="240"><ItemsPresenter/></ScrollViewer></Border></Popup>
          </Grid></ControlTemplate></Setter.Value></Setter></Style>
        </ResourceDictionary>
        """);
        if(!window.Resources.MergedDictionaries.Contains(styles))window.Resources.MergedDictionaries.Add(styles);
        int enabled=dark?1:0;var handle=new WindowInteropHelper(window).Handle;
        if(handle!=IntPtr.Zero)DwmSetWindowAttribute(handle,20,ref enabled,sizeof(int));
    }
}
