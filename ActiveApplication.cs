using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace NewStart;
internal record ActiveWindow(IntPtr Handle,uint ProcessId,long Started,string Path,string Title,string Class,long Style,bool Maximized,bool Fullscreen)
{
    internal string Name {get;init;}="";
    internal string AppId {get;init;}="";
    internal string IconTarget {get;init;}="";
    internal uint ContentProcessId {get;init;}
}
internal sealed class ActiveApplication:IDisposable
{
    internal delegate void EventProc(IntPtr hook,uint kind,IntPtr window,int objectId,int child,uint thread,uint time);
    [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,EventProc callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")]internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr hwnd,StringBuilder text,int size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int size);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]internal static extern IntPtr Style(IntPtr hwnd,int index);
    readonly EventProc callback;readonly List<IntPtr> hooks=new();readonly Dispatcher dispatcher;bool queued,disposed;int generation;
    readonly HashSet<IntPtr> watched=new();
    internal ActiveWindow? Current {get;private set;}
    internal long Notifications {get;private set;}
    internal long Refreshes {get;private set;}
    internal event Action? Changed;
    internal event Action? WindowStateChanged;
    internal event Action? ContextInvalidated;
    internal void Watch(IntPtr window)=>watched.Add(window);
    internal void Unwatch(IntPtr window)=>watched.Remove(window);
    internal ActiveApplication(Dispatcher ui)
    {
        dispatcher=ui;callback=OnEvent;
        foreach(var range in new[]{(3u,3u),(0x0016u,0x0017u),(0x8001u,0x8005u),(0x800Au,0x800Cu)})
        {var hook=SetWinEventHook(range.Item1,range.Item2,IntPtr.Zero,callback,0,0,2);if(hook!=IntPtr.Zero)hooks.Add(hook);}
        Refresh();
    }
    void OnEvent(IntPtr hook,uint kind,IntPtr window,int objectId,int child,uint thread,uint time)
    {
        Notifications++;
        if(disposed||queued)return;
        bool relevant=window==Current?.Handle||GetAncestor(window,2)==Current?.Handle;
        if(kind!=3&&!relevant&&!watched.Contains(window))return;
        queued=true;dispatcher.BeginInvoke(new Action(()=>{queued=false;if(!disposed){Refresh();WindowStateChanged?.Invoke();if(relevant)ContextInvalidated?.Invoke();}}),DispatcherPriority.Background);
    }
    void Refresh()
    {
        Refreshes++;
        var foreground=Native.GetForegroundWindow();GetWindowThreadProcessId(foreground,out uint pid);
        bool own=pid==Environment.ProcessId;
        var next=Read(own?Current?.Handle??IntPtr.Zero:foreground);int version=++generation;
        if(own&&next!=null&&Native.IsIconic(next.Handle))next=null; // Retain only a live, usable external target while operating the AppBar.
        if(next!=null&&Current is {} old&&old.Handle==next.Handle&&old.ProcessId==next.ProcessId&&old.Started==next.Started)next=next with{Name=old.Name,AppId=old.AppId,IconTarget=old.IconTarget,ContentProcessId=old.ContentProcessId,Path=old.Path};
        if(next!=Current){Current=next;Changed?.Invoke();}
        if(next!=null&&next.Name.Length==0)_=Enrich(next,version);
    }
    async System.Threading.Tasks.Task Enrich(ActiveWindow window,int version)
    {
        ActiveWindow result;
        try{result=await System.Threading.Tasks.Task.Run(()=>ApplicationIdentity.Enrich(window));}
        catch{result=window with{Name=window.Title,IconTarget=window.Path};}
        if(!disposed&&version==generation&&Current?.Handle==window.Handle){Current=result;Changed?.Invoke();}
    }
    internal static ActiveWindow? Read(IntPtr handle,bool includeHidden=false)
    {
        if(handle==IntPtr.Zero||!IsWindow(handle)||(!includeHidden&&!Native.IsWindowVisible(handle)))return null;
        GetWindowThreadProcessId(handle,out uint pid);if(pid==Environment.ProcessId)return null;
        var name=new StringBuilder(256);GetClassName(handle,name,256);
        if(name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "tooltips_class32" or "Xaml_WindowedPopupClass")return null;
        if(!includeHidden&&DwmGetWindowAttribute(handle,14,out int cloaked,4)==0&&cloaked!=0)return null;
        try
        {
            var info=ApplicationIdentity.ProcessInfo(pid);
            if(!includeHidden&&System.IO.Path.GetFileName(info.Path).ToLowerInvariant() is "textinputhost.exe" or "searchhost.exe" or "startmenuexperiencehost.exe" or "shellexperiencehost.exe" or "lockapp.exe")return null;
            var title=new StringBuilder(512);GetWindowText(handle,title,512);
            if(title.Length==0&&info.Path.Length==0&&info.AppId.Length==0&&!includeHidden)return null;
            long extended=Style(handle,-20).ToInt64();if(!includeHidden&&(extended&0x08000000)!=0)return null;
            if(!includeHidden&&(extended&0x80)!=0&&GetWindow(handle,4)==IntPtr.Zero)return null;
            return new(handle,pid,info.Started,info.Path,title.ToString(),name.ToString(),Style(handle,-16).ToInt64(),DesktopDisplays.IsZoomed(handle),DesktopDisplays.IsFullscreen(handle));
        }catch{return null;}
    }
    internal bool ValidTarget(ActiveWindow target)
    {
        var foreground=Native.GetForegroundWindow();GetWindowThreadProcessId(foreground,out uint foregroundProcess);
        if(foreground!=target.Handle&&(foregroundProcess!=Environment.ProcessId||Current?.Handle!=target.Handle))return false;
        return target.Started!=0&&Read(target.Handle) is {} live&&live.ProcessId==target.ProcessId&&live.Started==target.Started;
    }
    public void Dispose(){disposed=true;foreach(var hook in hooks)UnhookWinEvent(hook);hooks.Clear();}
}
