using System;
using System.Collections.Generic;
using System.Windows;

namespace NewStart;
internal static class LocationValidation
{
    internal static Dictionary<string,bool> Geometry()
    {
        var checks=new Dictionary<string,bool>();var prefs=new Preferences();
        checks["DefaultBottomCentre"]=prefs.Edge==DockEdge.Bottom&&prefs.Alignment==DockAlignment.Centre;
        var work=new Rect(0,0,1920,1080);
        foreach(var edge in Enum.GetValues<DockEdge>())foreach(var alignment in Enum.GetValues<DockAlignment>())
        {
            var size=edge==DockEdge.Left?new Size(100,500):new Size(500,100);var origin=DockGeometry.DockOrigin(work,size,edge,alignment);
            var anchor=new Rect(origin.X+12,origin.Y+(edge==DockEdge.Bottom?72:12),16,16);
            var menu=DockGeometry.MenuBounds(work,anchor,edge,alignment,new Rect(origin,size));
            bool side=edge switch{DockEdge.Top=>menu.Top>=anchor.Bottom,DockEdge.Left=>menu.Left>=anchor.Right,_=>menu.Bottom<=anchor.Top};
            bool centred=edge==DockEdge.Left||alignment!=DockAlignment.Centre||Math.Abs(menu.Left+menu.Width/2-960)<1;
            if(edge==DockEdge.Left&&alignment==DockAlignment.Centre)centred=Math.Abs(menu.Top+menu.Height/2-(origin.Y+size.Height/2))<1;
            checks[$"{edge}{alignment}MenuFits"]=work.Contains(menu)&&side&&centred;
        }
        prefs.Edge=(DockEdge)99;prefs.Alignment=(DockAlignment)99;prefs.Normalize();
        checks["InvalidLocationFallsBack"]=prefs.Edge==DockEdge.Bottom&&prefs.Alignment==DockAlignment.Centre;
        return checks;
    }
}
