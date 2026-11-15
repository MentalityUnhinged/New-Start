using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;

namespace NewStart;
internal static class AuditValidation
{
    internal static async Task MeasureMenu(DockWindow dock,Button button,Preferences prefs)
    {
        using var process=Process.GetCurrentProcess();var menu=button.ContextMenu!;
        await Task.Delay(1000);process.Refresh();double idleStart=process.TotalProcessorTime.TotalSeconds;
        await Task.Delay(2000);process.Refresh();double idleCpu=process.TotalProcessorTime.TotalSeconds-idleStart;
        menu.PlacementTarget=button;menu.IsOpen=true;await Task.Delay(500);
        process.Refresh();double menuStart=process.TotalProcessorTime.TotalSeconds;long positionStart=ThemedMenus.PositionUpdates;
        await Task.Delay(2000);process.Refresh();double menuCpu=process.TotalProcessorTime.TotalSeconds-menuStart;
        int recentHeadings=menu.Items.OfType<MenuItem>().Count(item=>item.Header?.ToString()=="Recent");
        long positionUpdates=ThemedMenus.PositionUpdates-positionStart;
        menu.IsOpen=false;double originalBlur=prefs.MenuBlur;prefs.MenuBlur=0;
        double clearCpu;
        try{menu.IsOpen=true;await Task.Delay(500);process.Refresh();double clearStart=process.TotalProcessorTime.TotalSeconds;await Task.Delay(2000);process.Refresh();clearCpu=process.TotalProcessorTime.TotalSeconds-clearStart;}
        finally{menu.IsOpen=false;prefs.MenuBlur=originalBlur;}
        menu.IsOpen=false;await Task.Delay(100);process.Refresh();int before=process.HandleCount;
        for(int cycle=0;cycle<12;cycle++){menu.IsOpen=true;await Task.Delay(50);menu.IsOpen=false;await Task.Delay(50);}
        await Task.Delay(300);process.Refresh();
        var report=new{SampleSeconds=2,IdleCpuSeconds=idleCpu,OpenMenuCpuSeconds=menuCpu,ZeroBlurMenuCpuSeconds=clearCpu,UnchangedMenuPositionUpdates=positionUpdates,RenderTier=System.Windows.Media.RenderCapability.Tier>>16,RecentHeadings=recentHeadings,ReopenCycles=12,HandlesBefore=before,HandlesAfter=process.HandleCount,PrivateMemoryMB=Math.Round(process.PrivateMemorySize64/1048576d,1)};
        File.WriteAllText(Path.Combine(Preferences.Folder,"menu-performance-verification.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        dock.Close();
    }
    internal static void Run()
    {
        var checks=new Dictionary<string,bool>();
        checks["NoBatteryHidesMeter"]=!BatteryReading.FromStatus(true,128,255,1).Present;
        checks["UnknownBatteryDoesNotInventDevice"]=!BatteryReading.FromStatus(true,255,255,255).Present;
        checks["FailedPowerQueryHidesMeter"]=!BatteryReading.FromStatus(false,0,0,0).Present;
        var charging=BatteryReading.FromStatus(true,8,73,1);
        checks["BatteryLevelAndChargingPreserved"]=charging.Present&&charging.Percentage==73&&charging.Charging&&charging.PluggedIn;
        checks["UnknownBatteryLevelIsNotZero"]=BatteryReading.FromStatus(true,1,255,0) is {Present:true,Percentage:null};
        var prefs=JsonSerializer.Deserialize<Preferences>("""{"IconSize":17.8,"Theme":99,"Clock":-1,"Pins":[null,"","C:\\Apps\\One.exe","c:\\apps\\one.exe"],"HiddenTaskbarPins":null,"DockPinOrder":[null," "]}""")!;
        prefs.Magnification=double.NaN;prefs.BounceHeight=double.PositiveInfinity;prefs.Normalize();
        checks["MalformedPreferencesRecover"]=prefs.IconSize==18&&prefs.Theme==2&&prefs.Clock==0&&prefs.Magnification==1.25&&prefs.BounceHeight==10;
        checks["InvalidAndDuplicatePinsRemoved"]=prefs.Pins.Count==1&&prefs.HiddenTaskbarPins.Count==0&&prefs.DockPinOrder.Count==0;
        string missing=Path.Combine(Path.GetTempPath(),"NewStart-audit-missing-"+Guid.NewGuid().ToString("N")+".exe");
        var shared=new System.Collections.Concurrent.ConcurrentBag<Task<System.Windows.Media.ImageSource?>>();
        System.Threading.Tasks.Parallel.For(0,24,_=>shared.Add(StartCatalog.Icon(missing)));
        checks["ConcurrentIconLoadsShareOneTask"]=shared.All(task=>ReferenceEquals(task,shared.First()));
        checks["MissingIconCompletesSafely"]=Task.WaitAll(shared.ToArray(),5000)&&shared.All(task=>task.Result==null);
        var recent=Enumerable.Range(0,24).Select(_=>JumpListReader.Read(missing)).ToArray();
        checks["RecentRequestsShareOneTask"]=recent.All(task=>ReferenceEquals(task,recent[0]));
        checks["UnknownRecentIdentityCompletesSafely"]=recent[0].Wait(5000)&&recent[0].Result.Count==0;
        var first=new Window();var second=new Window();
        Appearance.Apply(first,false);Appearance.Apply(second,true);Appearance.Apply(first,true);
        checks["ThemeStylesSharedWithoutAccumulation"]=first.Resources.MergedDictionaries.Count==1&&ReferenceEquals(first.Resources.MergedDictionaries[0],second.Resources.MergedDictionaries[0]);
        var menu=ThemedMenus.Create(prefs);var otherMenu=ThemedMenus.Create(prefs);
        checks["MenuStylesShared"]=ReferenceEquals(menu.Resources.MergedDictionaries[0],otherMenu.Resources.MergedDictionaries[0]);
        first.Close();second.Close();
        BackgroundLinkValidation.Run(checks);
        Directory.CreateDirectory(Preferences.Folder);
        File.WriteAllText(Path.Combine(Preferences.Folder,"audit-verification.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
        if(checks.Values.Any(passed=>!passed))throw new InvalidOperationException("Audit regression checks failed.");
    }
}
