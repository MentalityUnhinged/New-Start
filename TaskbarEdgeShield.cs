using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace NewStart;
// Intercepts only the official taskbar's edge hit strip. Explorer's windows/settings remain untouched.
internal sealed class TaskbarEdgeShield:IDisposable
{
    [StructLayout(LayoutKind.Sequential)]struct AppBarData{public uint Size;public IntPtr Window;public uint Callback,Edge;public Native.Rect Bounds;public IntPtr Parameter;}
    [DllImport("shell32.dll")]static extern UIntPtr SHAppBarMessage(uint message,ref AppBarData data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder name,int length);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]static extern bool SetLayeredWindowAttributes(IntPtr window,uint colour,byte alpha,uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate IntPtr WindowProc(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct WindowClass{public uint Style;public IntPtr Proc;public int ClassExtra,WindowExtra;public IntPtr Instance,Icon,Cursor,Background;[MarshalAs(UnmanagedType.LPWStr)]public string? Menu;[MarshalAs(UnmanagedType.LPWStr)]public string Name;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateWindowEx(uint extended,string windowClass,string name,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
    [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")]static extern IntPtr DefWindowProc(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string? module);
    [DllImport("gdi32.dll")]static extern IntPtr GetStockObject(int item);
    [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,ActiveApplication.EventProc callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
    internal record Edge(DesktopDisplay Display,uint Side,IntPtr Taskbar){internal string Source {get;init;}="Shell registration";}
    readonly Window owner;readonly Preferences prefs;readonly HwndSource ownerSource;readonly uint recreated;readonly ActiveApplication.EventProc foregroundCallback;readonly IntPtr foregroundHook;
    readonly Dictionary<(string,uint),Strip> strips=new();bool disposed,queued;
    internal int Count=>strips.Count;
    internal string? Error {get;private set;}
    internal TaskbarEdgeShield(Window window,Preferences preferences)
    {
        owner=window;prefs=preferences;ownerSource=HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle);recreated=RegisterWindowMessage("TaskbarCreated");ownerSource.AddHook(Hook);
        foregroundCallback=(_,_,_,_,_,_,_)=>QueueRefresh();foregroundHook=SetWinEventHook(3,3,IntPtr.Zero,foregroundCallback,0,0,0);Refresh();
    }
    static bool Official(IntPtr window){var name=new StringBuilder(128);GetClassName(window,name,name.Capacity);return name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";}
    internal static Edge[] Edges()
    {
        if(!Native.AutoHide)return Array.Empty<Edge>();var result=new List<Edge>();var displays=DesktopDisplays.All();
        foreach(var display in displays)for(uint side=0;side<4;side++)
        {
            var query=new AppBarData{Size=(uint)Marshal.SizeOf<AppBarData>(),Bounds=display.Bounds,Edge=side};var taskbar=new IntPtr(unchecked((long)SHAppBarMessage(11,ref query).ToUInt64()));
            if(taskbar!=IntPtr.Zero&&Official(taskbar))
            {
                uint actual=DesktopDisplays.GetWindowRect(taskbar,out var rect)?SideFromBounds(display.Bounds,rect,side):side;
                if(!result.Any(edge=>edge.Display.Name==display.Name&&edge.Side==actual))result.Add(new(display,actual,taskbar){Source=actual==side?"Shell registration":"Shell registration corrected by window bounds"});
            }
        }
        // Some Explorer versions report only the primary auto-hide appbar. Resolve remaining official bars geometrically.
        Native.EnumWindows((window,_)=>{
            if(!Official(window))return true;var display=displays.FirstOrDefault(item=>item.Handle==DesktopDisplays.MonitorFromWindow(window,2));
            if(display==null||result.Any(edge=>edge.Display.Name==display.Name)||!DesktopDisplays.GetWindowRect(window,out var rect))return true;
            uint side=SideFromBounds(display.Bounds,rect,3);
            result.Add(new(display,side,window){Source="Window geometry"});return true;
        },IntPtr.Zero);return result.ToArray();
    }
    internal static uint SideFromBounds(Native.Rect monitor,Native.Rect window,uint fallback)
    {
        bool horizontal=window.Right-window.Left>=window.Bottom-window.Top;
        long first=horizontal?Math.Abs((long)window.Top-monitor.Top):Math.Abs((long)window.Left-monitor.Left),last=horizontal?Math.Abs((long)window.Bottom-monitor.Bottom):Math.Abs((long)window.Right-monitor.Right);
        return first==last?fallback:horizontal?(first<last?1u:3u):(first<last?0u:2u);
    }
    internal static Native.Rect StripBounds(Native.Rect bounds,uint side,int pixels=2)=>side switch{
        0=>new(){Left=bounds.Left,Top=bounds.Top,Right=bounds.Left+pixels,Bottom=bounds.Bottom},
        1=>new(){Left=bounds.Left,Top=bounds.Top,Right=bounds.Right,Bottom=bounds.Top+pixels},
        2=>new(){Left=bounds.Right-pixels,Top=bounds.Top,Right=bounds.Right,Bottom=bounds.Bottom},
        _=>new(){Left=bounds.Left,Top=bounds.Bottom-pixels,Right=bounds.Right,Bottom=bounds.Bottom}};
    internal void Refresh()
    {
        try{RefreshCore();Error=null;}
        catch(Exception error){Error="Taskbar edge blocking is unavailable: "+error.Message;foreach(var strip in strips.Values)strip.Dispose();strips.Clear();}
    }
    void RefreshCore()
    {
        if(disposed)return;var edges=prefs.BlockTaskbarEdgeReveal?Edges():Array.Empty<Edge>();var keys=edges.Select(edge=>(edge.Display.Name,edge.Side)).ToHashSet();
        foreach(var key in strips.Keys.Where(key=>!keys.Contains(key)).ToArray()){strips[key].Dispose();strips.Remove(key);}
        var foreground=Native.GetForegroundWindow();bool fullscreen=DesktopDisplays.IsFullscreen(foreground);var activeMonitor=DesktopDisplays.MonitorFromWindow(foreground,2);
        foreach(var edge in edges)
        {
            var key=(edge.Display.Name,edge.Side);if(!strips.TryGetValue(key,out var strip)){strip=new Strip();strips.Add(key,strip);}
            strip.Update(StripBounds(edge.Display.Bounds,edge.Side),!(fullscreen&&edge.Display.Handle==activeMonitor));
        }
    }
    void QueueRefresh(){if(disposed||queued||owner.Dispatcher.HasShutdownStarted)return;queued=true;owner.Dispatcher.BeginInvoke(new Action(()=>{queued=false;Refresh();}));}
    IntPtr Hook(IntPtr window,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if((uint)message==recreated||message is 0x007E or 0x02E0 or 0x001A){foreach(var strip in strips.Values)strip.Invalidate();QueueRefresh();}return IntPtr.Zero;
    }
    public void Dispose(){if(disposed)return;disposed=true;ownerSource.RemoveHook(Hook);if(foregroundHook!=IntPtr.Zero)UnhookWinEvent(foregroundHook);foreach(var strip in strips.Values)strip.Dispose();strips.Clear();}
    internal sealed class Strip:IDisposable
    {
        static readonly WindowProc callback=Procedure;static readonly Lazy<ushort> registered=new(()=>{var windowClass=new WindowClass{Proc=Marshal.GetFunctionPointerForDelegate(callback),Instance=GetModuleHandle(null),Background=GetStockObject(4),Name="NewStart.TaskbarEdgeShield"};return RegisterClass(ref windowClass);});
        readonly IntPtr handle;Native.Rect previous;bool? shown;
        internal IntPtr Handle=>handle;
        internal Strip()
        {
            if(registered.Value==0)throw new InvalidOperationException("Windows could not register the taskbar edge shield.");
            handle=CreateWindowEx(0x08080088,"NewStart.TaskbarEdgeShield","",0x80000000,0,0,1,1,IntPtr.Zero,IntPtr.Zero,GetModuleHandle(null),IntPtr.Zero);
            if(handle==IntPtr.Zero)throw new InvalidOperationException("Windows could not create the taskbar edge shield.");
            // Alpha zero passes input through layered HWNDs. One alpha step is effectively invisible but hit-testable.
            if(!SetLayeredWindowAttributes(handle,0,1,2)){DestroyWindow(handle);throw new InvalidOperationException("Windows could not make the taskbar edge shield transparent.");}
        }
        static IntPtr Procedure(IntPtr window,uint message,IntPtr wParam,IntPtr lParam)
        {
            if(message==0x0084)return new IntPtr(1); // HTCLIENT, never HTTRANSPARENT
            if(message==0x0021)return new IntPtr(4); // MA_NOACTIVATEANDEAT
            if(message is >=0x0201 and <=0x020E)return IntPtr.Zero;
            return DefWindowProc(window,message,wParam,lParam);
        }
        internal void Update(Native.Rect rect,bool visible)
        {
            if(shown==visible&&previous.Left==rect.Left&&previous.Top==rect.Top&&previous.Right==rect.Right&&previous.Bottom==rect.Bottom)return;
            DesktopDisplays.SetWindowPos(handle,new IntPtr(-1),rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top,0x0010|(visible?0x0040u:0x0080u));previous=rect;shown=visible;
        }
        internal void Invalidate()=>shown=null;
        public void Dispose(){if(ActiveApplication.IsWindow(handle))DestroyWindow(handle);}
    }
}
