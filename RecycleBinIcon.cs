using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace NewStart;
internal static class RecycleBinIcon
{
    [StructLayout(LayoutKind.Sequential)]struct Query {public uint Size;public long Bytes,Items;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Stock {public uint Size;public IntPtr Icon;public int Index,IconIndex;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Path;}
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHQueryRecycleBin(string? root,ref Query data);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHGetStockIconInfo(uint id,uint flags,ref Stock data);
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
    static DateTime checkedAt;static Task? pending;static bool? full;
    internal static ImageSource? Current {get;private set;}
    internal static event Action? Changed;
    internal static void Refresh()
    {
        if(pending is {IsCompleted:false}||(DateTime.UtcNow-checkedAt).TotalSeconds<5)return;
        checkedAt=DateTime.UtcNow;
        pending=Task.Run(()=>{
            var query=new Query{Size=(uint)Marshal.SizeOf<Query>()};
            bool next=SHQueryRecycleBin(null,ref query)==0&&query.Items>0;if(full==next&&Current!=null)return;
            var stock=new Stock{Size=(uint)Marshal.SizeOf<Stock>(),Path=""};
            try{if(SHGetStockIconInfo(next?32u:31u,0x100,ref stock)<0)return;
                var source=Imaging.CreateBitmapSourceFromHIcon(stock.Icon,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());source.Freeze();Current=source;full=next;Changed?.Invoke();
            }finally{if(stock.Icon!=IntPtr.Zero)DestroyIcon(stock.Icon);}
        });
    }
}
