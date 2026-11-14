using System;
using System.Windows;
using System.Threading;
using System.Linq;

namespace NewStart;
public static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        using var single = new Mutex(true, "NewStart.User." + Environment.UserName, out bool first);
        if (!first) return;
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
            if(Array.IndexOf(args,"--verify-reservation")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>await dock.VerifyReservation()));
            if(Array.IndexOf(args,"--verify-backdrop")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>await dock.VerifyBackdropDismissal()));
            if(Array.IndexOf(args,"--settings-preview")>=0){dock.ShowInTaskbar=true;dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(dock.Settings));}
            if(Array.IndexOf(args,"--verify-start")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>await dock.VerifyStart()));
            if(Array.IndexOf(args,"--verify-menu-performance")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>await dock.VerifyMenuPerformance()));
            if(Array.IndexOf(args,"--verify-locations")>=0)dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>await dock.VerifyLocations()));
            app.Run(dock);
        }
    }
}

