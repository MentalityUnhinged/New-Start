using System;
using System.Windows;
using System.Threading;
using System.Linq;

namespace NewStart;
public static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        if(args.Length==4&&args[0]=="--taskbar-guardian"&&int.TryParse(args[1],out int taskbarOwner)&&long.TryParse(args[2],out long taskbarStarted)){TaskbarSuppression.Guard(taskbarOwner,taskbarStarted,args[3]);return;}
        if(args.Length==2&&args[0]=="--taskbar-suppression-crash-test"&&Guid.TryParseExact(args[1],"N",out _))
        {
            var suppressionFixtureApp=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};suppressionFixtureApp.Startup+=async(_,_)=>await TaskbarSuppressionValidation.CrashFixture(args[1]);suppressionFixtureApp.Run();return;
        }
        if(args.Length==1&&args[0]=="--capability-worker"){CapabilityWorker.Run();return;}
        if(args.Length==3&&args[0]=="--context-fixture"){ContextValidation.Fixture(args[1],args[2]=="slow");return;}
        if(args.Length==1&&args[0]=="--verify-capability-inventory")
        {
            var validation=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};validation.Startup+=async(_,_)=>{StartCatalog.Initialize();try{await ContextValidation.Inventory();validation.Shutdown();}catch{validation.Shutdown(1);}};validation.Run();return;
        }
        if(args.Length==1&&args[0]=="--verify-capability-core")
        {
            var validation=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};validation.Startup+=async(_,_)=>{try{await ContextValidation.Core(validation.Dispatcher);validation.Shutdown();}catch{validation.Shutdown(1);}};validation.Run();return;
        }
        if(args.Length==1&&args[0]=="--borderless-fixture"){BorderlessFixture.Run();return;}
        if(args.Length==2&&args[0]=="--borderless-crash-test"&&long.TryParse(args[1],out long fixture)){BorderlessFixture.CrashTest(new IntPtr(fixture));return;}
        if(args.Length==3&&args[0]=="--borderless-guardian"&&int.TryParse(args[1],out int parent)&&long.TryParse(args[2],out long started)){BorderlessManager.Guard(parent,started);return;}
        using var single = new Mutex(true, "NewStart.User." + Environment.UserName, out bool first);
        if (!first) return;
        BorderlessManager.Recover();
        TaskbarSuppression.Recover();
        if(args.Length==1&&args[0]=="--verify-taskbar-suppression")
        {
            var validation=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};validation.Startup+=async(_,_)=>{try{await TaskbarSuppressionValidation.Run();validation.Shutdown();}catch{validation.Shutdown(1);}};validation.Run();return;
        }
        if(args.Length==1&&args[0]=="--verify-context-appearance")
        {
            var validation=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            validation.Startup+=async(_,_)=>{try{StartCatalog.Initialize();await ContextValidation.Run(validation.Dispatcher);validation.Shutdown();}catch{validation.Shutdown(1);}};validation.Run();return;
        }
        if(Array.IndexOf(args,"--verify-pin-model")>=0){PinDragValidation.Run();return;}
        if(Array.IndexOf(args,"--verify-audit")>=0){var validationApp=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};AuditValidation.Run();return;}
        try{Preferences.MigrateLegacy();}catch(System.IO.IOException){}catch(UnauthorizedAccessException){}
        var app = new Application();
        StartCatalog.Initialize();
        AccountIdentity.Initialize();
        if(Array.IndexOf(args,"--start-preview")>=0)
        {
            var preferences=Preferences.Load();
            int transparency=Array.IndexOf(args,"--transparency");
            if(transparency>=0&&transparency+1<args.Length&&double.TryParse(args[transparency+1],out double value))preferences.StartTransparency=Math.Clamp(value/100,0,1);
            var clock=System.Diagnostics.Stopwatch.StartNew();
            var menu=new StartMenuWindow(preferences,Array.IndexOf(args,"--sections-preview")>=0){ShowInTaskbar=true};
            menu.ContentRendered+=(_,_)=>{
                clock.Stop();var snapshot=StartCatalog.Current;
                var metrics=new{FirstFrameMilliseconds=clock.Elapsed.TotalMilliseconds,Apps=snapshot?.Apps.Count,Pins=snapshot?.Pins.Count,PinnedIconsReady=snapshot?.Pins.Count(a=>StartCatalog.CachedIcon(a.Target)!=null)};
                System.IO.File.WriteAllText(System.IO.Path.Combine(Preferences.Folder,"start-preview-metrics.json"),System.Text.Json.JsonSerializer.Serialize(metrics));
            };
            app.Run(menu);
        }
        else
        {
            var dock=new DockWindow{Title="New Start (Beta)"};
            if(Array.IndexOf(args,"--dock-preview")>=0)dock.ShowInTaskbar=true;
            if(Array.IndexOf(args,"--verify-appbar")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>await dock.VerifyAppBar()));
            if(Array.IndexOf(args,"--verify-borderless")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>await dock.VerifyBorderless()));
            if(Array.IndexOf(args,"--appbar-preview")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(dock.PreviewAppBar));
            if(Array.IndexOf(args,"--verify-reservation")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>await dock.VerifyReservation()));
            if(Array.IndexOf(args,"--verify-backdrop")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>{await dock.VerifyBackdropDismissal();if(Array.IndexOf(args,"--exit-after-verification")>=0)dock.Close();}));
            if(Array.IndexOf(args,"--settings-preview")>=0){dock.ShowInTaskbar=true;dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(dock.Settings));}
            if(Array.IndexOf(args,"--start-actions-preview")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(dock.PreviewStartActions));
            if(Array.IndexOf(args,"--verify-start")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>await dock.VerifyStart()));
            if(Array.IndexOf(args,"--verify-menu-performance")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>await dock.VerifyMenuPerformance()));
            if(Array.IndexOf(args,"--verify-locations")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>await dock.VerifyLocations()));
            if(Array.IndexOf(args,"--verify-dock-glass")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>await dock.VerifyDockGlass()));
            app.Run(dock);
        }
    }
}

