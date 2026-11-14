using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;

namespace NewStart;
internal static class PinDragValidation
{
    internal static void Run()
    {
        string[] windows={@"C:\Apps\One.exe",@"C:\Apps\Two.exe"};var original=windows.ToArray();
        var prefs=new Preferences();var model=new DockPins(prefs,()=>windows);var checks=new Dictionary<string,bool>();
        model.Place(@"C:\Apps\Three.exe",1);checks["ExternalPinInsertedAtDropPosition"]=model.All().SequenceEqual(new[]{windows[0],@"C:\Apps\Three.exe",windows[1]});
        model.Place(windows[1],0);checks["ImportedPinCanBeReordered"]=model.All()[0]==windows[1];
        model.Place(@"c:\apps\THREE.exe",2);checks["DuplicateApplicationNotAdded"]=model.All().Count==3;
        model.Remove(windows[0]);checks["ImportedPinHiddenLocally"]=!model.All().Contains(windows[0])&&prefs.HiddenTaskbarPins.Contains(windows[0]);
        model.Place(windows[0],1);checks["DroppingHiddenPinRestoresIt"]=model.All()[1]==windows[0]&&!prefs.HiddenTaskbarPins.Contains(windows[0]);
        model.Remove(@"C:\Apps\Three.exe");checks["LocalPinRemoved"]=prefs.Pins.Count==0&&model.All().Count==2;
        var loaded=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(prefs))!;
        checks["PinOrderSurvivesReload"]=new DockPins(loaded,()=>windows).All().SequenceEqual(model.All());
        checks["WindowsPinsUnchanged"]=windows.SequenceEqual(original);
        Directory.CreateDirectory(Preferences.Folder);File.WriteAllText(Path.Combine(Preferences.Folder,"pin-drag-verification.json"),JsonSerializer.Serialize(checks));
        if(checks.Values.Any(value=>!value))throw new InvalidOperationException("Pin drag validation failed.");
    }
}
