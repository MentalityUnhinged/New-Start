using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NewStart;
internal static class BackgroundSettings
{
    internal static FrameworkElement Create(Preferences prefs,bool appBar,Func<string?> monitor,Action apply)
    {
        var root=new StackPanel{Margin=new Thickness(0,8,0,20)};bool syncing=false;
        root.Children.Add(new TextBlock{Text=appBar?"AppBar background":"Dock background",FontWeight=FontWeights.SemiBold,FontSize=15,Margin=new Thickness(0,8,0,8)});
        var sources=new ComboBox{ItemsSource=new[]{"System","Custom","Wallpaper automatic","Wallpaper palette"},SelectedIndex=appBar?prefs.AppBarColourSource:prefs.DockColourSource,Margin=new Thickness(0,0,0,8)};root.Children.Add(sources);
        var strategy=new ComboBox{ItemsSource=new[]{"Dominant wallpaper colour","Accent wallpaper colour"},SelectedIndex=appBar?prefs.AppBarWallpaperStrategy:prefs.DockWallpaperStrategy,Margin=new Thickness(0,0,0,8)};root.Children.Add(strategy);
        var previewText=new TextBlock{Text="Background preview",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        var preview=new Border{Height=42,CornerRadius=new CornerRadius(6),Child=previewText,Margin=new Thickness(0,0,0,8)};root.Children.Add(preview);
        var hex=new TextBox{Text=appBar?prefs.AppBarColour:prefs.DockColour,MaxLength=7,Margin=new Thickness(0,0,0,4)};System.Windows.Automation.AutomationProperties.SetName(hex,(appBar?"AppBar":"Dock")+" colour HEX");root.Children.Add(hex);
        hex.SetResourceReference(Control.BackgroundProperty,"Surface");hex.SetResourceReference(Control.ForegroundProperty,"Text");hex.SetResourceReference(Control.BorderBrushProperty,"Outline");hex.Padding=new Thickness(8,5,8,5);hex.FontSize=12;
        var error=new TextBlock{FontSize=11,TextWrapping=TextWrapping.Wrap};root.Children.Add(error);
        var channels=new Slider[3];var numbers=new TextBlock[3];var percent=new TextBlock{FontSize=11};var blurText=new TextBlock{FontSize=11};
        for(int index=0;index<3;index++)
        {
            int channel=index;var row=new Grid{Margin=new Thickness(0,3,0,3)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(50)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(34)});
            var label=new TextBlock{Text=new[]{"Red","Green","Blue"}[index],VerticalAlignment=VerticalAlignment.Center};row.Children.Add(label);
            var slider=new Slider{Minimum=0,Maximum=255,TickFrequency=1,IsSnapToTickEnabled=true};Grid.SetColumn(slider,1);row.Children.Add(slider);channels[index]=slider;
            System.Windows.Automation.AutomationProperties.SetName(slider,(appBar?"AppBar":"Dock")+" "+label.Text);
            numbers[index]=new TextBlock{HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(numbers[index],2);row.Children.Add(numbers[index]);root.Children.Add(row);
            slider.ValueChanged+=(_,_)=>{numbers[channel].Text=Math.Round(slider.Value).ToString("0");if(!syncing){var colour=Color.FromRgb((byte)channels[0].Value,(byte)channels[1].Value,(byte)channels[2].Value);Select(colour,1);}};
        }
        var swatches=new WrapPanel{Margin=new Thickness(0,6,0,4)};root.Children.Add(new TextBlock{Text="Wallpaper colours",Margin=new Thickness(0,8,0,0)});root.Children.Add(swatches);
        var status=new TextBlock{FontSize=11,Opacity=.75,TextWrapping=TextWrapping.Wrap};root.Children.Add(status);
        root.Children.Add(new TextBlock{Text="Transparency (%)",Margin=new Thickness(0,8,0,0)});
        var transparency=new Slider{Minimum=0,Maximum=100,Value=SurfaceAppearance.Transparency(prefs,appBar)*100,TickFrequency=1,IsSnapToTickEnabled=true};root.Children.Add(transparency);
        root.Children.Add(percent);System.Windows.Automation.AutomationProperties.SetName(transparency,(appBar?"AppBar":"Dock")+" background transparency");
        root.Children.Add(new TextBlock{Text="Blur (%)",Margin=new Thickness(0,8,0,0)});
        var blur=new Slider{Minimum=0,Maximum=100,Value=SurfaceAppearance.Blur(prefs,appBar)/.6,TickFrequency=1,IsSnapToTickEnabled=true};root.Children.Add(blur);root.Children.Add(blurText);
        System.Windows.Automation.AutomationProperties.SetName(blur,(appBar?"AppBar":"Dock")+" background blur");
        var reset=new Button{Content="Reset background to default",Margin=new Thickness(0,8,0,0)};root.Children.Add(reset);
        void Preview()
        {
            var colour=SurfaceAppearance.Effective(prefs,appBar,monitor());preview.Background=new SolidColorBrush(colour);previewText.Foreground=SurfaceAppearance.WhiteInk(colour)?Brushes.White:Brushes.Black;
            percent.Text=$"{SurfaceAppearance.Transparency(prefs,appBar)*100:0}% · 0% opaque, 100% clear";blurText.Text=$"{SurfaceAppearance.Blur(prefs,appBar)/.6:0}%";
            strategy.Visibility=(appBar?prefs.AppBarColourSource:prefs.DockColourSource)==2?Visibility.Visible:Visibility.Collapsed;
        }
        void SetChannels(Color colour){syncing=true;hex.Text=SurfaceAppearance.Hex(colour);channels[0].Value=colour.R;channels[1].Value=colour.G;channels[2].Value=colour.B;for(int i=0;i<3;i++)numbers[i].Text=channels[i].Value.ToString("0");syncing=false;}
        void Commit(){Preview();apply();}
        void Select(Color colour,int source){if(appBar){prefs.AppBarColour=SurfaceAppearance.Hex(colour);prefs.AppBarColourSource=source;}else{prefs.DockColour=SurfaceAppearance.Hex(colour);prefs.DockColourSource=source;}SetChannels(colour);syncing=true;sources.SelectedIndex=source;syncing=false;error.Text="";Commit();}
        void Palette()
        {
            swatches.Children.Clear();var palette=WallpaperPalette.Current?.ForMonitor(monitor());
            if(palette!=null)foreach(var colour in palette.Colours)
            {
                var swatch=new Button{Width=38,Height=32,Padding=new Thickness(0),Margin=new Thickness(0,0,6,6),ToolTip=SurfaceAppearance.Hex(colour),Content=new Border{Width=30,Height=24,Background=new SolidColorBrush(colour),CornerRadius=new CornerRadius(4)}};
                System.Windows.Automation.AutomationProperties.SetName(swatch,(appBar?"AppBar":"Dock")+" wallpaper colour "+SurfaceAppearance.Hex(colour));swatch.Click+=(_,_)=>Select(colour,3);swatches.Children.Add(swatch);
            }
            status.Text=WallpaperPalette.Current?.Status??"Wallpaper unavailable; the selected colour is preserved.";Preview();
        }
        sources.SelectionChanged+=(_,_)=>{if(syncing||sources.SelectedIndex<0)return;var previous=SurfaceAppearance.Background(prefs,appBar,monitor());if(appBar){if(sources.SelectedIndex==2)prefs.AppBarWallpaperFallback=SurfaceAppearance.Hex(previous);prefs.AppBarColourSource=sources.SelectedIndex;}else{if(sources.SelectedIndex==2)prefs.DockWallpaperFallback=SurfaceAppearance.Hex(previous);prefs.DockColourSource=sources.SelectedIndex;}Commit();};
        strategy.SelectionChanged+=(_,_)=>{if(syncing||strategy.SelectedIndex<0)return;if(appBar)prefs.AppBarWallpaperStrategy=strategy.SelectedIndex;else prefs.DockWallpaperStrategy=strategy.SelectedIndex;Commit();};
        hex.TextChanged+=(_,_)=>{if(syncing)return;if(SurfaceAppearance.TryHex(hex.Text,out var colour))Select(colour,1);else error.Text="Enter six HEX digits, for example #8040C0.";};
        transparency.ValueChanged+=(_,_)=>{if(syncing)return;if(appBar)prefs.AppBarTransparency=Math.Round(transparency.Value)/100;else prefs.DockTransparency=Math.Round(transparency.Value)/100;Commit();};
        blur.ValueChanged+=(_,_)=>{if(syncing)return;if(appBar)prefs.AppBarBlur=Math.Round(blur.Value)*.6;else prefs.DockBlur=Math.Round(blur.Value)*.6;Commit();};
        reset.Click+=(_,_)=>{syncing=true;sources.SelectedIndex=0;strategy.SelectedIndex=0;transparency.Value=80;blur.Value=27;syncing=false;if(appBar){prefs.AppBarColourSource=0;prefs.AppBarWallpaperStrategy=0;prefs.AppBarColour="#000000";prefs.AppBarTransparency=.8;prefs.AppBarBlur=16.2;}else{prefs.DockColourSource=0;prefs.DockWallpaperStrategy=0;prefs.DockColour="#000000";prefs.DockTransparency=.8;prefs.DockBlur=16.2;}SetChannels(Colors.Black);Commit();};
        SurfaceAppearance.TryHex(appBar?prefs.AppBarColour:prefs.DockColour,out var initial);SetChannels(initial);Palette();
        root.Loaded+=(_,_)=>{if(WallpaperPalette.Current is {} service)service.Changed+=Palette;Palette();};root.Unloaded+=(_,_)=>{if(WallpaperPalette.Current is {} service)service.Changed-=Palette;};
        return root;
    }
}
