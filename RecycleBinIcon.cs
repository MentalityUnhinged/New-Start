using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Threading;

namespace NewStart;
internal static class RecycleBinIcon
{
    [StructLayout(LayoutKind.Sequential)]struct Query {public uint Size;public long Bytes,Items;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Stock {public uint Size;public IntPtr Icon;public int Index,IconIndex;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Path;}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHQueryRecycleBin(string? root,ref Query data);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHGetStockIconInfo(uint id,uint flags,ref Stock data);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int SHEmptyRecycleBinW(IntPtr owner,string? root,uint flags);
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
    sealed record Snapshot(ImageSource? Icon,bool? HasItems);
    static Snapshot current=new(null,null);
    static readonly object gate=new();
    static DateTime checkedAt;static bool refreshing,requested;
    static ImageSource? emptyIcon,fullIcon;
    internal static ImageSource? Current=>Volatile.Read(ref current).Icon;
    internal static bool? HasItems=>Volatile.Read(ref current).HasItems;
    internal static int Empty(IntPtr owner)
    {
        // Zero flags preserves Windows' confirmation, progress UI and sound.
        // A null root addresses the current user's Recycle Bins on all drives.
        try{return SHEmptyRecycleBinW(owner,null,0);}
        finally{Refresh(true);}
    }
    internal static event Action? Changed;
    internal static void Refresh(bool force=false)
    {
        lock(gate)
        {
            requested|=force;
            if(refreshing||(!requested&&(DateTime.UtcNow-checkedAt).TotalSeconds<1))return;
            refreshing=true;requested=false;checkedAt=DateTime.UtcNow;
        }
        _=Task.Run(()=>{
            try
            {
                var query=new Query{Size=(uint)Marshal.SizeOf<Query>()};
                // A failed query is unknown, not proof that the bin is empty.
                if(SHQueryRecycleBin(null,ref query)!=0)return;
                bool next=query.Items>0;var previous=Volatile.Read(ref current);
                if(previous.HasItems==next&&previous.Icon!=null)return;
                var image=next?fullIcon??=ReadStock(true):emptyIcon??=ReadStock(false);
                if(image==null)return;
                Volatile.Write(ref current,new Snapshot(image,next));Changed?.Invoke();
            }
            finally
            {
                bool again;lock(gate){refreshing=false;again=requested;}
                if(again)Refresh(true);
            }
        });
    }
    static ImageSource? ReadStock(bool full)
    {
        var stock=new Stock{Size=(uint)Marshal.SizeOf<Stock>(),Path=""};
        try
        {
            if(SHGetStockIconInfo(full?32u:31u,0x100,ref stock)<0||stock.Icon==IntPtr.Zero)return null;
            var image=Imaging.CreateBitmapSourceFromHIcon(stock.Icon,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());image.Freeze();return image;
        }
        finally{if(stock.Icon!=IntPtr.Zero)DestroyIcon(stock.Icon);}
    }
}
