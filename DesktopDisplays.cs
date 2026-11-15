using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NewStart;
internal record DesktopDisplay(IntPtr Handle,string Name,Native.Rect Bounds,bool Primary);
internal static class DesktopDisplays
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Info{public uint Size;public Native.Rect Monitor,Work;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
    delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,IntPtr rect,IntPtr data);
    [DllImport("user32.dll")]static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfo(IntPtr monitor,ref Info info);
    [DllImport("user32.dll")]internal static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll")]internal static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]internal static extern uint GetDpiForWindow(IntPtr window);
    internal static IReadOnlyList<DesktopDisplay> All()
    {
        var result=new List<DesktopDisplay>();
        EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(handle,_,_,_)=>{var info=new Info{Size=(uint)Marshal.SizeOf<Info>(),Device=""};if(GetMonitorInfo(handle,ref info))result.Add(new(handle,info.Device,info.Monitor,(info.Flags&1)!=0));return true;},IntPtr.Zero);
        return result;
    }
    internal static Native.Rect Work(IntPtr monitor)=>ReadMonitor(monitor,out var info)?info.Work:default;
    internal static bool IsFullscreen(IntPtr window)
    {
        if(window==IntPtr.Zero||!GetWindowRect(window,out var rect)||!ReadMonitor(MonitorFromWindow(window,2),out var display))return false;
        return rect.Left<=display.Monitor.Left&&rect.Top<=display.Monitor.Top&&rect.Right>=display.Monitor.Right&&rect.Bottom>=display.Monitor.Bottom&&!IsZoomed(window);
    }
    static bool ReadMonitor(IntPtr monitor,out Info outInfo){outInfo=new Info{Size=(uint)Marshal.SizeOf<Info>(),Device=""};return GetMonitorInfo(monitor,ref outInfo);}
    [DllImport("user32.dll")]internal static extern bool GetWindowRect(IntPtr window,out Native.Rect bounds);
    [DllImport("user32.dll")]internal static extern bool IsZoomed(IntPtr window);
}
