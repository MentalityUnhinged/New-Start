using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NewStart;
internal static class JumpListReader
{
    internal record Destination(string Name,string Target);
    static readonly BlockingCollection<Action> work=new();
    static readonly Dictionary<string,(Task<List<Destination>> Task,DateTime Expires)> cache=new(StringComparer.OrdinalIgnoreCase);
    static JumpListReader()
    {
        var worker=new Thread(()=>{foreach(var job in work.GetConsumingEnumerable())job();}){IsBackground=true,Name="New Start recent items"};
        worker.SetApartmentState(ApartmentState.STA);worker.Start();
    }
    // Only public, automatically generated Recent items are readable. Custom
    // tasks, app-defined categories and user-pinned destinations are not exposed.
    internal static Task<List<Destination>> Read(string path)
    {
        lock(cache)
        {
            if(cache.TryGetValue(path,out var saved)&&(!saved.Task.IsCompleted||saved.Expires>DateTime.UtcNow))return saved.Task;
            foreach(var key in cache.Where(pair=>pair.Value.Task.IsCompleted&&pair.Value.Expires<=DateTime.UtcNow).Select(pair=>pair.Key).ToArray())cache.Remove(key);
            if(cache.Count>=128){var oldest=cache.FirstOrDefault(pair=>pair.Value.Task.IsCompleted);if(oldest.Key!=null)cache.Remove(oldest.Key);else return Task.FromResult(new List<Destination>());}
            var completion=new TaskCompletionSource<List<Destination>>(TaskCreationOptions.RunContinuationsAsynchronously);
            cache[path]=(completion.Task,DateTime.UtcNow.AddSeconds(15));
            work.Add(()=>{try{completion.SetResult(ReadCore(path));}catch{completion.SetResult(new());}});
            return completion.Task;
        }
    }
    static List<Destination> ReadCore(string path)
    {
        string? id=null;const string prefix="shell:AppsFolder\\";
        if(path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))id=path[prefix.Length..];
        else if(Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase))
        {
            object? shell=null,folder=null,item=null;
            try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);folder=((dynamic)shell!).NameSpace(Path.GetDirectoryName(path));item=((dynamic)folder!).ParseName(Path.GetFileName(path));id=((dynamic)item!).ExtendedProperty("System.AppUserModel.ID") as string;}
            finally{Release(item);Release(folder);Release(shell);}
        }
        if(string.IsNullOrEmpty(id)&&Path.GetFileName(path).Equals("explorer.exe",StringComparison.OrdinalIgnoreCase))id="Microsoft.Windows.Explorer";
        var result=new List<Destination>();if(string.IsNullOrWhiteSpace(id))return result;
        object? lists=null,arrayObject=null;
        try
        {
            lists=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("86bec222-30f2-47e0-9f25-60d11cd75c28"))!);
            var documents=(IApplicationDocumentLists)lists!;documents.SetAppID(id);
            var arrayId=typeof(IObjectArray).GUID;documents.GetList(0,10,ref arrayId,out arrayObject);
            var array=(IObjectArray)arrayObject;array.GetCount(out uint count);
            for(uint index=0;index<count;index++)
            {
                object? entry=null;
                try{var itemId=typeof(IShellItem).GUID;array.GetAt(index,ref itemId,out entry);var item=(IShellItem)entry;item.GetDisplayName(0,out string name);item.GetDisplayName(0x80028000,out string target);if(!string.IsNullOrWhiteSpace(target))result.Add(new(name,target));}
                catch(COMException){}finally{Release(entry);}
            }
        }
        catch(COMException){}finally{Release(arrayObject);Release(lists);}
        return result;
    }
    static void Release(object? value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    [ComImport,Guid("3c594f9f-9f30-47a1-979a-c9e83d3d0a06"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IApplicationDocumentLists
    {void SetAppID([MarshalAs(UnmanagedType.LPWStr)]string id);void GetList(uint type,uint count,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object list);}
    [ComImport,Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IObjectArray
    {void GetCount(out uint count);void GetAt(uint index,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object item);}
    [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IShellItem
    {
        void BindToHandler(IntPtr context,ref Guid handler,ref Guid iid,out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind,[MarshalAs(UnmanagedType.LPWStr)]out string name);
        void GetAttributes(uint mask,out uint attributes);
        void Compare(IShellItem other,uint hint,out int order);
    }
}
