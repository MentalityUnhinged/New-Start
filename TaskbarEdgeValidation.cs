using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace NewStart;
internal static class TaskbarEdgeValidation
{
    [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    internal static void Run(Dictionary<string,bool> checks)
    {
        var monitor=new Native.Rect{Left=-1920,Top=-400,Right=0,Bottom=680};
        checks["TaskbarCorrectsStaleShellEdge"]=TaskbarEdgeShield.SideFromBounds(new(){Left=0,Top=0,Right=1920,Bottom=1080},new(){Left=0,Top=1078,Right=1920,Bottom=1110},1)==3;
        foreach(uint side in new uint[]{0,1,2,3}){var strip=TaskbarEdgeShield.StripBounds(monitor,side);checks["TaskbarStripGeometry"+side]=strip.Left>=monitor.Left&&strip.Top>=monitor.Top&&strip.Right<=monitor.Right&&strip.Bottom<=monitor.Bottom&&(side is 0 or 2?strip.Right-strip.Left==2&&strip.Bottom-strip.Top==1080:strip.Bottom-strip.Top==2&&strip.Right-strip.Left==1920);}
        var display=DesktopDisplays.All().First();var bounds=TaskbarEdgeShield.StripBounds(display.Bounds,3);var before=DesktopDisplays.Work(display.Handle);var foreground=Native.GetForegroundWindow();IntPtr handle;
        using(var strip=new TaskbarEdgeShield.Strip())
        {
            handle=strip.Handle;strip.Update(bounds,true);
            checks["TaskbarStripReceivesEdgeHit"]=WindowFromPoint(new Point{X=(bounds.Left+bounds.Right)/2,Y=bounds.Bottom-1})==handle;
            checks["TaskbarStripDoesNotActivate"]=Native.GetForegroundWindow()==foreground&&SendMessage(handle,0x21,IntPtr.Zero,IntPtr.Zero)==new IntPtr(4);
            var after=DesktopDisplays.Work(display.Handle);checks["TaskbarStripReservesNoWorkspace"]=before.Left==after.Left&&before.Top==after.Top&&before.Right==after.Right&&before.Bottom==after.Bottom;
            strip.Update(bounds,false);checks["TaskbarStripHidesImmediately"]=!Native.IsWindowVisible(handle);
        }
        checks["TaskbarStripDestroyedOnDisable"]=!ActiveApplication.IsWindow(handle);
        var owner=new System.Windows.Window();new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
        var preferences=new Preferences{BlockTaskbarEdgeReveal=true};
        using(var manager=new TaskbarEdgeShield(owner,preferences))
        {
            var edges=TaskbarEdgeShield.Edges();checks["TaskbarManagerUsesDetectedEdges"]=manager.Error==null&&manager.Count==edges.Length;
            System.IO.File.WriteAllText(System.IO.Path.Combine(Preferences.Folder,"taskbar-edge-verification.json"),System.Text.Json.JsonSerializer.Serialize(new{AutoHide=Native.AutoHide,DetectedEdges=edges.Select(edge=>{DesktopDisplays.GetWindowRect(edge.Taskbar,out var rect);return new{Monitor=edge.Display.Name,edge.Side,edge.Source,rect.Left,rect.Top,rect.Right,rect.Bottom};}),ShieldCount=manager.Count,manager.Error}));
            preferences.BlockTaskbarEdgeReveal=false;manager.Refresh();checks["TaskbarManagerUnblocksImmediately"]=manager.Count==0;
        }
        owner.Close();
    }
}
