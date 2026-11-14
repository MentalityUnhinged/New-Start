using System;
using System.Windows;

namespace NewStart;
public enum DockEdge { Bottom, Top, Left }
public enum DockAlignment { Start, Centre, End }
internal static class DockGeometry
{
    internal const double WindowClearance=8;
    internal static double Aligned(double origin,double length,double size,DockAlignment alignment)
    {
        double offset=alignment switch{DockAlignment.Start=>8,DockAlignment.End=>length-size-8,_=>(length-size)/2};
        return origin+Math.Clamp(offset,0,Math.Max(0,length-size));
    }
    internal static Point DockOrigin(Rect area,Size size,DockEdge edge,DockAlignment alignment)=>edge switch
    {
        DockEdge.Top=>new(Aligned(area.Left,area.Width,size.Width,alignment),area.Top+4),
        DockEdge.Left=>new(area.Left+4,Aligned(area.Top,area.Height,size.Height,alignment)),
        _=>new(Aligned(area.Left,area.Width,size.Width,alignment),area.Bottom-size.Height-4)
    };
    internal static Rect MenuBounds(Rect work,Rect anchor,DockEdge edge,DockAlignment alignment=DockAlignment.Centre,Rect? dockBounds=null)
    {
        const double margin=8,gap=10;
        double width=Math.Min(640,Math.Max(1,work.Width-2*margin)),height=Math.Min(660,Math.Max(1,work.Height-2*margin));
        if(edge==DockEdge.Left)width=Math.Min(width,Math.Max(1,work.Right-anchor.Right-gap-margin));
        else height=Math.Min(height,Math.Max(1,edge==DockEdge.Top?work.Bottom-anchor.Bottom-gap-margin:anchor.Top-work.Top-gap-margin));
        double x=edge==DockEdge.Left?anchor.Right+gap:anchor.Left+anchor.Width/2-width/2;
        if(edge!=DockEdge.Left&&alignment==DockAlignment.Centre)x=work.Left+(work.Width-width)/2;
        double y=edge switch{DockEdge.Top=>anchor.Bottom+gap,DockEdge.Left=>anchor.Top+anchor.Height/2-height/2,_=>anchor.Top-gap-height};
        if(edge==DockEdge.Left&&alignment==DockAlignment.Centre&&dockBounds is Rect dock)y=dock.Top+(dock.Height-height)/2;
        x=Math.Clamp(x,work.Left+margin,Math.Max(work.Left+margin,work.Right-width-margin));
        y=Math.Clamp(y,work.Top+margin,Math.Max(work.Top+margin,work.Bottom-height-margin));
        return new Rect(x,y,width,height);
    }
}
