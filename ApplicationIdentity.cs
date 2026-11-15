using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media;

namespace NewStart;
// Window identity is distinct from application identity: frame hosts can own a different process's content.
internal static class ApplicationIdentity
{
    [DllImport("kernel32.dll")]static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int size);
    [DllImport("kernel32.dll")]static extern bool GetProcessTimes(IntPtr process,out long created,out long exited,out long kernel,out long user);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetApplicationUserModelId(IntPtr process,ref uint length,StringBuilder? id);
    [DllImport("user32.dll")]internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr window,Native.EnumCallback callback,IntPtr data);
    [DllImport("user32.dll")]static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll",EntryPoint="GetClassLongPtrW")]static extern IntPtr GetClassLongPtr(IntPtr window,int index);
    [DllImport("user32.dll")]static extern IntPtr CopyIcon(IntPtr icon);
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
    static readonly ConcurrentDictionary<string,string> names=new(StringComparer.OrdinalIgnoreCase);
    internal static (string Path,long Started,string AppId) ProcessInfo(uint pid)
    {
        var handle=OpenProcess(0x1000,false,pid);if(handle==IntPtr.Zero)return("",0,"");
        try
        {
            var path=new StringBuilder(32768);int length=path.Capacity;QueryFullProcessImageName(handle,0,path,ref length);
            long started=GetProcessTimes(handle,out long created,out _,out _,out _)?DateTime.FromFileTimeUtc(created).Ticks:0;
            uint count=0;GetApplicationUserModelId(handle,ref count,null);string id="";
            if(count is >0 and <4096){var text=new StringBuilder((int)count);if(GetApplicationUserModelId(handle,ref count,text)==0)id=text.ToString();}
            return(path.ToString(),started,id);
        }finally{CloseHandle(handle);}
    }
    internal static ActiveWindow Enrich(ActiveWindow window)
    {
        var info=ProcessInfo(window.ProcessId);uint contentPid=window.ProcessId;
        if(window.Class=="ApplicationFrameWindow")
        {
            EnumChildWindows(window.Handle,(child,_)=>{GetWindowThreadProcessId(child,out uint childPid);if(childPid!=window.ProcessId){var candidate=ProcessInfo(childPid);if(candidate.AppId.Length>0){info=candidate;contentPid=childPid;return false;}}return true;},IntPtr.Zero);
        }
        string icon=info.AppId.Length>0?"shell:AppsFolder\\"+info.AppId:info.Path;
        string name=StartCatalog.Current?.Apps.FirstOrDefault(app=>string.Equals(app.Target,icon,StringComparison.OrdinalIgnoreCase))?.Name??"";
        if(name.Length==0&&info.Path.Length>0)name=names.GetOrAdd(info.Path,path=>{try{var version=FileVersionInfo.GetVersionInfo(path);return !string.IsNullOrWhiteSpace(version.FileDescription)?version.FileDescription:!string.IsNullOrWhiteSpace(version.ProductName)?version.ProductName:System.IO.Path.GetFileNameWithoutExtension(path);}catch{return System.IO.Path.GetFileNameWithoutExtension(path);}});
        return window with{Path=info.Path,Name=name.Length>0?name:window.Title,AppId=info.AppId,IconTarget=icon,ContentProcessId=contentPid};
    }
    internal static async Task<ImageSource?> Icon(ActiveWindow window)
    {
        if(window.AppId.Length>0&&await StartCatalog.Icon(window.IconTarget) is {} packaged)return packaged;
        // A bounded WM_GETICON respects per-window icons, including multiple windows of one executable.
        var image=await Task.Run(()=>{
            SendMessageTimeout(window.Handle,0x7F,new IntPtr(2),IntPtr.Zero,2,70,out var icon);
            if(icon==IntPtr.Zero)icon=GetClassLongPtr(window.Handle,-34);
            if(icon==IntPtr.Zero)return null;
            var copy=CopyIcon(icon);if(copy==IntPtr.Zero)return null;
            try{var bitmap=Imaging.CreateBitmapSourceFromHIcon(copy,System.Windows.Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(24,24));bitmap.Freeze();return (ImageSource)bitmap;}catch{return null;}finally{DestroyIcon(copy);}
        });
        return image??(window.IconTarget.Length>0?await StartCatalog.Icon(window.IconTarget):null);
    }
}
