using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NewStart;
internal static class ContextValidation
{
    internal static async Task Core(Dispatcher ui)
    {
        var checks=new Dictionary<string,bool>();Process? fixture=null,slow=null;string token=Guid.NewGuid().ToString("N");
        try
        {
            using(var tracker=new ActiveApplication(ui)){var actual=ActiveApplication.Read(Native.GetForegroundWindow());checks["CurrentForegroundSnapshot"]=tracker.Current?.Handle==actual?.Handle;}
            fixture=Launch("--context-fixture",token,"normal");var windows=await FixtureWindows(fixture,2);checks["MultipleWindows"]=windows.Length==2;checks["CustomFrameWithoutTitleRecognised"]=windows.Any(item=>item.Title.Length==0&&(item.Style&0x00C00000)==0);var window=windows.First(item=>item.Title.StartsWith("New Start capability fixture",StringComparison.Ordinal));
            var request=new CapabilityRequest(window.Handle.ToInt64(),window.ProcessId,window.Started,Environment.ProcessId,"discover",null);var reply=await ApplicationCapabilities.RunWorker(request,CancellationToken.None,6000);
            checks["MtaInvokeDiscovery"]=reply.Actions.Any(action=>action.Kind==CapabilityKind.Back&&action.Method=="Invoke");checks["MtaSearchDiscovery"]=reply.Actions.Any(action=>action.Kind==CapabilityKind.Search&&action.Method=="Focus");checks["DisabledOmitted"]=reply.Actions.All(action=>action.Kind!=CapabilityKind.Undo);
            var stale=await ApplicationCapabilities.RunWorker(request with{Started=window.Started+1},CancellationToken.None);checks["CreationIdentityMismatchRejected"]=stale.Actions.Count==0&&stale.Inspected==0&&stale.Status=="The application window has changed.";
            using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();var watch=Stopwatch.StartNew();var result=await ApplicationCapabilities.RunWorker(request,cancelled.Token);checks["CancelledWorkDoesNotStart"]=watch.ElapsedMilliseconds<250&&result.Actions.Count==0;}
            slow=Launch("--context-fixture",token,"slow");var slowWindows=await FixtureWindows(slow,1);var blocked=slowWindows.First(item=>item.Title.StartsWith("New Start capability fixture",StringComparison.Ordinal));var clock=Stopwatch.StartNew();var timeout=await ApplicationCapabilities.RunWorker(new(blocked.Handle.ToInt64(),blocked.ProcessId,blocked.Started,Environment.ProcessId,"discover",null),CancellationToken.None,900);checks["HungMtaProviderKilled"]=clock.ElapsedMilliseconds<2500&&timeout.Status.Contains("timed out",StringComparison.Ordinal);
        }catch(Exception error){checks["Failure_"+error.GetType().Name]=false;}
        finally{foreach(var process in new[]{fixture,slow})if(process!=null){try{if(!process.HasExited)process.Kill(true);}catch{}process.Dispose();}File.WriteAllText(Path.Combine(Preferences.Folder,"capability-core-verification.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));}
        if(checks.Values.Contains(false))throw new InvalidOperationException("Capability core checks failed.");
    }
    internal static async Task Inventory()
    {
        var observations=new List<object>();var live=new List<ActiveWindow>();Native.EnumWindows((window,_)=>{if(ActiveApplication.Read(window) is {} item)live.Add(item);return true;},IntPtr.Zero);
        foreach(var app in live.Where(app=>new[]{"explorer.exe","chrome.exe","msedge.exe","ChatGPT.exe","SystemSettings.exe","ApplicationFrameHost.exe","WhatsApp.Root.exe"}.Contains(Path.GetFileName(app.Path),StringComparer.OrdinalIgnoreCase)).GroupBy(app=>app.Handle).Select(group=>group.First()))
        {
            var identity=ApplicationIdentity.Enrich(app);var available=await ApplicationCapabilities.RunWorker(new(app.Handle.ToInt64(),app.ProcessId,app.Started,Environment.ProcessId,"discover",null),CancellationToken.None);
            var icon=await ApplicationIdentity.Icon(identity);observations.Add(new{Application=Path.GetFileName(identity.Path),identity.Name,Packaged=identity.AppId.Length>0,WindowClass=identity.Class,IconAvailable=icon!=null,Capabilities=available.Actions.Select(action=>new{action.Kind,action.Method,action.Source}),available.Status,available.Inspected});
        }
        File.WriteAllText(Path.Combine(Preferences.Folder,"capability-inventory-verification.json"),JsonSerializer.Serialize(observations,new JsonSerializerOptions{WriteIndented=true}));
    }
    internal static void Fixture(string token,bool slow)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        using var form=new FixtureForm(slow){Text="New Start capability fixture "+token,Width=520,Height=240,StartPosition=System.Windows.Forms.FormStartPosition.CenterScreen};
        int clicks=0;var label=new System.Windows.Forms.Label{Text="No action",Left=20,Top=90,Width=300};form.Controls.Add(label);
        foreach(var item in new[]{("Back",20),("Save",130),("Copy",240)}){var button=new System.Windows.Forms.Button{Text=item.Item1,AccessibleName=item.Item1,Left=item.Item2,Top=20,Width=95};button.Click+=(_,_)=>{clicks++;label.Text="Executed "+button.Text;File.WriteAllText(Path.Combine(Preferences.Folder,"fixture-"+token+".txt"),button.Text+"|"+clicks);};form.Controls.Add(button);}
        form.Controls.Add(new System.Windows.Forms.Button{Text="Undo",AccessibleName="Undo",Enabled=false,Top=140,Left=20});
        form.Controls.Add(new System.Windows.Forms.TextBox{AccessibleName="Search",Left=20,Top=60,Width=300});
        using var second=new System.Windows.Forms.Form{Text="",AccessibleName="New Start frameless fixture "+token,FormBorderStyle=System.Windows.Forms.FormBorderStyle.None,Width=240,Height=150};form.Shown+=(_,_)=>second.Show();
        System.Windows.Forms.Application.Run(form);
    }
    sealed class FixtureForm: System.Windows.Forms.Form
    {
        readonly bool slow;internal FixtureForm(bool value)=>slow=value;
        protected override void WndProc(ref System.Windows.Forms.Message message){if(slow&&message.Msg==0x3D)Thread.Sleep(10000);base.WndProc(ref message);}
    }
    internal static async Task Run(Dispatcher ui)
    {
        var run=new System.Windows.Controls.Button{Content="Run checks",Margin=new Thickness(24)};var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);run.Click+=(_,_)=>{run.IsEnabled=false;ready.TrySetResult();};
        var anchor=new Window{Title="New Start verification",Width=360,Height=160,WindowStartupLocation=WindowStartupLocation.CenterScreen,Content=run};anchor.Show();
        // Real input gives the test host foreground permission; do not bypass Windows focus restrictions.
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(120));
        var checks=new Dictionary<string,bool>();var observations=new List<object>();var previous=Native.GetForegroundWindow();Process? fixture=null,slow=null;string token=Guid.NewGuid().ToString("N");
        try
        {
            var preferences=new Preferences{Theme=2,DockBackground=true,DockTransparency=0,AppBarTransparency=0};
            checks["SystemDarkOpaqueBlack"]=SurfaceAppearance.Background(preferences,false)==Colors.Black&&SurfaceAppearance.Background(preferences,true)==Colors.Black;
            preferences.Theme=1;checks["SystemLightOpaqueWhite"]=SurfaceAppearance.Background(preferences,false)==Colors.White&&SurfaceAppearance.Background(preferences,true)==Colors.White;
            preferences.DockColourSource=1;preferences.DockColour="#8040C0";preferences.AppBarColourSource=1;preferences.AppBarColour="#102030";preferences.DockTransparency=.5;
            var purple=Color.FromRgb(128,64,192);var fill=(SolidColorBrush)SurfaceAppearance.Material(purple,.5).Tint;
            checks["TintRetainsColourAndCorrectAlpha"]=fill.Color==purple&&fill.Opacity==.5;
            checks["OpaqueAndClearExtremes"]=SurfaceAppearance.Material(purple,0).Tint.Opacity==1&&SurfaceAppearance.Material(purple,1).Tint.Opacity==0;
            checks["IndependentComponents"]=SurfaceAppearance.Background(preferences,false)==purple&&SurfaceAppearance.Background(preferences,true)==Color.FromRgb(16,32,48)&&SurfaceAppearance.Transparency(preferences,true)==0;
            checks["ContrastBlackAndWhite"]=SurfaceAppearance.WhiteInk(Colors.Black)&&!SurfaceAppearance.WhiteInk(Colors.White);
            var roundtrip=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences))!;roundtrip.Normalize();checks["AppearancePersists"]=roundtrip.DockColour=="#8040C0"&&roundtrip.DockTransparency==.5&&roundtrip.AppBarColour=="#102030";
            var legacy=new Preferences{StartTransparency=.33,MenuBlur=12};legacy.Normalize();checks["LegacySettingsPreserved"]=legacy.DockTransparency==.33&&legacy.DockBlur==12;
            var malformed=new Preferences{DockColour="invalid",AppBarColourSource=900,DockTransparency=double.NaN};malformed.Normalize();checks["InvalidSettingsRecover"]=malformed.DockColour=="#000000"&&malformed.AppBarColourSource==3&&double.IsFinite(malformed.DockTransparency!.Value);
            var samples=Enumerable.Repeat(Color.FromRgb(35,20,65),800).Concat(Enumerable.Repeat(Color.FromRgb(220,70,20),200)).ToArray();var palette=WallpaperPalette.Extract(samples)!;
            checks["DominantClustering"]=palette.Dominant==samples[0];checks["AccentClustering"]=palette.Accent==samples[^1];checks["PaletteDerivedOnlyFromImage"]=palette.Colours.All(samples.Contains)&&palette.Colours.Length==2;
            checks["EmptyWallpaperSafe"]=WallpaperPalette.Extract(Array.Empty<Color>())==null;
            var mono=WallpaperPalette.Extract(Enumerable.Repeat(Colors.Purple,100).ToArray())!;checks["SingleColourNotFabricated"]=mono.Colours.Length==1&&mono.Accent==Colors.Purple;
            checks["UnknownCommandNotInvented"]=CapabilityRegistry.Kind("Mystery command")==null;
            checks["ShortcutSuffixOnlyNormalisesName"]=CapabilityRegistry.Kind("&Save\tCtrl+S")==CapabilityKind.Save;
            using(var wallpapers=new WallpaperPalette(ui))
            {
                for(int attempt=0;attempt<100&&wallpapers.Busy;attempt++)await Task.Delay(50);
                observations.Add(new{Wallpaper=wallpapers.Status,Monitors=DesktopDisplays.All().Select(display=>new{display.Name,Palette=wallpapers.ForMonitor(display.Name)?.Colours.Select(SurfaceAppearance.Hex)})});
                string imagePath=Path.Combine(Preferences.Folder,"palette-fixture-"+token+".png");
                try
                {
                    void Image(Color colour){var pixels=Enumerable.Range(0,100).SelectMany(_=>new byte[]{colour.B,colour.G,colour.R,255}).ToArray();var image=System.Windows.Media.Imaging.BitmapSource.Create(10,10,96,96,PixelFormats.Bgra32,null,pixels,40);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using var output=File.Create(imagePath);encoder.Save(output);}
                    Image(Colors.Purple);int before=wallpapers.Analyses;var firstPalette=wallpapers.Analyse(imagePath);var secondPalette=wallpapers.Analyse(imagePath);
                    checks["WallpaperFileAnalysed"]=firstPalette?.Dominant==Colors.Purple;
                    checks["WallpaperAnalysisCached"]=wallpapers.Analyses==before+1&&ReferenceEquals(firstPalette,secondPalette);
                    Image(Colors.Orange);File.SetLastWriteTimeUtc(imagePath,DateTime.UtcNow.AddSeconds(1));checks["WallpaperFileChangeInvalidates"]=wallpapers.Analyse(imagePath)?.Dominant==Colors.Orange;
                    checks["MissingWallpaperSafe"]=wallpapers.Analyse(imagePath+".missing")==null;
                }finally{File.Delete(imagePath);}
            }
            fixture=Launch("--context-fixture",token,"normal");
            var windows=await FixtureWindows(fixture,2);checks["TwoWindowsSameProcess"]=windows.Length>=2;
            var first=windows.FirstOrDefault(window=>window.Title.StartsWith("New Start capability fixture",StringComparison.Ordinal));
            if(first==null)throw new InvalidOperationException("Fixture did not expose its window.");
            var enriched=ApplicationIdentity.Enrich(first);checks["Win32Identity"]=enriched.Path.Length>0&&enriched.Name.Length>0&&enriched.Started>0;
            var reply=await ApplicationCapabilities.RunWorker(new(first.Handle.ToInt64(),first.ProcessId,first.Started,Environment.ProcessId,"discover",null),CancellationToken.None,6000);
            observations.Add(new{Fixture=reply});checks["BoundedDiscovery"]=reply.Inspected<=300;
            checks["InvokeDiscovered"]=reply.Actions.Any(action=>action.Kind==CapabilityKind.Back&&action.Method=="Invoke");
            checks["SearchFocusDiscovered"]=reply.Actions.Any(action=>action.Kind==CapabilityKind.Search&&action.Method=="Focus");
            checks["DisabledActionNotShown"]=reply.Actions.All(action=>action.Kind!=CapabilityKind.Undo);
            using(var tracker=new ActiveApplication(ui))
            {
                bool activated=Native.SetForegroundWindow(first.Handle);await Task.Delay(500);checks["ForegroundTracked"]=tracker.Current?.Handle==first.Handle;observations.Add(new{FirstActivated=activated,Foreground=Native.GetForegroundWindow().ToInt64(),Expected=first.Handle.ToInt64(),Tracked=tracker.Current?.Handle.ToInt64()});
                var second=windows.First(window=>window.Handle!=first.Handle);Native.SetForegroundWindow(second.Handle);await Task.Delay(500);checks["SecondWindowTracked"]=tracker.Current?.Handle==second.Handle;
                var back=reply.Actions.FirstOrDefault(action=>action.Kind==CapabilityKind.Back);
                if(back!=null)
                {
                    var rejected=await ApplicationCapabilities.RunWorker(new(first.Handle.ToInt64(),first.ProcessId,first.Started,Environment.ProcessId,"execute",back),CancellationToken.None);checks["WrongForegroundRejected"]=!rejected.Success&&!File.Exists(Path.Combine(Preferences.Folder,"fixture-"+token+".txt"));
                    Native.SetForegroundWindow(first.Handle);await Task.Delay(150);
                    var executed=await ApplicationCapabilities.RunWorker(new(first.Handle.ToInt64(),first.ProcessId,first.Started,Environment.ProcessId,"execute",back),CancellationToken.None,6000);
                    checks["RealInvokeExecuted"]=executed.Success&&File.Exists(Path.Combine(Preferences.Folder,"fixture-"+token+".txt"));observations.Add(new{Execution=executed.Status});
                }
                Native.CloseWindow(first.Handle);await Task.Delay(600);checks["ClosedWindowNotRetained"]=tracker.Current?.Handle!=first.Handle&&!ActiveApplication.IsWindow(first.Handle);
                var stale=await ApplicationCapabilities.RunWorker(new(first.Handle.ToInt64(),first.ProcessId,first.Started,Environment.ProcessId,"discover",null),CancellationToken.None);checks["StaleIdentityRejected"]=stale.Inspected==0&&stale.Actions.Count==0&&stale.Status=="The application window has changed.";
            }
            slow=Launch("--context-fixture",token,"slow");var slowWindows=await FixtureWindows(slow,1);var slowWindow=slowWindows.FirstOrDefault(window=>window.Title.StartsWith("New Start capability fixture",StringComparison.Ordinal));
            if(slowWindow!=null){var clock=Stopwatch.StartNew();var timeout=await ApplicationCapabilities.RunWorker(new(slowWindow.Handle.ToInt64(),slowWindow.ProcessId,slowWindow.Started,Environment.ProcessId,"discover",null),CancellationToken.None,900);checks["HungProviderBounded"]=clock.ElapsedMilliseconds<2500&&!timeout.Success;observations.Add(new{HungStatus=timeout.Status,Elapsed=clock.ElapsedMilliseconds});}
            // Read-only inventory; never execute actions against personal applications.
            var live=new List<ActiveWindow>();Native.EnumWindows((window,_)=>{if(ActiveApplication.Read(window) is {} item)live.Add(item);return true;},IntPtr.Zero);
            foreach(var app in live.Where(app=>new[]{"explorer.exe","chrome.exe","msedge.exe","ChatGPT.exe","SystemSettings.exe","WhatsApp.Root.exe"}.Contains(Path.GetFileName(app.Path),StringComparer.OrdinalIgnoreCase)).GroupBy(app=>app.ProcessId).Select(group=>group.First()))
            {
                var identity=ApplicationIdentity.Enrich(app);var available=await ApplicationCapabilities.RunWorker(new(app.Handle.ToInt64(),app.ProcessId,app.Started,Environment.ProcessId,"discover",null),CancellationToken.None);
                observations.Add(new{Application=Path.GetFileName(identity.Path),identity.Name,Packaged=identity.AppId.Length>0,WindowClass=identity.Class,Capabilities=available.Actions.Select(action=>new{action.Kind,action.Method,action.Source}),available.Status,available.Inspected});
            }
        }
        catch(Exception exception){checks["UnhandledFailure"]=false;observations.Add(new{Error=exception.ToString()});}
        finally
        {
            foreach(var process in new[]{fixture,slow})if(process!=null){try{if(!process.HasExited)process.Kill(true);}catch{}process.Dispose();}
            if(ActiveApplication.IsWindow(previous))Native.SetForegroundWindow(previous);
            string output=Path.Combine(Preferences.Folder,"context-appearance-verification.json");Directory.CreateDirectory(Preferences.Folder);File.WriteAllText(output,JsonSerializer.Serialize(new{Checks=checks,Observations=observations},new JsonSerializerOptions{WriteIndented=true}));
            try{File.Delete(Path.Combine(Preferences.Folder,"fixture-"+token+".txt"));}catch{}
            anchor.Close();
        }
        if(checks.Values.Contains(false))throw new InvalidOperationException("Context/appearance validation failed. See the private verification report.");
    }
    static Process Launch(params string[] arguments){var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};foreach(string argument in arguments)info.ArgumentList.Add(argument);return Process.Start(info)!;}
    static async Task<ActiveWindow[]> FixtureWindows(Process process,int count)
    {
        for(int attempt=0;attempt<35;attempt++){await Task.Delay(100);var windows=new List<ActiveWindow>();Native.EnumWindows((window,_)=>{ApplicationIdentity.GetWindowThreadProcessId(window,out uint pid);if(pid==process.Id&&ActiveApplication.Read(window) is {} item)windows.Add(item);return true;},IntPtr.Zero);if(windows.Count>=count)return windows.ToArray();}
        return Array.Empty<ActiveWindow>();
    }
}
