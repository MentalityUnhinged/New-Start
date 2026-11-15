using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace NewStart;
internal sealed class BorderlessManager:IDisposable
{
    const long Caption=0x00C00000,ThickFrame=0x00040000;
    internal record SavedWindow(long Handle,uint ProcessId,long Started,string Path,string Class,long OriginalStyle);
    internal record RecoveryJournal(int Owner,long Started,List<SavedWindow> Windows);
    static string Journal=>System.IO.Path.Combine(Preferences.Folder,"borderless-recovery.json");
    readonly Preferences prefs;readonly ActiveApplication active;readonly List<SavedWindow> changed=new();
    Process? guardian;bool disposed;
    string? lastJournal;
    internal string Feedback {get;private set;}="Borderless management is off.";
    internal IntPtr DiagnosticWindow {get;set;}
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]static extern IntPtr SetStyle(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")]static extern bool IsHungAppWindow(IntPtr hwnd);
    internal BorderlessManager(Preferences preferences,ActiveApplication applications){prefs=preferences;active=applications;active.Changed+=Update;active.WindowStateChanged+=Update;}
    internal static bool Compatible(ActiveWindow window)
    {
        // Conservative allow-list: custom frames cannot be reliably inferred from WS_CAPTION.
        bool standard=window.Class=="Notepad"||window.Class.StartsWith("WindowsForms10.Window.",StringComparison.Ordinal)||window.Class=="NewStart.BorderlessFixture";
        return standard&&(window.Style&Caption)==Caption&&(window.Style&ThickFrame)!=0&&!window.Fullscreen&&window.ProcessId!=Environment.ProcessId&&!IsHungAppWindow(window.Handle);
    }
    internal void Update()
    {try{UpdateCore();}catch(Exception e){Feedback="Window management paused: "+e.Message;}}
    void UpdateCore()
    {
        if(disposed)return;
        foreach(var saved in changed.ToArray())
        {
            var live=ActiveApplication.Read(new IntPtr(saved.Handle),true);
            bool excluded=live!=null&&Excluded(live.Path);
            if(!prefs.AppBarEnabled||!prefs.BorderlessWindows||live==null||!live.Maximized||live.Fullscreen||excluded)
            {if(Restore(saved)){changed.Remove(saved);active.Unwatch(new IntPtr(saved.Handle));}}
        }
        if(!prefs.AppBarEnabled||!prefs.BorderlessWindows){Feedback=changed.Count==0?"Borderless management is off.":$"Disabled; {changed.Count} window(s) still need frame recovery. Retry recovery or close and reopen those applications.";Persist();if(changed.Count==0)StopGuardian();return;}
        var window=DiagnosticWindow==IntPtr.Zero?active.Current:ActiveApplication.Read(DiagnosticWindow);
        if(window==null||!window.Maximized||window.Fullscreen){Feedback="Only compatible maximised windows are managed.";Persist();return;}
        if(changed.Any(item=>item.Handle==window.Handle.ToInt64())){Feedback="Standard title bar managed; restoring the window restores its frame.";return;}
        if(Excluded(window.Path)||!Compatible(window)){Feedback="This application keeps its own title bar (excluded or custom frame).";Persist();return;}
        if(!StartGuardian()){Feedback="Recovery helper unavailable; window was left unchanged.";return;}
        var record=new SavedWindow(window.Handle.ToInt64(),window.ProcessId,window.Started,window.Path,window.Class,window.Style);
        changed.Add(record);
        active.Watch(window.Handle);
        try
        {
            Persist(); // Durable before changing any external window.
            Marshal.SetLastPInvokeError(0);SetStyle(window.Handle,-16,new IntPtr(window.Style&~Caption));
            if(Marshal.GetLastPInvokeError()!=0)throw new InvalidOperationException("Windows denied changing this window.");
            FrameChanged(window.Handle);
            Feedback="Standard title bar managed; original frame recorded for recovery.";
        }
        catch(Exception e){Restore(record);changed.Remove(record);active.Unwatch(window.Handle);Persist();Feedback=e.Message;}
    }
    bool Excluded(string path)=>prefs.BorderlessExclusions.Any(entry=>string.Equals(entry,path,StringComparison.OrdinalIgnoreCase)||string.Equals(entry,System.IO.Path.GetFileName(path),StringComparison.OrdinalIgnoreCase));
    bool StartGuardian()
    {
        if(guardian is {HasExited:false})return true;
        try
        {
            var executable=Environment.ProcessPath!;
            var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
            info.ArgumentList.Add("--borderless-guardian");info.ArgumentList.Add(Environment.ProcessId.ToString());info.ArgumentList.Add(Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString());
            guardian=Process.Start(info);return guardian!=null;
        }catch{return false;}
    }
    static void FrameChanged(IntPtr handle)=>DesktopDisplays.SetWindowPos(handle,IntPtr.Zero,0,0,0,0,0x4037);
    static bool Restore(SavedWindow saved)
    {
        var hwnd=new IntPtr(saved.Handle);var live=ActiveApplication.Read(hwnd,true);
        if(live==null)return !ActiveApplication.IsWindow(hwnd);
        if(live.ProcessId!=saved.ProcessId||live.Started!=saved.Started||live.Path!=saved.Path||live.Class!=saved.Class)return true;
        if(IsHungAppWindow(hwnd))return false;
        long current=ActiveApplication.Style(hwnd,-16).ToInt64();
        // Restore only the caption bits we own; preserve subsequent style changes.
        Marshal.SetLastPInvokeError(0);SetStyle(hwnd,-16,new IntPtr((current&~Caption)|(saved.OriginalStyle&Caption)));
        if(Marshal.GetLastPInvokeError()!=0)return false;
        FrameChanged(hwnd);return true;
    }
    void Persist()
    {
        if(changed.Count==0&&lastJournal==null&&!File.Exists(Journal))return;
        string json=JsonSerializer.Serialize(new RecoveryJournal(Environment.ProcessId,Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,changed));
        if(json==lastJournal)return;
        using var journalLock=LockJournal();
        Directory.CreateDirectory(Preferences.Folder);
        File.WriteAllText(Journal+".tmp",json);File.Move(Journal+".tmp",Journal,true);lastJournal=json;
    }
    sealed class JournalLock:IDisposable
    {
        readonly Mutex mutex=new(false,"NewStart.BorderlessJournal."+Environment.UserName);
        internal JournalLock(){try{mutex.WaitOne();}catch(AbandonedMutexException){}}
        public void Dispose(){mutex.ReleaseMutex();mutex.Dispose();}
    }
    static IDisposable LockJournal()=>new JournalLock();
    void StopGuardian(){try{if(guardian is {HasExited:false})guardian.Kill();}catch{}guardian?.Dispose();guardian=null;}
    internal static string Recover(int? owner=null,long? started=null)
    {
        try
        {
            using var journalLock=LockJournal();
            if(!File.Exists(Journal))return "No pending window recovery.";
            var journal=JsonSerializer.Deserialize<RecoveryJournal>(File.ReadAllText(Journal));if(journal==null)return "No pending window recovery.";
            if(owner.HasValue&&(journal.Owner!=owner||journal.Started!=started))return "Recovery belongs to a different New Start instance.";
            var remaining=journal.Windows.Where(saved=>!Restore(saved)).ToList();
            File.WriteAllText(Journal,JsonSerializer.Serialize(journal with{Windows=remaining}));
            return remaining.Count==0?"Original window frames restored.":"Some windows could not be restored; close/reopen those applications.";
        }catch(Exception e){return "Window recovery: "+e.Message;}
    }
    internal static void Guard(int processId,long started)
    {
        try{using var parent=Process.GetProcessById(processId);if(parent.StartTime.ToUniversalTime().Ticks!=started)return;parent.WaitForExit();}catch(ArgumentException){}
        catch{return;}
        Recover(processId,started);
    }
    public void Dispose()
    {
        if(disposed)return;active.Changed-=Update;active.WindowStateChanged-=Update;
        foreach(var saved in changed.ToArray()){active.Unwatch(new IntPtr(saved.Handle));if(Restore(saved))changed.Remove(saved);}
        try{Persist();}catch{}disposed=true;if(changed.Count==0)StopGuardian();else guardian?.Dispose();
    }
}
