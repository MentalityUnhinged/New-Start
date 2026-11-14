using System;
using System.Runtime.InteropServices;
using System.IO;
namespace NewStart;
internal record StartShortcut(string Name,string Glyph,Func<string> Target);
internal static class StartShortcuts
{
    internal static readonly StartShortcut[] Items={
        new("Documents","\uE8A5",()=>Folder("FDD39AD0-238F-46AF-ADB4-6C85480369C7")),
        new("Downloads","\uE896",()=>Folder("374DE290-123F-4565-9164-39C4925E467B")),
        new("Music","\uEC4F",()=>Folder("4BD8D571-6D19-48D3-BE97-422220080E43")),
        new("Pictures","\uE8B9",()=>Folder("33E28130-4E1E-4676-835A-98395C3BC3BB")),
        new("Videos","\uE714",()=>Folder("18989B1D-99B5-455B-841C-AB7C74E4DDFC")),
        new("Network","\uE774",()=>"shell:::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}"),
        new("Personal folder","\uEC25",()=>Folder("5E6C858F-0E22-4760-9AFE-EA3317B67173")),
        new("File Explorer","\uEC50",()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe")),
        new("Windows Settings","\uE713",()=>"ms-settings:")
    };
    [DllImport("shell32.dll",PreserveSig=false)]static extern void SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
    static string Folder(string id)
    {
        var guid=new Guid(id);IntPtr path=IntPtr.Zero;
        try{SHGetKnownFolderPath(ref guid,0,IntPtr.Zero,out path);return Marshal.PtrToStringUni(path)??throw new IOException("Windows did not return the folder location.");}
        finally{if(path!=IntPtr.Zero)Marshal.FreeCoTaskMem(path);}
    }
}
