using System;
using System.Collections.Generic;
using System.Linq;

namespace NewStart;
internal sealed class DockPins
{
    internal const string DragFormat="NewStart.ApplicationPin";
    readonly Preferences prefs;readonly Func<IReadOnlyList<string>> windowsPins;
    internal DockPins(Preferences preferences,Func<IReadOnlyList<string>> imported){prefs=preferences;windowsPins=imported;}
    static bool Same(string left,string right)=>string.Equals(PinIdentity.Resolve(left),PinIdentity.Resolve(right),StringComparison.OrdinalIgnoreCase);
    internal List<string> Imported()=>prefs.FollowWindowsTaskbarPins?windowsPins().Where(p=>!prefs.HiddenTaskbarPins.Any(h=>Same(h,p))).ToList():new();
    internal List<string> All()
    {
        var pins=Imported().Concat(prefs.Pins).DistinctBy(PinIdentity.Resolve,StringComparer.OrdinalIgnoreCase);
        return pins.OrderBy(p=>{int index=prefs.DockPinOrder.FindIndex(id=>string.Equals(id,PinIdentity.Resolve(p),StringComparison.OrdinalIgnoreCase));return index<0?int.MaxValue:index;}).ToList();
    }
    internal void Place(string path,int index)
    {
        prefs.HiddenTaskbarPins.RemoveAll(p=>Same(p,path));
        var pins=All();string actual=pins.FirstOrDefault(p=>Same(p,path))??path;
        if(!pins.Any(p=>Same(p,path)))prefs.Pins.Add(path);
        pins.RemoveAll(p=>Same(p,path));pins.Insert(Math.Clamp(index,0,pins.Count),actual);
        prefs.DockPinOrder=pins.Select(PinIdentity.Resolve).ToList();
    }
    internal void Remove(string path)
    {
        foreach(var pin in windowsPins().Where(p=>Same(p,path)))if(!prefs.HiddenTaskbarPins.Contains(pin,StringComparer.OrdinalIgnoreCase))prefs.HiddenTaskbarPins.Add(pin);
        prefs.Pins.RemoveAll(p=>Same(p,path));prefs.DockPinOrder.RemoveAll(p=>Same(p,path));
    }
    internal void Move(string path,int delta)
    {
        var pins=All();int index=pins.FindIndex(p=>Same(p,path));int next=index+delta;if(index<0||next<0||next>=pins.Count)return;
        string actual=pins[index];pins.RemoveAt(index);pins.Insert(next,actual);prefs.DockPinOrder=pins.Select(PinIdentity.Resolve).ToList();
    }
}
