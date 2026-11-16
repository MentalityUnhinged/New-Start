using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace NewStart;
internal static class DesktopValidation
{
    // Separate profile and no normal-startup recovery: a running user's shell/profile is not adopted.
    internal static void Run(bool interactive=false)
    {
        string? previous=Preferences.StorageOverride;string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"validation"));string profile=Path.Combine(root,"appbar-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(profile);Preferences.StorageOverride=profile;
            var prefs=new Preferences{BlockTaskbarEdgeReveal=false,BorderlessWindows=false,ShowDockStart=true,ShowAppBarStart=true,AppBarApplicationMenu=true};prefs.Normalize();prefs.Save();
            ScrollPolicy.Install();StartCatalog.Initialize();AccountIdentity.Initialize();var app=new Application();var dock=new DockWindow{Title="New Start isolated verification",ShowInTaskbar=true};
            dock.Loaded+=(_,_)=>dock.Dispatcher.BeginInvoke(new Action(async()=>{if(interactive)dock.PreviewAppBar();else await dock.VerifyAppBar();}),DispatcherPriority.ContextIdle);
            app.Run(dock);
            string report=Path.Combine(profile,"appbar-verification.json");if(File.Exists(report)){File.Copy(report,Path.Combine(AppContext.BaseDirectory,"appbar-verification.json"),true);if(!interactive){using var result=JsonDocument.Parse(File.ReadAllText(report));if(!result.RootElement.TryGetProperty("Checks",out var checks)||checks.EnumerateObject().Any(check=>check.Value.ValueKind!=JsonValueKind.True))Environment.ExitCode=1;}}else if(!interactive)Environment.ExitCode=1;
        }
        finally
        {
            Preferences.StorageOverride=previous;string resolved=Path.GetFullPath(profile);if(resolved.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(resolved).StartsWith("appbar-",StringComparison.Ordinal)&&Directory.Exists(resolved))try{Directory.Delete(resolved,true);}catch{}
        }
    }
}
