using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
namespace NewStart;
internal static class PinIdentity
{
    internal static bool Matches(string pin,Native.TaskWindow window)=>string.Equals(Resolve(pin),window.Target,StringComparison.OrdinalIgnoreCase)||Matches(pin,window.Path);
    internal static bool Matches(string pin,string executable)
    {
        string target=Resolve(pin);if(string.Equals(target,executable,StringComparison.OrdinalIgnoreCase))return true;
        const string prefix="shell:AppsFolder\\";
        if(!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return false;
        string family=target[prefix.Length..].Split('!')[0];int separator=family.LastIndexOf('_');
        if(separator<0)return false;
        string name=family[..separator],publisher=family[(separator+1)..];
        return executable.Contains("\\WindowsApps\\"+name+"_",StringComparison.OrdinalIgnoreCase)&&executable.Contains("__"+publisher+"\\",StringComparison.OrdinalIgnoreCase);
    }
    static readonly Dictionary<string,(DateTime Stamp,string Target)> cache=new(StringComparer.OrdinalIgnoreCase);
    internal static string Resolve(string path)
    {
        if(!string.Equals(Path.GetExtension(path),".lnk",StringComparison.OrdinalIgnoreCase))return path;
        DateTime stamp=File.GetLastWriteTimeUtc(path);if(cache.TryGetValue(path,out var saved)&&saved.Stamp==stamp)return saved.Target;
        string target=path;object? shell=null,link=null;
        try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);link=((dynamic)shell!).CreateShortcut(path);string resolved=((dynamic)link).TargetPath;if(!string.IsNullOrWhiteSpace(resolved))target=Environment.ExpandEnvironmentVariables(resolved);else target=ShellIdentity(path);}
        catch{}finally{if(link!=null)Marshal.ReleaseComObject(link);if(shell!=null)Marshal.ReleaseComObject(shell);}
        cache[path]=(stamp,target);return target;
    }
    static string ShellIdentity(string path)
    {
        object? shell=null,folder=null,item=null;
        try{
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);folder=((dynamic)shell!).NameSpace(Path.GetDirectoryName(path));item=((dynamic)folder!).ParseName(Path.GetFileName(path));
            string id=((dynamic)item!).ExtendedProperty("System.AppUserModel.ID") as string??"";
            if(id=="Microsoft.Windows.Explorer")return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe");
            if(id.Length>0)return "shell:AppsFolder\\"+id;
        }catch{}
        finally{if(item!=null)Marshal.ReleaseComObject(item);if(folder!=null)Marshal.ReleaseComObject(folder);if(shell!=null)Marshal.ReleaseComObject(shell);}
        return path;
    }
}
