using System;
using System.Collections.Generic;
using System.Linq;
namespace NewStart;
internal static class ApplicationActivation
{
    internal static Native.TaskWindow? Choose(IReadOnlyList<Native.TaskWindow> windows,IntPtr foreground)=>windows.FirstOrDefault(window=>window.Handle==foreground)??windows.FirstOrDefault(window=>!Native.IsIconic(window.Handle))??windows.FirstOrDefault();
    internal static bool Toggle(IReadOnlyList<Native.TaskWindow> windows,IntPtr foreground)
    {
        var selected=Choose(windows,foreground);if(selected==null||!ActiveApplication.IsWindow(selected.Handle))return false;
        ApplicationIdentity.GetWindowThreadProcessId(selected.Handle,out uint pid);if(selected.ProcessId>0&&(pid!=selected.ProcessId||selected.Started>0&&ApplicationIdentity.ProcessInfo(pid).Started!=selected.Started))return false;
        if(selected.Handle==foreground&&!Native.IsIconic(selected.Handle))return Native.ShowWindowAsync(selected.Handle,6);
        if(Native.IsIconic(selected.Handle))Native.ShowWindowAsync(selected.Handle,9);return Native.SetForegroundWindow(selected.Handle);
    }
}
