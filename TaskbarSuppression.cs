using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace NewStart;
// Hides Explorer's taskbar while idle; Windows-key input is observed, never consumed or remapped.
internal sealed class TaskbarSuppression:IDisposable
{
    internal record SavedWindow(long Handle,uint Process,long Started,string Class);
    internal record RecoveryJournal(int Owner,long Started,List<SavedWindow> Windows);
    [StructLayout(LayoutKind.Sequential)]struct RawDevice{public ushort Page,Usage;public uint Flags;public IntPtr Window;}
    [StructLayout(LayoutKind.Sequential)]struct RawKeyboard{public uint Type,Size;public IntPtr Device,WParam;public ushort MakeCode,Flags,Reserved,VirtualKey;public uint Message,Extra;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder text,int length);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]static extern bool RegisterRawInputDevices([In]RawDevice[] devices,uint count,uint size);
    [DllImport("user32.dll")]static extern uint GetRawInputData(IntPtr input,uint command,out RawKeyboard keyboard,ref uint size,uint headerSize);
    [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,ActiveApplication.EventProc callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
    readonly Window owner;readonly Preferences prefs;readonly HwndSource source;readonly uint recreated;readonly ActiveApplication.EventProc callback;readonly List<IntPtr> hooks=new();
    readonly List<SavedWindow> saved=new();readonly long started=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
    Process? guardian;bool disposed,queued,rawInput,guardianStarting,guardianReady,keyboardReveal;DateTime graceUntil;string? lastJournal;
    internal string? Error {get;private set;}
    internal int Count=>saved.Count;
    internal bool Ready=>guardianReady;
    internal bool KeyboardReveal=>keyboardReveal;
    static string Journal=>Path.Combine(Preferences.Folder,"taskbar-recovery.json");
    internal TaskbarSuppression(Window window,Preferences preferences)
    {
        owner=window;prefs=preferences;source=HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle);recreated=RegisterWindowMessage("TaskbarCreated");source.AddHook(Hook);
        callback=(_,kind,window,objectId,_,_,_)=>{if(kind==3||objectId==0&&Official(window))QueueRefresh();};
        foreach(var range in new[]{(3u,3u),(0x8001u,0x8003u)}){var hook=SetWinEventHook(range.Item1,range.Item2,IntPtr.Zero,callback,0,0,0);if(hook!=IntPtr.Zero)hooks.Add(hook);}
        Refresh();
    }
    static string Class(IntPtr window){var text=new StringBuilder(128);GetClassName(window,text,text.Capacity);return text.ToString();}
    internal static bool Official(IntPtr window)
    {
        if(Class(window) is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd"))return false;
        ApplicationIdentity.GetWindowThreadProcessId(window,out uint pid);return Path.GetFileName(ApplicationIdentity.ProcessInfo(pid).Path).Equals("explorer.exe",StringComparison.OrdinalIgnoreCase);
    }
    internal static IntPtr[] Windows(){var result=new List<IntPtr>();Native.EnumWindows((window,_)=>{if(Official(window))result.Add(window);return true;},IntPtr.Zero);return result.ToArray();}
    bool RegisterKeyboard(bool enabled)
    {
        if(rawInput==enabled)return true;
        bool accepted=RegisterRawInputDevices(new[]{new RawDevice{Page=1,Usage=6,Flags=enabled?0x100u:2u,Window=enabled?source.Handle:IntPtr.Zero}},1,(uint)Marshal.SizeOf<RawDevice>());
        if(accepted)rawInput=enabled;return accepted;
    }
    internal void WindowsKeyPressed()
    {
        if(disposed||!prefs.BlockTaskbarEdgeReveal)return;
        keyboardReveal=true;graceUntil=DateTime.UtcNow.AddSeconds(2);RestoreAll(false);
    }
    static bool NativeShellActive()
    {
        var foreground=Native.GetForegroundWindow();if(Official(foreground)||Class(foreground)=="#32768")return true;
        ApplicationIdentity.GetWindowThreadProcessId(foreground,out uint pid);
        if(Path.GetFileName(ApplicationIdentity.ProcessInfo(pid).Path).Equals("StartMenuExperienceHost.exe",StringComparison.OrdinalIgnoreCase))return true;
        // Some Start versions activate a proxy: only visible, uncloaked Start-host windows count.
        bool start=false;Native.EnumWindows((window,_)=>{
            if(!Native.IsWindowVisible(window)||Class(window)!="Windows.UI.Core.CoreWindow"||DwmGetWindowAttribute(window,14,out int cloaked,4)==0&&cloaked!=0)return true;
            ApplicationIdentity.GetWindowThreadProcessId(window,out uint process);
            if(Path.GetFileName(ApplicationIdentity.ProcessInfo(process).Path).Equals("StartMenuExperienceHost.exe",StringComparison.OrdinalIgnoreCase)){start=true;return false;}return true;
        },IntPtr.Zero);return start;
    }
    internal void Refresh()
    {
        if(disposed)return;
        try
        {
            if(!prefs.BlockTaskbarEdgeReveal){RegisterKeyboard(false);keyboardReveal=false;RestoreAll(true);Error=null;return;}
            if(!RegisterKeyboard(true))throw new InvalidOperationException("Windows-key observation could not be registered; the Windows taskbar was left available.");
            if(!guardianReady||guardian is {HasExited:true}){guardianReady=false;StartGuardian();return;}
            if(keyboardReveal)
            {
                if(NativeShellActive())graceUntil=DateTime.UtcNow.AddSeconds(1);
                if(DateTime.UtcNow<graceUntil){RestoreAll(false);return;}
                keyboardReveal=false;
            }
            saved.RemoveAll(item=>!Same(item));
            var windows=Windows();
            foreach(var window in windows)
            {
                if(!Native.IsWindowVisible(window))continue;
                if(!saved.Any(item=>item.Handle==window.ToInt64()))
                {
                    ApplicationIdentity.GetWindowThreadProcessId(window,out uint pid);long created=ApplicationIdentity.ProcessInfo(pid).Started;
                    if(created==0)continue;saved.Add(new(window.ToInt64(),pid,created,Class(window)));Persist(); // Durable before mutation.
                }
                if(!Native.ShowWindowAsync(window,0))throw new InvalidOperationException("Explorer did not accept hiding its taskbar.");
            }
            Error=null;
        }catch(Exception error){Error="Taskbar suppression is unavailable: "+error.Message;RestoreAll(false);}
    }
    async void StartGuardian()
    {
        if(guardianStarting||disposed)return;guardianStarting=true;
        try
        {
            Persist();string name="Local\\NewStart.TaskbarReady."+Environment.ProcessId+"."+Guid.NewGuid().ToString("N");
            using var ready=new EventWaitHandle(false,EventResetMode.ManualReset,name);
            var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
            foreach(string argument in new[]{"--taskbar-guardian",Environment.ProcessId.ToString(),started.ToString(),name})info.ArgumentList.Add(argument);
            guardian?.Dispose();guardian=Process.Start(info);
            bool accepted=await Task.Run(()=>ready.WaitOne(3000));
            if(disposed)return;if(!accepted||guardian==null||guardian.HasExited)throw new InvalidOperationException("The taskbar recovery helper did not become ready.");
            guardianReady=true;QueueRefresh();
        }catch(Exception error){if(!disposed){Error=error.Message;RestoreAll(false);}}
        finally{guardianStarting=false;}
    }
    sealed class JournalLock:IDisposable
    {
        readonly Mutex mutex=new(false,"NewStart.TaskbarJournal."+Environment.UserName);
        internal JournalLock(){try{mutex.WaitOne();}catch(AbandonedMutexException){}}
        public void Dispose(){mutex.ReleaseMutex();mutex.Dispose();}
    }
    void Persist()
    {
        string json=JsonSerializer.Serialize(new RecoveryJournal(Environment.ProcessId,started,saved));if(json==lastJournal)return;
        using var journalLock=new JournalLock();Directory.CreateDirectory(Preferences.Folder);File.WriteAllText(Journal+".tmp",json);File.Move(Journal+".tmp",Journal,true);lastJournal=json;
    }
    static bool Same(SavedWindow item)
    {
        var window=new IntPtr(item.Handle);if(!ActiveApplication.IsWindow(window)||Class(window)!=item.Class||!Official(window))return false;
        ApplicationIdentity.GetWindowThreadProcessId(window,out uint pid);return pid==item.Process&&ApplicationIdentity.ProcessInfo(pid).Started==item.Started;
    }
    void RestoreAll(bool release)
    {
        foreach(var item in saved.ToArray())
        {
            if(!Same(item)){saved.Remove(item);continue;}
            var window=new IntPtr(item.Handle);if(!Native.IsWindowVisible(window))Native.ShowWindowAsync(window,8);
            if(release&&Native.IsWindowVisible(window))saved.Remove(item);
        }
        if(lastJournal!=null)Persist();
    }
    internal static void Recover(int? owner=null,long? created=null)
    {
        try
        {
            using var journalLock=new JournalLock();if(!File.Exists(Journal))return;
            var journal=JsonSerializer.Deserialize<RecoveryJournal>(File.ReadAllText(Journal));if(journal==null||owner.HasValue&&(journal.Owner!=owner||journal.Started!=created))return;
            foreach(var item in journal.Windows)if(Same(item))Native.ShowWindowAsync(new IntPtr(item.Handle),8);
            var remaining=journal.Windows.Where(item=>Same(item)&&!Native.IsWindowVisible(new IntPtr(item.Handle))).ToList();
            File.WriteAllText(Journal+".tmp",JsonSerializer.Serialize(journal with{Windows=remaining}));File.Move(Journal+".tmp",Journal,true);
        }catch{ /* Explorer itself remains a fallback if the recovery file cannot be read. */ }
    }
    internal static void Guard(int owner,long created,string ready)
    {
        try
        {
            using var parent=Process.GetProcessById(owner);if(parent.StartTime.ToUniversalTime().Ticks!=created)return;
            using(var signal=EventWaitHandle.OpenExisting(ready))signal.Set();
            parent.WaitForExit();for(int retry=0;retry<5;retry++){Recover(owner,created);Thread.Sleep(100);}
        }catch(ArgumentException){Recover(owner,created);}catch{}
    }
    void QueueRefresh(){if(disposed||queued||owner.Dispatcher.HasShutdownStarted)return;queued=true;owner.Dispatcher.BeginInvoke(new Action(()=>{queued=false;Refresh();}));}
    IntPtr Hook(IntPtr window,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==0x00FF&&rawInput)
        {
            uint size=(uint)Marshal.SizeOf<RawKeyboard>();
            if(GetRawInputData(lParam,0x10000003,out var input,ref size,(uint)(8+2*IntPtr.Size))!=uint.MaxValue&&input.Type==1&&input.VirtualKey is 0x5B or 0x5C&&input.Message is 0x100 or 0x104)WindowsKeyPressed();
        }
        if((uint)message==recreated||message is 0x007E or 0x02E0 or 0x001A)QueueRefresh();return IntPtr.Zero;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;RegisterKeyboard(false);source.RemoveHook(Hook);foreach(var hook in hooks)UnhookWinEvent(hook);hooks.Clear();RestoreAll(true);
        // The independent helper restores any remaining entries after this process exits, including crashes.
        if(saved.Count==0){try{if(guardian is {HasExited:false})guardian.Kill();}catch{}}guardian?.Dispose();guardian=null;
    }
}
