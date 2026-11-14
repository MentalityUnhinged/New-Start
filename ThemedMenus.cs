using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace NewStart;
internal static class ThemedMenus
{
    [DllImport("NewStart.Backdrop.dll",CallingConvention=CallingConvention.StdCall)]static extern int CreateBackdrop(IntPtr hwnd,float radius,[MarshalAs(UnmanagedType.Bool)]bool visible,out IntPtr state);
    [DllImport("NewStart.Backdrop.dll",CallingConvention=CallingConvention.StdCall)]static extern int RebindBackdrop(IntPtr state,IntPtr owner,float radius,[MarshalAs(UnmanagedType.Bool)]bool visible);
    [DllImport("NewStart.Backdrop.dll",CallingConvention=CallingConvention.StdCall)]static extern int UpdateBackdrop(IntPtr state,float radius,[MarshalAs(UnmanagedType.Bool)]bool visible);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out Native.Rect bounds);
    // One reusable popup surface avoids destroying a live composition target
    // while WPF is closing its popup HWND.
    static IntPtr popupBackdrop;static ContextMenu? activeMenu;
    internal static long PositionUpdates {get;private set;}
    static ResourceDictionary? styles;
    internal static ContextMenu Create(Preferences prefs)
    {
        var menu=new ContextMenu{MinWidth=210,MaxWidth=460,FontFamily=new FontFamily("Segoe UI Variable, Segoe UI"),FontSize=13,HasDropShadow=false};
        menu.Resources.MergedDictionaries.Add(styles??=(ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="ContextMenu"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ContextMenu"><Border CornerRadius="8" BorderThickness="1" BorderBrush="{DynamicResource MenuOutline}" Background="{DynamicResource MenuSurface}" Padding="5"><StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/></Border></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="MenuItem"><Setter Property="Foreground" Value="{DynamicResource MenuText}"/><Setter Property="Padding" Value="10,8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem">
            <Border x:Name="Row" Background="Transparent" CornerRadius="4" Margin="0,1" Padding="{TemplateBinding Padding}"><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="24"/><ColumnDefinition/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><ContentPresenter ContentSource="Icon" Width="16" Height="16" VerticalAlignment="Center" HorizontalAlignment="Left"/><ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center"/><TextBlock Grid.Column="2" Text="{TemplateBinding InputGestureText}" Margin="18,0,2,0" Opacity="0.65" VerticalAlignment="Center"/></Grid></Border>
            <ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Row" Property="Background" Value="{DynamicResource MenuHover}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="Separator"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Separator"><Border Height="1" Background="{DynamicResource MenuOutline}" Margin="8,5"/></ControlTemplate></Setter.Value></Setter></Style>
        </ResourceDictionary>
        """));
        (IntPtr Handle,int Left,int Top,int Right,int Bottom,double Blur) lastPosition=default;
        void FollowPopup(object? sender,EventArgs args)
        {
            if(activeMenu!=menu||popupBackdrop==IntPtr.Zero||PresentationSource.FromVisual(menu) is not HwndSource source||!GetWindowRect(source.Handle,out var bounds))return;
            var position=(source.Handle,bounds.Left,bounds.Top,bounds.Right,bounds.Bottom,prefs.MenuBlur);
            if(position==lastPosition)return;lastPosition=position;
            PositionUpdates++;
            RebindBackdrop(popupBackdrop,source.Handle,(float)prefs.MenuBlur,true);
        }
        HwndSource? popupSource=null;
        IntPtr PositionChanged(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){if(message==0x0047)FollowPopup(null,EventArgs.Empty);return IntPtr.Zero;}
        void Release(){lastPosition=default;if(popupSource!=null){popupSource.RemoveHook(PositionChanged);popupSource=null;}if(activeMenu==menu){if(popupBackdrop!=IntPtr.Zero)UpdateBackdrop(popupBackdrop,0,false);activeMenu=null;}}
        menu.Opened+=(_,_)=>{
            Release();bool dark=Appearance.IsDark(prefs.Theme);bool effects=NativeBackdrop.TransparencyAllowed();
            var text=SystemParameters.HighContrast?SystemColors.MenuTextBrush:new SolidColorBrush(dark?Color.FromRgb(245,245,245):Color.FromRgb(25,25,25));
            menu.Foreground=text;menu.Resources["MenuText"]=text;
            menu.Resources["MenuOutline"]=new SolidColorBrush(dark?Color.FromArgb(80,160,160,160):Color.FromArgb(70,80,80,80));
            menu.Resources["MenuHover"]=new SolidColorBrush(dark?Color.FromArgb(40,255,255,255):Color.FromArgb(22,0,0,0));
            if(effects&&PresentationSource.FromVisual(menu) is HwndSource source)
            {
                try{activeMenu=menu;int result=popupBackdrop==IntPtr.Zero?CreateBackdrop(source.Handle,(float)prefs.MenuBlur,true,out popupBackdrop):RebindBackdrop(popupBackdrop,source.Handle,(float)prefs.MenuBlur,true);if(result<0){Release();effects=false;}}
                catch(DllNotFoundException){Release();effects=false;}catch(EntryPointNotFoundException){Release();effects=false;}
            }
            else effects=false;
            if(effects&&PresentationSource.FromVisual(menu) is HwndSource popup){popupSource=popup;popup.AddHook(PositionChanged);}
            byte alpha=effects?(byte)Math.Max(1,Math.Round(255*(1-prefs.StartTransparency))):(byte)255;
            menu.Resources["MenuSurface"]=SystemParameters.HighContrast?SystemColors.MenuBrush:new SolidColorBrush(dark?Color.FromArgb(alpha,35,35,35):Color.FromArgb(alpha,243,243,243));
            foreach(var item in menu.Items)if(item is MenuItem entry&&entry.Icon==null)
            {
                string label=entry.Header?.ToString()??"";
                string glyph=label switch{"Pin" or "Pin to taskbar"=>"\uE718","Unpin" or "Unpin from taskbar" or "Hide from New Start"=>"\uE77A","Close window" or "Close all windows"=>"\uE8BB","Task Manager"=>"\uE9D9","Taskbar settings" or "New Start settings"=>"\uE713","Launch new instance"=>"\uE768","Move left"=>"\uE72B","Move right"=>"\uE72A","Open Recycle Bin"=>"\uE8E5","Lock"=>"\uE72E","Restart"=>"\uE777","Shut down"=>"\uE7E8","Cut"=>"\uE8C6","Copy"=>"\uE8C8","Paste"=>"\uE77F","Select all"=>"\uE8B3",_=>"\uE737"};
                entry.Icon=new TextBlock{Text=glyph,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=15,VerticalAlignment=VerticalAlignment.Center};
            }
        };
        menu.Closed+=(_,_)=>Release();
        return menu;
    }
}
