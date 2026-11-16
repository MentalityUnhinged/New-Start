using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
namespace NewStart;
internal static class DockDragGeometry
{
    internal static int InsertionIndex(IReadOnlyList<Rect> bounds,Point pointer,bool vertical)=>bounds.Count(rect=>(vertical?rect.Top+rect.Height/2:rect.Left+rect.Width/2)<(vertical?pointer.Y:pointer.X));
    internal static double InsertionPosition(IReadOnlyList<Rect> bounds,int index,bool vertical)
    {
        if(bounds.Count==0)return 0;index=Math.Clamp(index,0,bounds.Count);var rect=bounds[Math.Min(index,bounds.Count-1)];return index<bounds.Count?(vertical?rect.Top:rect.Left):(vertical?rect.Bottom:rect.Right);
    }
    internal static bool IsRemoval(Point pointer,Rect footprint,double threshold){footprint.Inflate(Math.Max(0,threshold),Math.Max(0,threshold));return !footprint.Contains(pointer);}
}
