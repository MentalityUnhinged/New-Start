using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NewStart;
internal static class DevelopmentValidation
{
    internal static void Run()
    {
        string? previous=Preferences.StorageOverride;string temporary=Path.Combine(AppContext.BaseDirectory,"validation","development-"+Guid.NewGuid().ToString("N"));var checks=new Dictionary<string,bool>();
        void Suite(string name,Action<Dictionary<string,bool>> run){try{run(checks);checks[name+"SuiteCompleted"]=true;}catch{checks[name+"SuiteCompleted"]=false;}}
        try
        {
            Directory.CreateDirectory(temporary);Preferences.StorageOverride=temporary;
            Suite("Interaction",InteractionValidation.Run);Suite("Plugins",PluginValidation.Run);Suite("Power",PowerValidation.Run);Suite("Media",MediaValidation.Run);Suite("CustomIcons",CustomIconValidation.Run);Suite("DockDrag",DragValidation.Run);Suite("PinModel",PinDragValidation.Run);
        }
        finally
        {
            Preferences.StorageOverride=previous;
            string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"validation"))+Path.DirectorySeparatorChar;string resolved=Path.GetFullPath(temporary);
            if(resolved.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(resolved))try{Directory.Delete(resolved,true);}catch{checks["TemporaryValidationCleanup"]=false;}
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"development-verification.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
        }
        if(checks.Values.Any(passed=>!passed))throw new InvalidOperationException("Development regression checks failed.");
    }
}
