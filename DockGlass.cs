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
    internal double MaximumCornerRadius {get;set;}=18;
    internal bool NativeVisible=>handle!=IntPtr.Zero&&IsBackdropVisible(handle);
    internal double TintOpacity=>Surface.Background?.Opacity??0;
    internal Color? TintColour=>(Surface.Background as SolidColorBrush)?.Color;
    IntPtr handle;bool visible;
    (Rect Bounds,double X,double Y,double Dpi,bool OwnerVisible,bool Dark,bool HighContrast,bool Effects,double Transparency,double Blur,Color Colour)? previous;
    internal static Brush Ink(Preferences prefs,string? monitor=null)=>!prefs.DockBackground?Brushes.White:SurfaceAppearance.Ink(prefs,false,monitor);
    internal void Update(Window owner,Preferences prefs,Rect bounds)
    {
        if(!prefs.DockBackground||bounds.IsEmpty||bounds.Width<=0||bounds.Height<=0)
        {
            Surface.Visibility=Visibility.Collapsed;previous=null;
            if(visible&&handle!=IntPtr.Zero)UpdateBackdrop(handle,0,false);visible=false;return;
        }
        string? monitor=System.Linq.Enumerable.FirstOrDefault(DesktopDisplays.All(),display=>display.Handle==DesktopDisplays.MonitorFromWindow(new WindowInteropHelper(owner).Handle,2))?.Name;
        double transparency=SurfaceAppearance.Transparency(prefs,false),blur=SurfaceAppearance.Blur(prefs,false);var colour=SurfaceAppearance.Background(prefs,false,monitor);
        bool dark=Appearance.IsDark(prefs.Theme),high=SystemParameters.HighContrast,effects=NativeBackdrop.TransparencyAllowed()&&!high&&transparency>0&&transparency<1;
        var key=(bounds,owner.Left,owner.Top,VisualTreeHelper.GetDpi(owner).DpiScaleX,owner.IsVisible,dark,high,effects,transparency,blur,colour);
        if(previous==key&&(!visible||NativeVisible))return;
        Bounds=bounds;double corner=Math.Min(MaximumCornerRadius,Math.Min(bounds.Width,bounds.Height)/2);
        Surface.Width=bounds.Width;Surface.Height=bounds.Height;Surface.Margin=new Thickness(bounds.Left,bounds.Top,0,0);Surface.CornerRadius=new CornerRadius(corner);Surface.Visibility=Visibility.Visible;
        bool native=effects;
        try
        {
            if(effects&&blur>0&&handle==IntPtr.Zero)native=CreateBackdrop(new WindowInteropHelper(owner).Handle,0,false,out handle)>=0;
            if(handle!=IntPtr.Zero)native=UpdateBackdropRegion(handle,(float)bounds.X,(float)bounds.Y,(float)bounds.Width,(float)bounds.Height,(float)corner,(float)blur,effects&&owner.IsVisible)>=0&&effects;
        }
        catch(DllNotFoundException){native=false;}catch(EntryPointNotFoundException){native=false;}
        if(!native&&handle!=IntPtr.Zero)UpdateBackdrop(handle,0,false);
        visible=native&&blur>0;
        var material=SystemParameters.HighContrast?(SystemColors.WindowBrush,SystemColors.WindowTextBrush):SurfaceAppearance.Material(colour,transparency);Surface.Background=material.Item1;Surface.BorderBrush=material.Item2;
        previous=key;
    }
    internal static (Brush Tint,Brush Rim) Material(Preferences prefs,bool native)
    {
        if(SystemParameters.HighContrast)return(SystemColors.WindowBrush,SystemColors.WindowTextBrush);
        return SurfaceAppearance.Material(prefs.SurfaceColour??SurfaceAppearance.Background(prefs,false),prefs.StartTransparency);
    }
    public void Dispose(){if(handle!=IntPtr.Zero){DestroyBackdrop(handle);handle=IntPtr.Zero;}}
}
