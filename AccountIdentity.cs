using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace NewStart;
internal record AccountProfile(string DisplayName,ImageSource? Avatar);
internal static class AccountIdentity
{
    static AccountProfile current=new("Account",null);
    internal static AccountProfile Current=>Volatile.Read(ref current);
    internal static event Action? Updated;
    internal static void Initialize()=>_=Task.Run(()=>{
        var profile=Read();Volatile.Write(ref current,profile);Updated?.Invoke();
    });
    [StructLayout(LayoutKind.Sequential)]struct UserInfo23 {public IntPtr Name,FullName,Comment;public uint Flags;public IntPtr Sid;}
    [DllImport("netapi32.dll",CharSet=CharSet.Unicode)]static extern int NetUserGetInfo(string? server,string name,int level,out IntPtr data);
    [DllImport("netapi32.dll")]static extern int NetApiBufferFree(IntPtr buffer);
    [DllImport("secur32.dll",CharSet=CharSet.Unicode)]static extern bool GetUserNameEx(int format,StringBuilder name,ref uint size);
    static AccountProfile Read()
    {
        string name="Account";ImageSource? avatar=null;
        try
        {
            using var identity=WindowsIdentity.GetCurrent();
            string account=identity.Name.Contains('\\')?identity.Name[(identity.Name.LastIndexOf('\\')+1)..]:identity.Name;
            if(!string.IsNullOrWhiteSpace(account))name=account;
            var display=new StringBuilder(1024);uint size=(uint)display.Capacity;
            if(GetUserNameEx(3,display,ref size)&&display.Length>0)name=display.ToString();
            else
            {
                IntPtr data=IntPtr.Zero;
                try{if(NetUserGetInfo(null,account,23,out data)==0){var info=Marshal.PtrToStructure<UserInfo23>(data);string? full=Marshal.PtrToStringUni(info.FullName);if(!string.IsNullOrWhiteSpace(full))name=full;}}
                finally{if(data!=IntPtr.Zero)NetApiBufferFree(data);}
            }
            // Read-only compatibility adapter for this unpackaged desktop application.
            // Windows.System.User.GetPictureAsync requires a packaged account-info capability.
            // Read only the current SID's Windows-maintained picture paths, with a generic fallback.
            if(identity.User is { } sid)
            {
                using var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\"+sid.Value);
                foreach(var resolution in new[]{"Image96","Image64","Image192","Image48"})
                {
                    if(key?.GetValue(resolution) is not string path||!Path.IsPathFullyQualified(path)||path.StartsWith(@"\\")||!File.Exists(path))continue;
                    try{using var file=File.OpenRead(path);var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=96;image.StreamSource=file;image.EndInit();image.Freeze();avatar=image;break;}catch{}
                }
            }
        }catch{}
        return new(name,avatar);
    }
}
