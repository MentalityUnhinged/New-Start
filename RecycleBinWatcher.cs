using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NewStart;
internal sealed class RecycleBinWatcher:IDisposable
{
    const int Message=0x8000+73;
    [StructLayout(LayoutKind.Sequential)]struct Entry{public IntPtr Pidl;[MarshalAs(UnmanagedType.Bool)]public bool Recursive;}
    [DllImport("shell32.dll")]static extern int SHGetSpecialFolderLocation(IntPtr owner,int folder,out IntPtr pidl);
    [DllImport("shell32.dll")]static extern uint SHChangeNotifyRegister(IntPtr hwnd,int sources,int events,uint message,int count,ref Entry entry);
    [DllImport("shell32.dll")]static extern bool SHChangeNotifyDeregister(uint registration);
    [DllImport("shell32.dll")]static extern IntPtr SHChangeNotification_Lock(IntPtr change,uint process,out IntPtr pidls,out int events);
    [DllImport("shell32.dll")]static extern bool SHChangeNotification_Unlock(IntPtr notification);
    readonly HwndSource source;IntPtr pidl;uint registration;bool disposed;
    internal RecycleBinWatcher(Window owner)
    {
        source=HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle)!;
        if(SHGetSpecialFolderLocation(source.Handle,10,out pidl)<0)return;
        var entry=new Entry{Pidl=pidl,Recursive=true};
        registration=SHChangeNotifyRegister(source.Handle,0x8003,0x7FFFFFFF,Message,1,ref entry);
        if(registration!=0)source.AddHook(Hook);
    }
    IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message!=Message||disposed)return IntPtr.Zero;
        var notification=SHChangeNotification_Lock(wParam,unchecked((uint)lParam.ToInt64()),out _,out _);
        if(notification!=IntPtr.Zero)SHChangeNotification_Unlock(notification);
        RecycleBinIcon.Refresh(true);handled=true;return IntPtr.Zero;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(registration!=0){SHChangeNotifyDeregister(registration);source.RemoveHook(Hook);registration=0;}
        if(pidl!=IntPtr.Zero){Marshal.FreeCoTaskMem(pidl);pidl=IntPtr.Zero;}
    }
}
