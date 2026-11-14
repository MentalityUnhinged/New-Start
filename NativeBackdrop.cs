using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;

namespace NewStart;
internal static class NativeBackdrop
{
    [StructLayout(LayoutKind.Sequential)] struct Margins {public int Left,Right,Top,Bottom;}
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")]static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref Margins margins);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    internal static void Configure(Window window)
    {
        if(window.AllowsTransparency)return;
        // System backdrops require a normal DWM-composited HWND, not a layered window.
        WindowChrome.SetWindowChrome(window,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(-1),CornerRadius=new CornerRadius(8),UseAeroCaptionButtons=false});
    }
    internal static bool TransparencyAllowed()
    {
        if(SystemParameters.HighContrast)return false;
        try{using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");return key?.GetValue("EnableTransparency") is not int value||value!=0;}catch{return true;}
    }
    internal static bool Apply(Window window)
    {
        IntPtr handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return false;
        // Retain the DWM caption style for native material rendering, but no caption buttons.
        long style=GetWindowLongPtr(handle,-16).ToInt64();
        long menuStyle=style&~0xB0000L;
        if(style!=menuStyle){SetWindowLongPtr(handle,-16,new IntPtr(menuStyle));SetWindowPos(handle,IntPtr.Zero,0,0,0,0,0x27);}
        bool enabled=!SystemParameters.HighContrast;
        try{using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");if(key?.GetValue("EnableTransparency") is int value&&value==0)enabled=false;}catch{}
        int backdrop=enabled?3:1; // DWMSBT_TRANSIENTWINDOW: native Desktop Acrylic.
        int result=DwmSetWindowAttribute(handle,38,ref backdrop,sizeof(int));
        if(result<0||!enabled)return false;
        var source=HwndSource.FromHwnd(handle);if(source?.CompositionTarget!=null)source.CompositionTarget.BackgroundColor=Colors.Transparent;
        var margins=new Margins{Left=-1,Right=-1,Top=-1,Bottom=-1};
        if(DwmExtendFrameIntoClientArea(handle,ref margins)<0)return false;
        int corner=2;DwmSetWindowAttribute(handle,33,ref corner,sizeof(int));
        return true;
    }
}

