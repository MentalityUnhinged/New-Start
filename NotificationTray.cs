using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace NewStart;
// A launcher for Explorer's real notification area, not an imitation icon host.
internal static class NotificationTray
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string className,string? title);
    internal static Button BuildButton(Window owner,Preferences prefs,Action revealNativeTray,Func<Preferences>? menuPreferences=null)
    {
        var glyph=new TextBlock{Text="\uE8EF",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=15,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        var button=new Button{Content=glyph,MinWidth=28,MinHeight=28,Padding=new Thickness(5),ToolTip="Open Windows notification area"};
        StartMenuWindow.Borderless(button);System.Windows.Automation.AutomationProperties.SetName(button,"Windows notification area");
        bool opening=false;
        button.Click+=async(_,_)=>
        {
            if(opening)return;opening=true;
            try
            {
                string? failure=await ShowAsync(owner,revealNativeTray);button.ToolTip=failure??"Open Windows notification area";
                if(failure!=null)MessageBox.Show(owner,failure,"Windows notification area",MessageBoxButton.OK,MessageBoxImage.Information);
            }
            finally{opening=false;}
        };
        var menu=ThemedMenus.Create(prefs,menuPreferences);
        var open=new MenuItem{Header="Open Windows notification area"};open.Click+=async(_,_)=>{string? failure=await ShowAsync(owner,revealNativeTray);if(failure!=null)MessageBox.Show(owner,failure,"Windows notification area");};menu.Items.Add(open);
        var settings=new MenuItem{Header="Windows taskbar and tray settings"};settings.Click+=(_,_)=>AdaptiveAppBarWindow.Open("ms-settings:taskbar");menu.Items.Add(settings);button.ContextMenu=menu;
        return button;
    }
    internal static async Task<string?> ShowAsync(Window owner,Action revealNativeTray)
    {
        try
        {
            revealNativeTray();var monitor=DesktopDisplays.MonitorFromWindow(new WindowInteropHelper(owner).Handle,2);
            var windows=TaskbarSuppression.Windows();
            // The primary Explorer taskbar hosts the real notification area; secondary taskbars may have no tray.
            var target=FindWindow("Shell_TrayWnd",null);if(!TaskbarSuppression.Official(target))target=IntPtr.Zero;
            if(target==IntPtr.Zero)target=windows.FirstOrDefault(window=>DesktopDisplays.MonitorFromWindow(window,2)==monitor);
            if(target==IntPtr.Zero)target=windows.FirstOrDefault();
            if(target==IntPtr.Zero)return "Explorer is not exposing a Windows taskbar. Restart Explorer or open Windows taskbar settings.";
            if(!TaskbarSuppression.Official(target))return "The Windows taskbar changed. Try again.";
            Native.ShowWindowAsync(target,8);
            for(int attempt=0;attempt<16&&!Native.IsWindowVisible(target);attempt++)await Task.Delay(50);
            if(!TaskbarSuppression.Official(target)||!Native.IsWindowVisible(target))return "Windows did not reveal its taskbar. You can also use the Windows key to access it.";
            // A restored HWND can still be auto-hidden offscreen. Ask Windows to focus its
            // real tray using the documented system shortcut, without manipulating overflow internals.
            if(!Native.FocusNotificationArea()&&!Native.SetForegroundWindow(target))return "The Windows taskbar is available, but Windows did not allow moving keyboard focus to it. Click its notification area to continue.";
            for(int attempt=0;attempt<12&&Native.GetForegroundWindow()!=target;attempt++)await Task.Delay(25);
            if(Native.GetForegroundWindow()!=target)return "Windows did not confirm notification-area focus. Use Windows + B or open Windows taskbar settings.";
            return null;
        }
        catch{return "The Windows notification area is unavailable. Open Windows taskbar settings or use the Windows key.";}
    }
}
