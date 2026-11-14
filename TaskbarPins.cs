using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NewStart;
internal static class TaskbarPins
{
    static string[] current=Array.Empty<string>();static Task? pending;static DateTime checkedAt;
    internal static IReadOnlyList<string> Current=>Volatile.Read(ref current);
    internal static void Refresh()
    {
        if(pending is {IsCompleted:false}||(DateTime.UtcNow-checkedAt).TotalSeconds<15)return;
        checkedAt=DateTime.UtcNow;var completion=new TaskCompletionSource();pending=completion.Task;
        var thread=new Thread(()=>{try{Volatile.Write(ref current,Read());completion.SetResult();}catch{completion.SetResult();}}){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();
    }
    static int Find(byte[] data,string text)
    {
        byte[] needle=Encoding.Unicode.GetBytes(text);
        for(int i=0;i<=data.Length-needle.Length;i++){int j=0;while(j<needle.Length&&data[i+j]==needle[j])j++;if(j==needle.Length)return i;}
        return int.MaxValue;
    }
    static string[] Read()
    {
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),@"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        var shortcuts=Directory.Exists(folder)?Directory.GetFiles(folder,"*.lnk"):Array.Empty<string>();
        using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband");
        // Read-only compatibility adapter: match known shell identities, never modify/decode private records.
        byte[] favorites=key?.GetValue("Favorites") as byte[]??Array.Empty<byte>();
        byte[] resolved=key?.GetValue("FavoritesResolve") as byte[]??Array.Empty<byte>();
        var entries=new List<(string Target,int Order)>();var coveredIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object? shell=null,namespaceFolder=null;
        try
        {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);namespaceFolder=((dynamic)shell!).NameSpace(folder);
            foreach(string link in shortcuts)
            {
                string id="";object? item=null;
                try{if(namespaceFolder!=null){item=((dynamic)namespaceFolder).ParseName(Path.GetFileName(link));id=((dynamic)item!).ExtendedProperty("System.AppUserModel.ID") as string??"";}}catch{}
                finally{if(item!=null)Marshal.ReleaseComObject(item);}
                if(id.Length>0)coveredIds.Add(id);
                int order=Math.Min(Find(favorites,Path.GetFileName(link)),id.Length>0?Find(favorites,id):int.MaxValue);
                if(order==int.MaxValue){int fallback=Find(resolved,Path.GetFileName(link));if(fallback<int.MaxValue)order=favorites.Length+fallback;}
                entries.Add((link,order));
            }
        }finally{if(namespaceFolder!=null)Marshal.ReleaseComObject(namespaceFolder);if(shell!=null)Marshal.ReleaseComObject(shell);}
        foreach(var app in StartCatalog.Current?.Apps??new())
        {
            const string prefix="shell:AppsFolder\\";if(!app.Target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))continue;
            string id=app.Target[prefix.Length..];if(coveredIds.Contains(id))continue;
            int order=Find(favorites,id);if(order<int.MaxValue){entries.Add((app.Target,order));coveredIds.Add(id);}
        }
        return entries.OrderBy(e=>e.Order).ThenBy(e=>e.Target,StringComparer.CurrentCultureIgnoreCase).Select(e=>e.Target).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
