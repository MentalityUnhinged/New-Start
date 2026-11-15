using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace NewStart;
internal static class TaskbarSuppressionValidation
{
    static string Signal(string token)=>Path.Combine(Preferences.Folder,"taskbar-test-"+token+".json");
    internal static async Task CrashFixture(string token)
    {
        var owner=new Window();new WindowInteropHelper(owner).EnsureHandle();using var controller=new TaskbarSuppression(owner,new Preferences{BlockTaskbarEdgeReveal=true});
        bool hidden=await Until(()=>{controller.Refresh();return controller.Ready&&TaskbarSuppression.Windows().All(window=>!Native.IsWindowVisible(window));},8000);
        File.WriteAllText(Signal(token),JsonSerializer.Serialize(new{Ready=hidden,controller.Error}));
        while(true){await Task.Delay(1000);controller.Refresh();}
    }
    internal static async Task Run()
    {
        var checks=new Dictionary<string,bool>();var details=new List<object>();var owner=new Window();new WindowInteropHelper(owner).EnsureHandle();TaskbarSuppression? controller=null;Process? crash=null;string token=Guid.NewGuid().ToString("N");
        var original=TaskbarSuppression.Windows().Where(Native.IsWindowVisible).ToArray();var preferences=new Preferences{BlockTaskbarEdgeReveal=true};
        try
        {
            if(original.Length==0)throw new InvalidOperationException("No visible official taskbar available to verify.");
            var work=DesktopDisplays.All().ToDictionary(display=>display.Name,display=>DesktopDisplays.Work(display.Handle));
            controller=new TaskbarSuppression(owner,preferences);
            checks["RecoveryHelperReadyBeforeHiding"]=await Until(()=>controller.Ready,5000);
            checks["OfficialTaskbarActuallyHidden"]=await Until(()=>{controller.Refresh();return original.All(window=>!Native.IsWindowVisible(window));},3000);
            bool posted=original.All(window=>Native.ShowWindowAsync(window,8));await Task.Delay(250);
            checks["ExplorerRevealIsSuppressed"]=posted&&await Until(()=>original.All(window=>!Native.IsWindowVisible(window)),2000);
            controller.WindowsKeyPressed();
            checks["WindowsKeyPathRestoresTaskbar"]=await Until(()=>original.All(Native.IsWindowVisible),2000);
            checks["KeyboardFallbackRemainsAvailable"]=controller.KeyboardReveal;
            await Task.Delay(2200);controller.Refresh();
            checks["SuppressionResumesAfterKeyboardGrace"]=await Until(()=>original.All(window=>!Native.IsWindowVisible(window)),2000);
            checks["NoWorkspaceReservationChange"]=DesktopDisplays.All().All(display=>{var before=work[display.Name];var after=DesktopDisplays.Work(display.Handle);return before.Left==after.Left&&before.Top==after.Top&&before.Right==after.Right&&before.Bottom==after.Bottom;});
            preferences.BlockTaskbarEdgeReveal=false;controller.Refresh();checks["DisableRestoresOfficialTaskbar"]=await Until(()=>original.All(Native.IsWindowVisible),2000);controller.Refresh();controller.Dispose();controller=null;
            var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};info.ArgumentList.Add("--taskbar-suppression-crash-test");info.ArgumentList.Add(token);crash=Process.Start(info)!;
            bool ready=await Until(()=>File.Exists(Signal(token)),10000);
            checks["CrashFixtureHidTaskbar"]=ready&&JsonDocument.Parse(File.ReadAllText(Signal(token))).RootElement.GetProperty("Ready").GetBoolean()&&original.All(window=>!Native.IsWindowVisible(window));
            if(!crash.HasExited){crash.Kill();await crash.WaitForExitAsync();}
            checks["IndependentHelperRestoresAfterCrash"]=await Until(()=>original.All(Native.IsWindowVisible),5000);
        }
        catch(Exception error){checks["UnhandledFailure"]=false;details.Add(new{Error=error.ToString()});}
        finally
        {
            if(controller!=null){preferences.BlockTaskbarEdgeReveal=false;controller.Refresh();controller.Dispose();}
            if(crash!=null){try{if(!crash.HasExited)crash.Kill();}catch{}crash.Dispose();}
            TaskbarSuppression.Recover();await Until(()=>original.All(Native.IsWindowVisible),3000);owner.Close();try{File.Delete(Signal(token));}catch{}
            File.WriteAllText(Path.Combine(Preferences.Folder,"taskbar-suppression-verification.json"),JsonSerializer.Serialize(new{Checks=checks,Details=details},new JsonSerializerOptions{WriteIndented=true}));
        }
        if(checks.Values.Contains(false))throw new InvalidOperationException("Taskbar suppression checks failed.");
    }
    static async Task<bool> Until(Func<bool> condition,int milliseconds){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<milliseconds){if(condition())return true;await Task.Delay(50);}return condition();}
}
