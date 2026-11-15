using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NewStart;
internal readonly record struct AppBarOutline(double Width,double Rest,double Left,double LobeWidth,double Extra,double Shoulder,double Radius)
{
    internal Geometry Geometry()
    {
        var geometry=new StreamGeometry();using(var draw=geometry.Open())
        {
            double l=Left,r=l+LobeWidth,b=Rest+Extra,s=Shoulder,k=Radius;
            draw.BeginFigure(new Point(0,0),true,true);draw.LineTo(new Point(Width,0),true,false);draw.LineTo(new Point(Width,Rest),true,false);
            if(Extra>.5)
            {
                draw.LineTo(new Point(r+s,Rest),true,false);draw.BezierTo(new Point(r+s*.45,Rest),new Point(r,Rest+s*.45),new Point(r,Rest+s),true,true);
                draw.LineTo(new Point(r,b-k),true,false);draw.BezierTo(new Point(r,b-k*.45),new Point(r-k*.45,b),new Point(r-k,b),true,true);
                draw.LineTo(new Point(l+k,b),true,false);draw.BezierTo(new Point(l+k*.45,b),new Point(l,b-k*.45),new Point(l,b-k),true,true);
                draw.LineTo(new Point(l,Rest+s),true,false);draw.BezierTo(new Point(l,Rest+s*.45),new Point(l-s*.45,Rest),new Point(l-s,Rest),true,true);
            }
            draw.LineTo(new Point(0,Rest),true,false);
        }
        geometry.Freeze();return geometry;
    }
}
internal sealed class AppBarMaterial:IDisposable
{
    [DllImport("NewStart.Backdrop.dll")]static extern int CreateBackdrop(IntPtr owner,float radius,bool visible,out IntPtr state);
    [DllImport("NewStart.Backdrop.dll")]static extern int UpdateAppBarBackdrop(IntPtr state,float strip,float left,float width,float extra,float shoulder,float corner,float radius,bool visible);
    [DllImport("NewStart.Backdrop.dll")]static extern int UpdateBackdrop(IntPtr state,float radius,bool visible);
    [DllImport("NewStart.Backdrop.dll")]static extern bool IsBackdropVisible(IntPtr state);
    [DllImport("NewStart.Backdrop.dll")]static extern bool BackdropRegionContains(IntPtr state,float x,float y);
    [DllImport("NewStart.Backdrop.dll")]static extern void DestroyBackdrop(IntPtr state);
    internal Path Surface {get;}=new(){IsHitTestVisible=false,StrokeThickness=1,Stretch=Stretch.None,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
    internal bool NativeVisible=>handle!=IntPtr.Zero&&IsBackdropVisible(handle);
    internal double TintOpacity=>Surface.Fill?.Opacity??0;
    internal Color? TintColour=>(Surface.Fill as SolidColorBrush)?.Color;
    internal bool NativeContains(Point point)=>handle!=IntPtr.Zero&&BackdropRegionContains(handle,(float)point.X,(float)point.Y);
    IntPtr handle;
    (AppBarOutline Shape,double X,double Y,double Dpi,bool Visible,bool Dark,bool High,bool Effects,double Transparency,double Blur,Color? Colour)? previous;
    (bool Dark,bool High,bool Native,double Transparency,Color? Colour)? palette;
    internal void Update(Window owner,Preferences prefs,AppBarOutline shape)
    {
        bool effects=NativeBackdrop.TransparencyAllowed()&&!SystemParameters.HighContrast&&prefs.StartTransparency>0&&prefs.StartTransparency<1;
        var key=(shape,owner.Left,owner.Top,VisualTreeHelper.GetDpi(owner).DpiScaleX,owner.IsVisible,Appearance.IsDark(prefs.Theme),SystemParameters.HighContrast,effects,prefs.StartTransparency,prefs.MenuBlur,prefs.SurfaceColour);
        if(previous==key&&(prefs.MenuBlur==0||!effects||NativeVisible))return;
        Surface.Data=shape.Geometry();bool native=effects;
        try
        {
            if(effects&&prefs.MenuBlur>0&&handle==IntPtr.Zero)native=CreateBackdrop(new WindowInteropHelper(owner).Handle,0,false,out handle)>=0;
            if(handle!=IntPtr.Zero)native=UpdateAppBarBackdrop(handle,(float)shape.Rest,(float)shape.Left,(float)shape.LobeWidth,(float)shape.Extra,(float)shape.Shoulder,(float)shape.Radius,(float)prefs.MenuBlur,effects&&owner.IsVisible)>=0&&effects;
        }
        catch(DllNotFoundException){native=false;}catch(EntryPointNotFoundException){native=false;}
        if(!native&&handle!=IntPtr.Zero)UpdateBackdrop(handle,0,false);
        var colors=(Appearance.IsDark(prefs.Theme),SystemParameters.HighContrast,native,prefs.StartTransparency,prefs.SurfaceColour);
        if(palette!=colors){var material=DockGlass.Material(prefs,native);Surface.Fill=material.Tint;Surface.Stroke=material.Rim;palette=colors;}
        previous=key;
    }
    public void Dispose(){if(handle!=IntPtr.Zero){DestroyBackdrop(handle);handle=IntPtr.Zero;}}
}
