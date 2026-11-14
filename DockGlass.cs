using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace NewStart;
internal sealed class DockGlass:IDisposable
{
    [DllImport("NewStart.Backdrop.dll",CallingConvention=CallingConvention.StdCall)]static extern int CreateBackdrop(IntPtr owner,float radius,[MarshalAs(UnmanagedType.Bool)]bool visible,out IntPtr state);
    [DllImport("NewStart.Backdrop.dll",CallingConvention=CallingConvention.StdCall)]static extern int UpdateBackdropRegion(IntPtr state,float x,float y,float width,float height,float corner,float radius,[MarshalAs(UnmanagedType.Bool)]bool visible);
    [DllImport("NewStart.Backdrop.dll",CallingConvention=CallingConvention.StdCall)]static extern int UpdateBackdrop(IntPtr state,float radius,[MarshalAs(UnmanagedType.Bool)]bool visible);
    [DllImport("NewStart.Backdrop.dll",CallingConvention=CallingConvention.StdCall)]static extern bool IsBackdropVisible(IntPtr state);
    [DllImport("NewStart.Backdrop.dll",CallingConvention=CallingConvention.StdCall)]static extern void DestroyBackdrop(IntPtr state);
    internal Border Surface {get;}=new(){Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,BorderThickness=new Thickness(1),IsHitTestVisible=false};
    internal Rect Bounds {get;private set;}
    internal bool NativeVisible=>handle!=IntPtr.Zero&&IsBackdropVisible(handle);
    internal double TintOpacity=>Surface.Background?.Opacity??0;
    IntPtr handle;bool visible;
    (Rect Bounds,double X,double Y,double Dpi,bool OwnerVisible,bool Dark,bool HighContrast,bool Effects,double Transparency,double Blur)? previous;
    internal static Brush Ink(Preferences prefs)=>!prefs.DockBackground?Brushes.White:SystemParameters.HighContrast?SystemColors.WindowTextBrush:Appearance.IsDark(prefs.Theme)?Brushes.White:new SolidColorBrush(Color.FromRgb(30,35,43));
    internal void Update(Window owner,Preferences prefs,Rect bounds)
    {
        if(!prefs.DockBackground||bounds.IsEmpty||bounds.Width<=0||bounds.Height<=0)
        {
            Surface.Visibility=Visibility.Collapsed;previous=null;
            if(visible&&handle!=IntPtr.Zero)UpdateBackdrop(handle,0,false);visible=false;return;
        }
        bool dark=Appearance.IsDark(prefs.Theme),high=SystemParameters.HighContrast,effects=NativeBackdrop.TransparencyAllowed();
        var key=(bounds,owner.Left,owner.Top,VisualTreeHelper.GetDpi(owner).DpiScaleX,owner.IsVisible,dark,high,effects,prefs.StartTransparency,prefs.MenuBlur);
        if(previous==key&&(!visible||NativeVisible))return;
        Bounds=bounds;double corner=Math.Min(18,Math.Min(bounds.Width,bounds.Height)/2);
        Surface.Width=bounds.Width;Surface.Height=bounds.Height;Surface.Margin=new Thickness(bounds.Left,bounds.Top,0,0);Surface.CornerRadius=new CornerRadius(corner);Surface.Visibility=Visibility.Visible;
        bool native=effects;
        try
        {
            if(effects&&prefs.MenuBlur>0&&handle==IntPtr.Zero)native=CreateBackdrop(new WindowInteropHelper(owner).Handle,0,false,out handle)>=0;
            if(handle!=IntPtr.Zero)native=UpdateBackdropRegion(handle,(float)bounds.X,(float)bounds.Y,(float)bounds.Width,(float)bounds.Height,(float)corner,(float)prefs.MenuBlur,effects&&owner.IsVisible)>=0&&effects;
        }
        catch(DllNotFoundException){native=false;}catch(EntryPointNotFoundException){native=false;}
        if(!native&&handle!=IntPtr.Zero)UpdateBackdrop(handle,0,false);
        visible=native&&prefs.MenuBlur>0;
        double opacity=native?1-prefs.StartTransparency:1;
        if(high){Surface.Background=SystemColors.WindowBrush;Surface.BorderBrush=SystemColors.WindowTextBrush;}
        else
        {
            var tint=new LinearGradientBrush(dark?Color.FromRgb(58,64,75):Colors.White,dark?Color.FromRgb(22,26,34):Color.FromRgb(210,219,232),90){Opacity=opacity};tint.Freeze();Surface.Background=tint;
            var rim=new LinearGradientBrush(Color.FromArgb(dark?(byte)170:(byte)240,255,255,255),dark?Color.FromArgb(65,205,216,235):Color.FromArgb(85,80,95,120),90){Opacity=opacity};rim.Freeze();Surface.BorderBrush=rim;
        }
        previous=key;
    }
    public void Dispose(){if(handle!=IntPtr.Zero){DestroyBackdrop(handle);handle=IntPtr.Zero;}}
}
