using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NewStart;
internal static class CustomIconValidation
{
    // The caller supplies an isolated Preferences.StorageOverride. No live icon configuration is touched.
    internal static void Run(Dictionary<string,bool> checks)
    {
        string? original=Preferences.StorageOverride;string root=Path.Combine(Preferences.Folder,"icon-fixtures");Directory.CreateDirectory(root);string store=Path.Combine(root,"preferences");Preferences.StorageOverride=store;
        const string target=@"C:\NewStartFixture\Application.exe";string red=Path.Combine(root,"red.png"),blue=Path.Combine(root,"blue.png"),green=Path.Combine(root,"green.ico");
        void Check(string name,Func<bool> test){try{checks[name]=test();}catch{checks[name]=false;}}
        try
        {
            WritePng(red,Colors.Red);WritePng(blue,Colors.Blue);WriteIco(green,Colors.Lime);
            Check("IconRasterAppliesAndDecodes",()=>{CustomIcons.Set(target,"application",red);return Pixel(CustomIcons.Cached(target,"application"),Colors.Red);});
            Check("IconApplicationIdentityIsStable",()=>Pixel(CustomIcons.Cached(@"c:\newstartfixture\APPLICATION.EXE","application"),Colors.Red));
            Check("IconApplicationFallbackInBothLocations",()=>Pixel(CustomIcons.Cached(target,"dock"),Colors.Red)&&Pixel(CustomIcons.Cached(target,"appbar"),Colors.Red));
            Check("IconDockOverrideDoesNotChangeAppBar",()=>{CustomIcons.Set(target,"dock",blue);return Pixel(CustomIcons.Cached(target,"dock"),Colors.Blue)&&Pixel(CustomIcons.Cached(target,"appbar"),Colors.Red);});
            Check("IconIcoFormatAndIndependentAppBarScope",()=>{CustomIcons.Set(target,"appbar",green);return Pixel(CustomIcons.Cached(target,"appbar"),Colors.Lime)&&Pixel(CustomIcons.Cached(target,"dock"),Colors.Blue);});
            Check("IconAssetCopiedInsidePreferences",()=>
            {
                var item=CustomIcons.Entries.Single(item=>item.Identity==CustomIcons.Identity(target)&&item.Scope=="application");string copied=Path.Combine(store,"CustomIcons",item.File);return Path.GetFileName(item.File)==item.File&&File.Exists(copied)&&!Path.GetFullPath(copied).Equals(Path.GetFullPath(red),StringComparison.OrdinalIgnoreCase)&&File.ReadAllBytes(copied).SequenceEqual(File.ReadAllBytes(red));
            });
            Check("IconConfigurationPersistsAndReloads",()=>{Reload(store,root);return CustomIcons.Entries.Count==3&&Pixel(CustomIcons.Cached(target,"application"),Colors.Red)&&Pixel(CustomIcons.Cached(target,"dock"),Colors.Blue)&&Pixel(CustomIcons.Cached(target,"appbar"),Colors.Lime);});
            Check("IconDecodeIsFrozenAndBounded",()=>CustomIcons.Cached(target,"dock") is BitmapSource {IsFrozen:true,PixelWidth:<=128,PixelHeight:<=128});
            string disposable=Path.Combine(root,"removable-source.png");File.Copy(red,disposable,true);
            Check("IconSourceCanBeRemovedAfterImport",()=>{CustomIcons.Set("component:start","dock",disposable);File.Delete(disposable);Reload(store,root);return Pixel(CustomIcons.Cached("component:start","dock"),Colors.Red);});
            string wide=Path.Combine(root,"wide.png"),tall=Path.Combine(root,"tall.png"),small=Path.Combine(root,"small-wide.png");WritePng(wide,Colors.Red,320,80);WritePng(tall,Colors.Blue,80,320);WritePng(small,Colors.Lime,64,16);
            Check("IconWideRasterPreservesAspectRatio",()=>{CustomIcons.Set("component:start","dock",wide);return CustomIcons.Cached("component:start","dock") is BitmapSource {PixelWidth:128,PixelHeight:32}&&Pixel(CustomIcons.Cached("component:start","dock"),Colors.Red);});
            Check("IconTallRasterPreservesAspectRatio",()=>{CustomIcons.Set("component:start","dock",tall);return CustomIcons.Cached("component:start","dock") is BitmapSource {PixelWidth:32,PixelHeight:128}&&Pixel(CustomIcons.Cached("component:start","dock"),Colors.Blue);});
            Check("IconSmallRasterDoesNotUpscale",()=>{CustomIcons.Set("component:start","dock",small);return CustomIcons.Cached("component:start","dock") is BitmapSource {PixelWidth:64,PixelHeight:16};});
            Check("IconReloadPreservesRasterAspectRatio",()=>{CustomIcons.Set("component:start","dock",wide);Reload(store,root);return CustomIcons.Cached("component:start","dock") is BitmapSource {PixelWidth:128,PixelHeight:32,IsFrozen:true};});
            string corrupt=Path.Combine(root,"corrupt.png");File.WriteAllText(corrupt,"This is deliberately not a bitmap.");string manifest=Path.Combine(store,"CustomIcons","overrides.json");
            Check("IconCorruptImportPreservesWorkingOverride",()=>{string before=File.ReadAllText(manifest);bool rejected=false;try{CustomIcons.Set(target,"application",corrupt);}catch{rejected=true;}return rejected&&before==File.ReadAllText(manifest)&&Pixel(CustomIcons.Cached(target,"application"),Colors.Red);});
            string unsupported=Path.Combine(root,"unsupported.svg");File.WriteAllText(unsupported,"<svg/>");Check("IconUnsupportedFormatRejected",()=>Rejected(()=>CustomIcons.Set(target,"application",unsupported))&&Pixel(CustomIcons.Cached(target,"application"),Colors.Red));
            string oversized=Path.Combine(root,"oversized.png");using(var file=File.Create(oversized))file.SetLength(10*1024*1024+1);Check("IconOversizedImportRejected",()=>Rejected(()=>CustomIcons.Set(target,"application",oversized))&&Pixel(CustomIcons.Cached(target,"application"),Colors.Red));
            Check("IconInvalidScopeRejected",()=>Rejected(()=>CustomIcons.Set(target,"unknown",red)));
            Check("IconRestoreUsesApplicationFallback",()=>{CustomIcons.Remove(target,"dock");return Pixel(CustomIcons.Cached(target,"dock"),Colors.Red)&&Pixel(CustomIcons.Cached(target,"appbar"),Colors.Lime);});
            Check("IconRestoreOriginalClearsOverrides",()=>{CustomIcons.Remove(target,"appbar");CustomIcons.Remove(target,"application");return CustomIcons.Cached(target,"dock")==null&&CustomIcons.Cached(target,"appbar")==null;});
            Check("IconMalformedStoreEntriesExcluded",()=>
            {
                string malformed=Path.Combine(root,"malformed");WriteStore(malformed,"""[null,{"Identity":"component:start","Scope":"dock","File":null},{"Identity":"component:start","Scope":"unknown","File":"safe.png"},{"Identity":"","Scope":"dock","File":"safe.png"},{"Identity":"component:start","Scope":"dock","File":"../outside.png"}]""");Preferences.StorageOverride=malformed;return CustomIcons.Entries.Count==0&&CustomIcons.Cached("component:start","dock")==null;
            });
            Check("IconMissingAssetGraceful",()=>
            {
                string missing=Path.Combine(root,"missing");WriteStore(missing,JsonSerializer.Serialize(new[]{new IconOverride("component:start","dock","missing.png")}));Preferences.StorageOverride=missing;return CustomIcons.Cached("component:start","dock")==null;
            });
            Check("IconCorruptConfigurationGraceful",()=>{string broken=Path.Combine(root,"broken");WriteStore(broken,"{ invalid json");Preferences.StorageOverride=broken;return CustomIcons.Entries.Count==0&&CustomIcons.Cached("component:start","dock")==null;});
        }
        finally{Preferences.StorageOverride=original;_ =CustomIcons.Entries;}
    }
    static bool Rejected(Action action){try{action();return false;}catch{return true;}}
    static void WriteStore(string store,string json){Directory.CreateDirectory(Path.Combine(store,"CustomIcons"));File.WriteAllText(Path.Combine(store,"CustomIcons","overrides.json"),json);}
    static void Reload(string store,string root){Preferences.StorageOverride=Path.Combine(root,"reload-probe");_ =CustomIcons.Entries;Preferences.StorageOverride=store;_ =CustomIcons.Entries;}
    static bool Pixel(ImageSource? image,Color expected)
    {
        if(image is not BitmapSource bitmap)return false;var converted=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);var pixel=new byte[4];converted.CopyPixels(new System.Windows.Int32Rect(0,0,1,1),pixel,4,0);return pixel[0]==expected.B&&pixel[1]==expected.G&&pixel[2]==expected.R&&pixel[3]==255;
    }
    static byte[] Png(Color colour,int width=32,int height=32)
    {
        byte[] pixels=new byte[width*height*4];for(int index=0;index<pixels.Length;index+=4){pixels[index]=colour.B;pixels[index+1]=colour.G;pixels[index+2]=colour.R;pixels[index+3]=255;}
        var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
    static void WritePng(string file,Color colour,int width=32,int height=32)=>File.WriteAllBytes(file,Png(colour,width,height));
    static void WriteIco(string file,Color colour)
    {
        byte[] png=Png(colour);using var stream=File.Create(file);using var writer=new BinaryWriter(stream);writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)1);writer.Write((byte)32);writer.Write((byte)32);writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(png.Length);writer.Write(22);writer.Write(png);
    }
}
