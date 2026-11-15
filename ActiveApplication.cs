using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace NewStart;
internal record ActiveWindow(IntPtr Handle,uint ProcessId,long Started,string Path,string Title,string Class,long Style,bool Maximized,bool Fullscreen);
internal sealed class ActiveApplication:IDisposable
{
    internal delegate void EventProc(IntPtr hook,uint kind,IntPtr window,int objectId,int child,uint thread,uint time);
    [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,EventProc callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")]internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr hwnd,StringBuilder text,int size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int size);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]internal static extern IntPtr Style(IntPtr hwnd,int index);
    readonly EventProc callback;readonly List<IntPtr> hooks=new();readonly Dispatcher dispatcher;bool queued,disposed;
    readonly HashSet<IntPtr> watched=new();
    internal ActiveWindow? Current {get;private set;}
    internal long Notifications {get;private set;}
    internal long Refreshes {get;private set;}
    internal event Action? Changed;
    internal event Action? WindowStateChanged;
    internal void Watch(IntPtr window)=>watched.Add(window);
    internal void Unwatch(IntPtr window)=>watched.Remove(window);
    internal ActiveApplication(Dispatcher ui)
    {
        dispatcher=ui;callback=OnEvent;
        foreach(var range in new[]{(3u,3u),(0x0016u,0x0017u),(0x8001u,0x8001u),(0x800Bu,0x800Cu)})
        {var hook=SetWinEventHook(range.Item1,range.Item2,IntPtr.Zero,callback,0,0,2);if(hook!=IntPtr.Zero)hooks.Add(hook);}
        Refresh();
    }
    void OnEvent(IntPtr hook,uint kind,IntPtr window,int objectId,int child,uint thread,uint time)
    {
        Notifications++;
        if(disposed||objectId!=0||child!=0||queued)return;
        if(kind!=3&&window!=Current?.Handle&&!watched.Contains(window))return;
        queued=true;dispatcher.BeginInvoke(new Action(()=>{queued=false;if(!disposed){Refresh();WindowStateChanged?.Invoke();}}),DispatcherPriority.Background);
    }
    void Refresh()
    {
        Refreshes++;
        var foreground=Native.GetForegroundWindow();GetWindowThreadProcessId(foreground,out uint pid);
        if(pid==Environment.ProcessId){if(Current!=null&&!IsWindow(Current.Handle)){Current=null;Changed?.Invoke();}return;} // Preserve a live external target while operating the AppBar.
        var next=Read(foreground);
        if(next!=Current){Current=next;Changed?.Invoke();}
    }
    internal static ActiveWindow? Read(IntPtr handle,bool includeHidden=false)
    {
        if(handle==IntPtr.Zero||!IsWindow(handle)||(!includeHidden&&!Native.IsWindowVisible(handle)))return null;
        GetWindowThreadProcessId(handle,out uint pid);if(pid==Environment.ProcessId)return null;
        var name=new StringBuilder(256);GetClassName(handle,name,256);
        if(name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow")return null;
        try
        {
            using var process=Process.GetProcessById((int)pid);string path=process.MainModule?.FileName??"";
            if(path.Length==0)return null;
            var title=new StringBuilder(512);GetWindowText(handle,title,512);
            return new(handle,pid,process.StartTime.ToUniversalTime().Ticks,path,title.ToString(),name.ToString(),Style(handle,-16).ToInt64(),DesktopDisplays.IsZoomed(handle),DesktopDisplays.IsFullscreen(handle));
        }catch{return null;}
    }
    internal bool ValidTarget(ActiveWindow target)
    {
        var foreground=Native.GetForegroundWindow();GetWindowThreadProcessId(foreground,out uint foregroundProcess);
        if(foreground!=target.Handle&&(foregroundProcess!=Environment.ProcessId||Current?.Handle!=target.Handle))return false;
        return Read(target.Handle) is {} live&&live.ProcessId==target.ProcessId&&live.Started==target.Started;
    }
    public void Dispose(){disposed=true;foreach(var hook in hooks)UnhookWinEvent(hook);hooks.Clear();}
}
