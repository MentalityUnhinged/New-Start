using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace NewStart;
internal sealed record WallpaperColours(Color Dominant,Color Accent,Color[] Colours);
internal sealed class WallpaperPalette:IDisposable
{
    [ComImport,Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string? monitor,[MarshalAs(UnmanagedType.LPWStr)]string path);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string? monitor,[MarshalAs(UnmanagedType.LPWStr)]out string path);
        void GetMonitorDevicePathAt(uint index,[MarshalAs(UnmanagedType.LPWStr)]out string monitor);
        void GetMonitorDevicePathCount(out uint count);
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)]string monitor,out Native.Rect rect);
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool SystemParametersInfo(uint action,uint size,StringBuilder data,uint flags);
    internal static WallpaperPalette? Current {get;private set;}
    readonly Dispatcher ui;readonly DispatcherTimer debounce;readonly Dictionary<string,WallpaperColours> monitors=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,WallpaperColours> cache=new();bool disposed,busy,pending;
    internal event Action? Changed;
    internal string Status {get;private set;}="Reading desktop wallpaper…";
    internal int Analyses {get;private set;}
    internal bool Busy=>busy;
    internal WallpaperPalette(Dispatcher dispatcher)
    {
        ui=dispatcher;Current=this;debounce=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(650)};debounce.Tick+=(_,_)=>{debounce.Stop();Refresh();};
        SystemEvents.UserPreferenceChanged+=PreferencesChanged;SystemEvents.DisplaySettingsChanged+=DisplayChanged;Refresh();
    }
    void PreferencesChanged(object sender,UserPreferenceChangedEventArgs args)=>Schedule();
    void DisplayChanged(object? sender,EventArgs args)=>Schedule();
    void Schedule(){if(!disposed)ui.BeginInvoke(new Action(()=>{if(!disposed){debounce.Stop();debounce.Start();}}));}
    internal WallpaperColours? ForMonitor(string? monitor)=>monitor!=null?monitors.GetValueOrDefault(monitor):monitors.Values.FirstOrDefault();
    internal async void Refresh()
    {
        if(disposed)return;if(busy){pending=true;return;}busy=true;
        try
        {
            var result=await Sta(()=>Read());if(disposed)return;
            foreach(var pair in result)monitors[pair.Key]=pair.Value;
            Status=result.Count>0?"Colours from your desktop wallpaper":"Wallpaper unavailable; retaining the selected colour.";
            Changed?.Invoke(); // The same notification also carries Windows light/dark theme changes.
        }catch{if(!disposed){Status="Wallpaper unavailable; retaining the selected colour.";Changed?.Invoke();}}
        finally{busy=false;if(pending&&!disposed){pending=false;Schedule();}}
    }
    static Task<T> Sta<T>(Func<T> action){var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>{try{result.SetResult(action());}catch(Exception e){result.SetException(e);}}){IsBackground=true,Name="New Start wallpaper"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return result.Task;}
    Dictionary<string,WallpaperColours> Read()
    {
        var result=new Dictionary<string,WallpaperColours>();var displays=DesktopDisplays.All();object? shell=null;
        try
        {
            shell=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"))!);var desktop=(IDesktopWallpaper)shell!;desktop.GetMonitorDevicePathCount(out uint count);
            for(uint index=0;index<Math.Min(count,32);index++)
            {
                desktop.GetMonitorDevicePathAt(index,out string id);desktop.GetMonitorRECT(id,out var rect);desktop.GetWallpaper(id,out string path);
                var display=displays.FirstOrDefault(display=>display.Bounds.Left==rect.Left&&display.Bounds.Top==rect.Top);if(display!=null&&Analyse(path) is {} palette)result[display.Name]=palette;
            }
        }catch{ /* Slideshow or policy may temporarily hide individual files. */ }
        finally{if(shell!=null)Marshal.ReleaseComObject(shell);}
        if(result.Count==0){var path=new StringBuilder(32768);if(SystemParametersInfo(0x73,(uint)path.Capacity,path,0)&&Analyse(path.ToString()) is {} palette)foreach(var display in displays)result[display.Name]=palette;}
        return result;
    }
    internal WallpaperColours? Analyse(string path)
    {
        try
        {
            var file=new FileInfo(path);if(!file.Exists||file.Length>100*1024*1024)return null;
            string key=path+"|"+file.LastWriteTimeUtc.Ticks+"|"+file.Length;
            if(cache.TryGetValue(key,out var found))return found;
            string disk=Path.Combine(Preferences.Folder,"palettes",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)))+".json");
            try{var colours=JsonSerializer.Deserialize<string[]>(File.ReadAllText(disk));if(colours is {Length:>=2 and <=8}&&colours.All(value=>SurfaceAppearance.TryHex(value,out _))){var list=colours.Select(value=>{SurfaceAppearance.TryHex(value,out var c);return c;}).ToArray();found=new(list[0],list[1],list.Distinct().ToArray());cache[key]=found;return found;}}catch{}
            using var stream=File.OpenRead(path);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=80;bitmap.DecodePixelHeight=80;bitmap.StreamSource=stream;bitmap.EndInit();
            var converted=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);int stride=converted.PixelWidth*4;var pixels=new byte[stride*converted.PixelHeight];converted.CopyPixels(pixels,stride,0);
            var samples=new List<Color>();for(int y=0;y<converted.PixelHeight;y+=Math.Max(1,converted.PixelHeight/80))for(int x=0;x<converted.PixelWidth;x++){int offset=y*stride+x*4;if(pixels[offset+3]>128)samples.Add(Color.FromRgb(pixels[offset+2],pixels[offset+1],pixels[offset]));}
            found=Extract(samples);if(found==null)return null;Analyses++;
            if(cache.Count>=32)cache.Clear();cache[key]=found;
            try{Directory.CreateDirectory(Path.GetDirectoryName(disk)!);File.WriteAllText(disk,JsonSerializer.Serialize(new[]{found.Dominant,found.Accent}.Concat(found.Colours).Select(SurfaceAppearance.Hex)));}catch{}
            return found;
        }catch{return null;}
    }
    internal static WallpaperColours? Extract(IReadOnlyList<Color> samples)
    {
        if(samples.Count==0)return null;
        static double Distance(Color a,Color b)=>2*Math.Pow(a.R-b.R,2)+4*Math.Pow(a.G-b.G,2)+3*Math.Pow(a.B-b.B,2);
        var centres=new List<Color>{samples[samples.Count/2]};
        while(centres.Count<6){var far=samples.OrderByDescending(c=>centres.Min(k=>Distance(c,k))).First();if(centres.Min(k=>Distance(far,k))<400)break;centres.Add(far);}
        int[] counts=new int[centres.Count];
        for(int pass=0;pass<10;pass++)
        {
            var sums=new double[centres.Count,3];Array.Clear(counts);
            foreach(var pixel in samples){int closest=Enumerable.Range(0,centres.Count).MinBy(i=>Distance(pixel,centres[i]));counts[closest]++;sums[closest,0]+=pixel.R;sums[closest,1]+=pixel.G;sums[closest,2]+=pixel.B;}
            for(int i=0;i<centres.Count;i++)if(counts[i]>0)centres[i]=Color.FromRgb((byte)Math.Round(sums[i,0]/counts[i]),(byte)Math.Round(sums[i,1]/counts[i]),(byte)Math.Round(sums[i,2]/counts[i]));
        }
        var order=Enumerable.Range(0,centres.Count).Where(i=>counts[i]>=Math.Max(1,samples.Count/200)).OrderByDescending(i=>counts[i]).ToArray();
        if(order.Length==0)return null;var dominant=centres[order[0]];
        int accent=order.MaxBy(i=>Math.Sqrt(Distance(centres[i],dominant))*(.5+(Math.Max(centres[i].R,Math.Max(centres[i].G,centres[i].B))-Math.Min(centres[i].R,Math.Min(centres[i].G,centres[i].B)))/255d)*Math.Sqrt(counts[i]/(double)samples.Count));
        return new(dominant,centres[accent],order.Select(i=>centres[i]).Distinct().ToArray());
    }
    public void Dispose(){disposed=true;debounce.Stop();SystemEvents.UserPreferenceChanged-=PreferencesChanged;SystemEvents.DisplaySettingsChanged-=DisplayChanged;if(Current==this)Current=null;}
}
