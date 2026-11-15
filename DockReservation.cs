using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace NewStart;
internal sealed class DockReservation:IDisposable
{
    [StructLayout(LayoutKind.Sequential)]internal struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct Data{public uint Size;public IntPtr Window;public uint Callback,Edge;public Rect Bounds;public IntPtr Param;}
    [StructLayout(LayoutKind.Sequential)]struct MonitorInfo{public uint Size;public Rect Monitor,Work;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
    [DllImport("shell32.dll")]static extern UIntPtr SHAppBarMessage(uint message,ref Data data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]static extern IntPtr MonitorFromPoint(Point point,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    readonly Window owner;readonly HwndSource source;readonly uint callback,recreated;
    readonly Func<IntPtr>? targetMonitor;
    bool requested,registered,updating,queued,disposed;double dockHeight;
    DockEdge edge;
    internal bool Enabled=>registered;
    internal Rect Bounds{get;private set;}
    internal static Rect WorkArea
    {
        get{var monitor=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()};if(!GetMonitorInfo(MonitorFromPoint(new Point(),1),ref monitor))throw new InvalidOperationException("Windows did not report a display work area.");return monitor.Work;}
    }
    internal event Action? Changed;
    internal DockReservation(Window dock,Func<IntPtr>? monitor=null)
    {
        targetMonitor=monitor;
        owner=dock;callback=RegisterWindowMessage("NewStart.AppBar.Callback");recreated=RegisterWindowMessage("TaskbarCreated");
        // This hidden HWND owns the screen-wide reservation; only the centred dock is interactive.
        source=new HwndSource(new HwndSourceParameters("NewStart.DockReservation"){WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x080000A0,Width=0,Height=0});
        source.AddHook(Hook);
    }
    Data Message()=>new(){Size=(uint)Marshal.SizeOf<Data>(),Window=source.Handle,Callback=callback,Edge=edge switch{DockEdge.Top=>1u,DockEdge.Left=>0u,_=>3u}};
    internal void Update(bool enabled,double height,DockEdge location=DockEdge.Bottom)
    {
        if(disposed)return;requested=enabled;dockHeight=height;
        if(edge!=location){if(registered){var remove=Message();SHAppBarMessage(1,ref remove);registered=false;}edge=location;Bounds=default;}
        if(!enabled){if(registered){var remove=Message();SHAppBarMessage(1,ref remove);registered=false;Bounds=default;Changed?.Invoke();}return;}
        if(updating)return;updating=true;
        try
        {
            if(!registered){var register=Message();registered=SHAppBarMessage(0,ref register)!=UIntPtr.Zero;if(!registered)return;Bounds=default;}
            var monitor=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()};
            if(!GetMonitorInfo(targetMonitor?.Invoke()??MonitorFromPoint(new Point(),1),ref monitor))return;
            int pixels=(int)Math.Ceiling(dockHeight*VisualTreeHelper.GetDpi(owner).DpiScaleY);
            var position=Message();position.Bounds=monitor.Monitor;SetThickness(ref position.Bounds,pixels);
            SHAppBarMessage(2,ref position);SetThickness(ref position.Bounds,pixels);
            var previous=Bounds;
            if(previous.Left==position.Bounds.Left&&previous.Top==position.Bounds.Top&&previous.Right==position.Bounds.Right&&previous.Bottom==position.Bounds.Bottom)return;
            SHAppBarMessage(3,ref position);Bounds=position.Bounds;
            SetWindowPos(source.Handle,IntPtr.Zero,Bounds.Left,Bounds.Top,Bounds.Right-Bounds.Left,Bounds.Bottom-Bounds.Top,0x0014);
            Changed?.Invoke();
        }
        finally{updating=false;}
    }
    void SetThickness(ref Rect bounds,int pixels)
    {
        if(edge==DockEdge.Left)bounds.Right=bounds.Left+pixels;
        else if(edge==DockEdge.Top)bounds.Bottom=bounds.Top+pixels;
        else bounds.Top=bounds.Bottom-pixels;
    }
    IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(disposed)return IntPtr.Zero;
        if((uint)message==recreated){registered=false;Bounds=default;QueueUpdate();}
        else if((uint)message==callback)
        {
            if(wParam.ToInt32()==1)QueueUpdate(); // ABN_POSCHANGED
            else if(wParam.ToInt32()==2)owner.Topmost=lParam==IntPtr.Zero; // fullscreen applications
        }
        else if(message==0x007E||message==0x02E0||message==0x001A)QueueUpdate();
        else if(message==0x0047&&registered){var changed=Message();SHAppBarMessage(9,ref changed);}
        return IntPtr.Zero;
    }
    void QueueUpdate()
    {
        if(queued||disposed)return;queued=true;
        owner.Dispatcher.BeginInvoke(new Action(()=>{queued=false;if(!disposed)Update(requested,dockHeight,edge);}));
    }
    public void Dispose()
    {
        if(disposed)return;if(registered){var remove=Message();SHAppBarMessage(1,ref remove);registered=false;}disposed=true;source.RemoveHook(Hook);source.Dispose();
    }
}
