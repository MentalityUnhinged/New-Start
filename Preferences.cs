using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace NewStart;
public sealed class Preferences
{
    public double IconSize { get; set; } = 36;
    public DockEdge Edge {get;set;}=DockEdge.Bottom;
    public DockAlignment Alignment {get;set;}=DockAlignment.Centre;
    public int Theme { get; set; } // System, Light, Dark
    public double StartTransparency { get; set; } = .8;
    public double MenuBlur { get; set; } = 16;
    public bool DockBackground {get;set;}=false;
    public bool AppBarEnabled {get;set;}
    public double? AppBarTransparency {get;set;}
    public double? AppBarBlur {get;set;}
    public bool AppBarAllMonitors {get;set;}=true;
    public bool AppBarMediaHover {get;set;}=true;
    public bool AppBarAnimations {get;set;}=true;
    public int AppBarAnimationMilliseconds {get;set;}=180;
    public bool AppBarClock {get;set;}=true;
    public bool AppBarDate {get;set;}
    public bool AppBarBatteryPercentage {get;set;}
    public int AppBarClockFormat {get;set;} // System, 12-hour, 24-hour
    public bool BorderlessWindows {get;set;}
    public List<string> BorderlessExclusions {get;set;}=new();
    public DockEdge AppBarPreviousEdge {get;set;}=DockEdge.Bottom;
    public DockAlignment AppBarPreviousAlignment {get;set;}=DockAlignment.Centre;
    public bool ShowSystemControls { get; set; } = false;
    public bool ShowTaskView { get; set; } = true;
    public bool ShowDesktop { get; set; } = true;
    public bool ReserveDockSpace { get; set; } = true;
    public bool FollowWindowsTaskbarPins { get; set; } = true;
    public List<string> HiddenTaskbarPins {get;set;}=new();
    public List<string> HiddenStartPins {get;set;}=new();
    public List<string> StartPins {get;set;}=new();
    public double Magnification { get; set; } = 1.25;
    public double HoverMilliseconds { get; set; } = 150;
    public bool Hover { get; set; } = true;
    public bool Bounce { get; set; } = true;
    public double BounceHeight { get; set; } = 10;
    public double BounceMilliseconds { get; set; } = 650;
    public int Clock { get; set; }
    public bool Indicators { get; set; } = true;
    public List<string> Pins { get; set; } = new();
    public List<string> DockPinOrder {get;set;}=new();
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewStart");
    public static void MigrateLegacy()
    {
        string legacy=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MinimalDock");
        if(Directory.Exists(Folder)||!Directory.Exists(legacy))return;
        Directory.CreateDirectory(Folder);
        foreach(var file in Directory.EnumerateFiles(legacy,"*",SearchOption.AllDirectories))
        {
            string destination=Path.Combine(Folder,Path.GetRelativePath(legacy,file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if(!File.Exists(destination))File.Copy(file,destination);
        }
    }
    public static Preferences Load()
    {
        try {
            var p = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(Folder,"settings.json"))) ?? new();
            p.Normalize();return p;
        } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    internal void Normalize()
    {
        static double Number(double value,double min,double max,double fallback,int digits=0)=>Math.Round(Math.Clamp(double.IsFinite(value)?value:fallback,min,max),digits);
        static List<string> Paths(List<string>? values)=>(values??new()).Where(value=>!string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Magnification=Number(Magnification,1,1.75,1.25,2);IconSize=Number(IconSize,16,64,36);
        Theme=Math.Clamp(Theme,0,2);Clock=Math.Clamp(Clock,0,2);
        AppBarAnimationMilliseconds=Math.Clamp(AppBarAnimationMilliseconds,0,600);AppBarClockFormat=Math.Clamp(AppBarClockFormat,0,2);
        if(AppBarTransparency.HasValue)AppBarTransparency=Number(AppBarTransparency.Value,0,1,StartTransparency,2);
        if(AppBarBlur.HasValue)AppBarBlur=Number(AppBarBlur.Value,0,60,MenuBlur,2);
        BorderlessExclusions=Paths(BorderlessExclusions);
        if(!Enum.IsDefined(AppBarPreviousEdge))AppBarPreviousEdge=DockEdge.Bottom;
        if(!Enum.IsDefined(AppBarPreviousAlignment))AppBarPreviousAlignment=DockAlignment.Centre;
        if(!Enum.IsDefined(Edge))Edge=DockEdge.Bottom;
        if(!Enum.IsDefined(Alignment))Alignment=DockAlignment.Centre;
        StartTransparency=Number(StartTransparency,0,1,.8,2);MenuBlur=Number(MenuBlur,0,60,16,2);
        HoverMilliseconds=Number(HoverMilliseconds,0,1000,150);BounceHeight=Number(BounceHeight,0,30,10);BounceMilliseconds=Number(BounceMilliseconds,100,2000,650);
        Pins=Paths(Pins);HiddenTaskbarPins=Paths(HiddenTaskbarPins);DockPinOrder=Paths(DockPinOrder);HiddenStartPins=Paths(HiddenStartPins);StartPins=Paths(StartPins);
    }
    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var path=Path.Combine(Folder,"settings.json");
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(path+".tmp",path,true);
    }
}

