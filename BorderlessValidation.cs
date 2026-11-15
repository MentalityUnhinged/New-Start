using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace NewStart;
internal static class BorderlessFixture
{
    internal static void Run()
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        using var form=new System.Windows.Forms.Form{Text="New Start disposable frame test",Width=500,Height=300,StartPosition=System.Windows.Forms.FormStartPosition.CenterScreen};
        System.Windows.Forms.Application.Run(form);
    }
    internal static void CrashTest(IntPtr window)
    {
        var live=ActiveApplication.Read(window);
        if(live==null||live.Path!=Environment.ProcessPath||!live.Class.StartsWith("WindowsForms10.Window.",StringComparison.Ordinal))return;
        var app=new Application();using var tracker=new ActiveApplication(app.Dispatcher);
        var prefs=new Preferences{AppBarEnabled=true,BorderlessWindows=true};
        var manager=new BorderlessManager(prefs,tracker){DiagnosticWindow=window};manager.Update();
        File.WriteAllText(Path.Combine(Preferences.Folder,"borderless-crash-marker.json"),JsonSerializer.Serialize(new{Changed=(ActiveApplication.Style(window,-16).ToInt64()&0x00C00000)==0,manager.Feedback}));
        // Deliberately skip Dispose, validating the independent recovery helper.
        Process.GetCurrentProcess().Kill();
    }
}
public sealed partial class DockWindow
{
    internal async Task VerifyBorderless()
    {
        var checks=new Dictionary<string,bool>();Process? fixture=null;BorderlessManager? manager=null;ActiveApplication? tracker=null;
        try
        {
            var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,WindowStyle=ProcessWindowStyle.Normal};info.ArgumentList.Add("--borderless-fixture");fixture=Process.Start(info)!;
            IntPtr hwnd=IntPtr.Zero;
            for(int i=0;i<40&&hwnd==IntPtr.Zero;i++){await Task.Delay(100);fixture.Refresh();hwnd=fixture.MainWindowHandle;}
            if(hwnd==IntPtr.Zero)throw new InvalidOperationException("Disposable window was unavailable.");
            var original=ActiveApplication.Read(hwnd)!;long style=original.Style;
            checks["StandardFrameRecognized"]=BorderlessManager.Compatible(original);
            var settings=new Preferences{AppBarEnabled=true,BorderlessWindows=true};tracker=new ActiveApplication(Dispatcher);manager=new BorderlessManager(settings,tracker){DiagnosticWindow=hwnd};
            manager.Update();checks["FloatingWindowUnchanged"]=ActiveApplication.Style(hwnd,-16).ToInt64()==style;
            Native.ShowWindowAsync(hwnd,3);await Task.Delay(250);manager.Update();
            checks["MaximizedCaptionRemoved"]=(ActiveApplication.Style(hwnd,-16).ToInt64()&0x00C00000)==0;
            checks["ResizeStylePreserved"]=(ActiveApplication.Style(hwnd,-16).ToInt64()&0x00040000)==(style&0x00040000);
            Native.ShowWindowAsync(hwnd,9);await Task.Delay(200);manager.Update();
            checks["RestoreRestoresCaption"]=(ActiveApplication.Style(hwnd,-16).ToInt64()&0x00C00000)==(style&0x00C00000);
            Native.ShowWindowAsync(hwnd,3);await Task.Delay(150);manager.Update();settings.BorderlessWindows=false;manager.Update();
            checks["DisableRestoresCaption"]=(ActiveApplication.Style(hwnd,-16).ToInt64()&0x00C00000)==(style&0x00C00000);
            settings.BorderlessWindows=true;settings.BorderlessExclusions.Add(original.Path);manager.Update();
            checks["ExclusionPreventsMutation"]=(ActiveApplication.Style(hwnd,-16).ToInt64()&0x00C00000)==(style&0x00C00000);
            settings.BorderlessExclusions.Clear();manager.Update();manager.Dispose();manager=null;tracker.Dispose();tracker=null;
            checks["OrderlyShutdownRestoresCaption"]=(ActiveApplication.Style(hwnd,-16).ToInt64()&0x00C00000)==(style&0x00C00000);
            checks["ChromeCustomFrameExcluded"]=!BorderlessManager.Compatible(original with{Class="Chrome_WidgetWin_1"});
            checks["ExplorerCustomFrameExcluded"]=!BorderlessManager.Compatible(original with{Class="CabinetWClass"});
            checks["FullscreenExcluded"]=!BorderlessManager.Compatible(original with{Fullscreen=true});
            var crashInfo=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};crashInfo.ArgumentList.Add("--borderless-crash-test");crashInfo.ArgumentList.Add(hwnd.ToInt64().ToString());
            using(var crash=Process.Start(crashInfo)!){await crash.WaitForExitAsync();}
            for(int i=0;i<40&&(ActiveApplication.Style(hwnd,-16).ToInt64()&0x00C00000)!=(style&0x00C00000);i++)await Task.Delay(100);
            checks["AbnormalShutdownGuardianRestoresCaption"]=(ActiveApplication.Style(hwnd,-16).ToInt64()&0x00C00000)==(style&0x00C00000);
            var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(Preferences.Folder,"borderless-crash-marker.json")));checks["CrashFixtureActuallyModified"]=marker.RootElement.GetProperty("Changed").GetBoolean();
            // Simulate a persisted caption mutation with no helper, then exercise startup/manual recovery.
            tracker=new ActiveApplication(Dispatcher);manager=new BorderlessManager(settings,tracker){DiagnosticWindow=hwnd};manager.Update();
            BorderlessManager.Recover();checks["JournalRecoveryRestoresCaption"]=(ActiveApplication.Style(hwnd,-16).ToInt64()&0x00C00000)==(style&0x00C00000);
        }
        catch(Exception e){checks["UnhandledFailure"]=false;File.WriteAllText(Path.Combine(Preferences.Folder,"borderless-test-error.txt"),e.ToString());}
        finally
        {
            manager?.Dispose();tracker?.Dispose();
            if(fixture!=null){if(!fixture.HasExited){fixture.Refresh();if(fixture.MainWindowHandle!=IntPtr.Zero)Native.CloseWindow(fixture.MainWindowHandle);await Task.Delay(150);if(!fixture.HasExited)fixture.Kill();}fixture.Dispose();}
            File.WriteAllText(Path.Combine(Preferences.Folder,"borderless-verification.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));Close();
        }
    }
}
