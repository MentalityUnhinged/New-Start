using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NewStart;
internal record StartApp(string Name,string Target,ImageSource? Icon);
internal record StartData(List<StartApp> Apps,List<StartApp> Pins,string? Warning);
internal static class StartCatalog
{
    static readonly BlockingCollection<Action> iconQueue=new();
    static readonly ConcurrentDictionary<string,Lazy<Task<ImageSource?>>> icons=new(StringComparer.OrdinalIgnoreCase);
    static readonly object refreshLock=new();
    static Task<StartData>? refreshing;
    static DateTime refreshed;
    static StartData? current;
    static string SnapshotPath=>System.IO.Path.Combine(Preferences.Folder,"start-apps.json");
    internal static event Action? Updated;
    internal static StartData? Current=>Volatile.Read(ref current);
    internal static void Initialize()
    {
        try
        {
            var saved=JsonSerializer.Deserialize<SavedData>(File.ReadAllText(SnapshotPath));
            if(saved is {Apps:not null,Pins:not null})
            {
                static List<StartApp> Valid(List<SavedApp> values)=>values.Where(a=>a!=null&&!string.IsNullOrWhiteSpace(a.Name)&&!string.IsNullOrWhiteSpace(a.Target)).Select(a=>new StartApp(a.Name,a.Target,null)).ToList();
                current=new(Valid(saved.Apps),Valid(saved.Pins),null);
                foreach(var app in current.Pins)
                {
                    var image=ReadDiskIcon(app.Target);
                    if(image!=null)icons.TryAdd(app.Target,new Lazy<Task<ImageSource?>>(()=>Task.FromResult<ImageSource?>(image)));
                }
                WarmIcons(current);
            }
        }catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException) { }
        _=Refresh();
    }
    sealed record SavedApp(string Name,string Target);
    sealed record SavedData(List<SavedApp> Apps,List<SavedApp> Pins);
    static void WarmIcons(StartData snapshot)
    {
        // Pinned icons go first so the first page is ready before All Apps icons.
        foreach(var a in snapshot.Pins.Concat(snapshot.Apps))_=Icon(a.Target);
    }
    internal static ImageSource? CachedIcon(string target)
    {
        if(icons.TryGetValue(target,out var entry)&&entry.IsValueCreated&&entry.Value.IsCompletedSuccessfully)return entry.Value.Result;
        return null;
    }
    internal static Task<StartData> Refresh()
    {
        lock(refreshLock)
        {
            if(refreshing is {IsCompleted:false})return refreshing;
            if((DateTime.UtcNow-refreshed).TotalSeconds<30)return Task.FromResult(Current??new(new(),new(),"Apps are being prepared. Please try again shortly."));
            refreshing=RefreshCore();return refreshing;
        }
    }
    static async Task<StartData> RefreshCore()
    {
        try
        {
            var snapshot=await Load().ConfigureAwait(false);
            var previous=Current;lock(refreshLock)refreshed=DateTime.UtcNow;
            if(previous!=null&&previous.Warning==snapshot.Warning&&previous.Apps.Select(a=>(a.Name,a.Target)).SequenceEqual(snapshot.Apps.Select(a=>(a.Name,a.Target)))&&previous.Pins.Select(a=>(a.Name,a.Target)).SequenceEqual(snapshot.Pins.Select(a=>(a.Name,a.Target))))return previous;
            // Publish names immediately; icon warming never blocks opening the menu.
            Volatile.Write(ref current,snapshot);WarmIcons(snapshot);
            var saved=new SavedData(snapshot.Apps.Select(a=>new SavedApp(a.Name,a.Target)).ToList(),snapshot.Pins.Select(a=>new SavedApp(a.Name,a.Target)).ToList());
            try{File.WriteAllText(SnapshotPath+".tmp",JsonSerializer.Serialize(saved));File.Move(SnapshotPath+".tmp",SnapshotPath,true);}catch(IOException){}catch(UnauthorizedAccessException){}
            Updated?.Invoke();return snapshot;
        }
        catch
        {
            lock(refreshLock)refreshed=DateTime.UtcNow;
            return Current??new(new(),new(),"Apps are being prepared. Please try again shortly.");
        }
    }
    static StartCatalog()
    {
        var worker=new Thread(()=>{foreach(var job in iconQueue.GetConsumingEnumerable())job();}){IsBackground=true};
        worker.SetApartmentState(ApartmentState.STA);worker.Start();
    }
    internal static Task<ImageSource?> Icon(string target)=>icons.GetOrAdd(target,key=>new Lazy<Task<ImageSource?>>(()=>{
        var result=new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);iconQueue.Add(()=>{try{result.SetResult(ReadIcon(key));}catch{result.SetResult(null);}});return result.Task;
    })).Value;
    static string IconPath(string target)=>System.IO.Path.Combine(Preferences.Folder,"icons",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target)))+".png");
    static ImageSource? ReadDiskIcon(string target)
    {
        string path=IconPath(target);
        try
        {
            if(File.Exists(path)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(path)<TimeSpan.FromDays(7))
            {
                using var stream=File.OpenRead(path);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return bitmap;
            }
        }catch{}
        return null;
    }
    static ImageSource? ReadIcon(string target)
    {
        var cached=ReadDiskIcon(target);if(cached!=null)return cached;
        var image=Image(target);
        if(image is BitmapSource source)
        {
            try{Directory.CreateDirectory(System.IO.Path.GetDirectoryName(IconPath(target))!);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using var stream=File.Create(IconPath(target));encoder.Save(stream);}catch{}
        }
        return image;
    }
    internal static Task<StartData> Load()
    {
        var result=new TaskCompletionSource<StartData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker=new Thread(()=>{try{result.SetResult(Read());}catch(Exception e){result.SetException(e);}}){IsBackground=true};
        worker.SetApartmentState(ApartmentState.STA);worker.Start();return result.Task;
    }
    static StartData Read()
    {
        var apps=new List<StartApp>();
        object? shell=null,folder=null,items=null;
        try
        {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder=((dynamic)shell!).NameSpace("shell:AppsFolder");items=((dynamic)folder!).Items();
            int count=((dynamic)items).Count;
            for(int i=0;i<count;i++)
            {
                object item=((dynamic)items).Item(i);
                try{string name=((dynamic)item).Name;string id=((dynamic)item).Path;
                    string target=System.IO.Path.IsPathRooted(id)?id:"shell:AppsFolder\\"+id;
                    if(!string.IsNullOrWhiteSpace(name))apps.Add(new(name,target,null));
                }finally{Marshal.ReleaseComObject(item);}
            }
        }
        finally {if(items!=null)Marshal.ReleaseComObject(items);if(folder!=null)Marshal.ReleaseComObject(folder);if(shell!=null)Marshal.ReleaseComObject(shell);}
        apps=apps.OrderBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
        Directory.CreateDirectory(Preferences.Folder);
        string cache=System.IO.Path.Combine(Preferences.Folder,"start-pins.json"),temporary=System.IO.Path.Combine(Preferences.Folder,"start-"+Guid.NewGuid().ToString("N")+".json");
        string? warning=null;
        try
        {
            var info=new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
            info.ArgumentList.Add("-NoProfile");info.ArgumentList.Add("-NonInteractive");info.ArgumentList.Add("-Command");
            info.ArgumentList.Add("Export-StartLayout -Path '"+temporary.Replace("'","''")+"' -ErrorAction Stop");
            using var p=Process.Start(info)!;
            var error=p.StandardError.ReadToEndAsync();var output=p.StandardOutput.ReadToEndAsync();
            if(!p.WaitForExit(10000)){p.Kill(true);throw new IOException("Start export timed out.");}
            if(p.ExitCode!=0||!File.Exists(temporary))throw new IOException("Windows could not export Start pins.");
            using(var check=JsonDocument.Parse(File.ReadAllText(temporary)))check.RootElement.GetProperty("pinnedList");
            File.Move(temporary,cache,true);
        }
        catch {warning=File.Exists(cache)?"Windows pins could not refresh. Showing the last synced list.":"Windows pins could not be read. All Apps is still available.";}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
        var pins=new List<StartApp>();
        if(File.Exists(cache))
        {
            try{using var doc=JsonDocument.Parse(File.ReadAllText(cache));
                foreach(var pin in doc.RootElement.GetProperty("pinnedList").EnumerateArray())
                {
                    string? target=null,name=null;
                    if(pin.TryGetProperty("packagedAppId",out var id))target="shell:AppsFolder\\"+id.GetString();
                    else if(pin.TryGetProperty("desktopAppId",out id))target="shell:AppsFolder\\"+id.GetString();
                    else if(pin.TryGetProperty("desktopAppLink",out id)){target=Environment.ExpandEnvironmentVariables(id.GetString()!);name=System.IO.Path.GetFileNameWithoutExtension(target);}
                    if(target==null){warning="Some Windows pins use an unsupported format.";continue;}
                    var match=apps.FirstOrDefault(a=>string.Equals(a.Target,target,StringComparison.OrdinalIgnoreCase));
                    pins.Add(match??new StartApp(name??target.Split('\\').Last(),target,null));
                }
            }catch{warning="The saved Windows pin list could not be read.";}
        }
        return new(apps,pins,warning);
    }
    [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IImageFactory
    {[PreserveSig] int GetImage(Size size,uint flags,out IntPtr bitmap);}
    [StructLayout(LayoutKind.Sequential)] struct Size{public int Width,Height;}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)] static extern void SHCreateItemFromParsingName(string name,IntPtr context,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IImageFactory factory);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);
    static ImageSource? Image(string target)
    {
        IImageFactory? factory=null;IntPtr bitmap=IntPtr.Zero;
        try{var iid=typeof(IImageFactory).GUID;SHCreateItemFromParsingName(target,IntPtr.Zero,ref iid,out factory);if(factory.GetImage(new Size{Width=48,Height=48},0x100,out bitmap)<0)return null;
            var image=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());image.Freeze();return image;
        }catch{return null;}finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(factory!=null)Marshal.ReleaseComObject(factory);}
    }
}

