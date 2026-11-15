using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace NewStart;
internal record WirelessNetwork(Guid Adapter,string Name,string Profile,uint Quality,bool Connected,bool Connectable,uint Authentication,bool Secured);
internal sealed class AppBarNetwork:IDisposable
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Interface{public Guid Id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Description;public uint State;}
    [StructLayout(LayoutKind.Sequential)]struct Ssid{public uint Length;[MarshalAs(UnmanagedType.ByValArray,SizeConst=32)]public byte[] Bytes;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Network{[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Profile;public Ssid Ssid;public uint BssType,Bssids;public int Connectable;public uint Reason,PhyCount;[MarshalAs(UnmanagedType.ByValArray,SizeConst=8)]public uint[] PhyTypes;public int MorePhy;public uint Quality;public int Security;public uint Auth,Cipher,Flags,Reserved;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Connection{public uint Mode;[MarshalAs(UnmanagedType.LPWStr)]public string Profile;public IntPtr Ssid,Bssids;public uint BssType,Flags;}
    [StructLayout(LayoutKind.Sequential)]struct Notice{public uint Source,Code;public Guid Adapter;public uint Size;public IntPtr Data;}
    delegate void Notification(ref Notice notice,IntPtr context);
    [DllImport("wlanapi.dll")]static extern uint WlanOpenHandle(uint version,IntPtr reserved,out uint negotiated,out IntPtr handle);
    [DllImport("wlanapi.dll")]static extern uint WlanCloseHandle(IntPtr handle,IntPtr reserved);
    [DllImport("wlanapi.dll")]static extern uint WlanEnumInterfaces(IntPtr handle,IntPtr reserved,out IntPtr list);
    [DllImport("wlanapi.dll")]static extern uint WlanGetAvailableNetworkList(IntPtr handle,ref Guid adapter,uint flags,IntPtr reserved,out IntPtr list);
    [DllImport("wlanapi.dll")]static extern uint WlanScan(IntPtr handle,ref Guid adapter,IntPtr ssid,IntPtr extra,IntPtr reserved);
    [DllImport("wlanapi.dll")]static extern uint WlanRegisterNotification(IntPtr handle,uint source,bool ignoreDuplicate,Notification? callback,IntPtr context,IntPtr reserved,out uint previous);
    [DllImport("wlanapi.dll")]static extern uint WlanConnect(IntPtr handle,ref Guid adapter,ref Connection parameters,IntPtr reserved);
    [DllImport("wlanapi.dll")]static extern uint WlanDisconnect(IntPtr handle,ref Guid adapter,IntPtr reserved);
    [DllImport("wlanapi.dll",CharSet=CharSet.Unicode)]static extern uint WlanSetProfile(IntPtr handle,ref Guid adapter,uint flags,string xml,string? security,bool overwrite,IntPtr reserved,out uint reason);
    [DllImport("wlanapi.dll")]static extern void WlanFreeMemory(IntPtr value);
    [DllImport("wlanapi.dll",CharSet=CharSet.Unicode)]static extern uint WlanReasonCodeToString(uint reason,uint size,StringBuilder buffer,IntPtr reserved);
    readonly Dispatcher ui;readonly Notification notification;readonly SemaphoreSlim gate=new(1);IntPtr handle;bool disposed;int queued;
    string? connectionFeedback;
    internal IReadOnlyList<WirelessNetwork> Networks {get;private set;}=Array.Empty<WirelessNetwork>();
    internal string Status {get;private set;}="Checking Wi-Fi…";
    internal long Refreshes {get;private set;}
    internal long Notifications {get;private set;}
    internal bool AccessDenied {get;private set;}
    internal event Action? Changed;
    internal AppBarNetwork(Dispatcher dispatcher){ui=dispatcher;notification=OnNotice;_=Refresh();}
    void OnNotice(ref Notice notice,IntPtr context)
    {
        Notifications++;
        if(notice.Source==8&&notice.Code is 10 or 11&&notice.Data!=IntPtr.Zero&&notice.Size>=564)
        {
            uint reason=(uint)Marshal.ReadInt32(notice.Data,560);string? message=null;
            if(reason!=0){var buffer=new StringBuilder(512);WlanReasonCodeToString(reason,512,buffer,IntPtr.Zero);message=buffer.Length>0?buffer.ToString():"Connection failed ("+reason+").";}
            string? feedback=message;ui.BeginInvoke(new Action(()=>{if(!disposed)connectionFeedback=feedback;}));
        }
        if(disposed||Interlocked.Exchange(ref queued,1)!=0)return;
        ui.BeginInvoke(new Action(()=>{Interlocked.Exchange(ref queued,0);if(!disposed)_=Refresh();}));
    }
    static void Check(uint error){if(error!=0)throw new Win32Exception((int)error);}
    List<Interface> Interfaces()
    {
        Check(WlanEnumInterfaces(handle,IntPtr.Zero,out var list));
        try{int count=Marshal.ReadInt32(list);return Enumerable.Range(0,Math.Clamp(count,0,64)).Select(i=>Marshal.PtrToStructure<Interface>(list+8+i*Marshal.SizeOf<Interface>())).ToList();}finally{WlanFreeMemory(list);}
    }
    internal async Task Refresh(bool scan=false)
    {
        Refreshes++;
        if(disposed)return;
        await gate.WaitAsync();
        try
        {
            if(disposed)return;
            var result=await Task.Run(()=>{
                if(handle==IntPtr.Zero){Check(WlanOpenHandle(2,IntPtr.Zero,out _,out handle));Check(WlanRegisterNotification(handle,8,true,notification,IntPtr.Zero,IntPtr.Zero,out _));}
                var adapters=Interfaces();var networks=new List<WirelessNetwork>();
                foreach(var adapter in adapters)
                {
                    var id=adapter.Id;
                    if(scan)Check(WlanScan(handle,ref id,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero));
                    Check(WlanGetAvailableNetworkList(handle,ref id,2,IntPtr.Zero,out var list));
                    try{int count=Marshal.ReadInt32(list);for(int i=0;i<Math.Clamp(count,0,2048);i++)
                    {var item=Marshal.PtrToStructure<Network>(list+8+i*Marshal.SizeOf<Network>());string name=Encoding.UTF8.GetString(item.Ssid.Bytes,0,(int)Math.Min(32,item.Ssid.Length));if(name.Length>0)networks.Add(new(id,name,item.Profile,item.Quality,(item.Flags&1)!=0,item.Connectable!=0,item.Auth,item.Security!=0));}}
                    finally{WlanFreeMemory(list);}
                }
                return (adapters.Count,networks);
            });
            if(disposed)return;AccessDenied=false;Networks=result.networks.OrderByDescending(n=>n.Connected).ThenByDescending(n=>n.Profile.Length>0).ThenByDescending(n=>n.Quality).DistinctBy(n=>(n.Adapter,n.Name)).ToArray();
            Status=result.Count==0?"No wireless adapter available.":Networks.FirstOrDefault(n=>n.Connected) is {} connected?"Connected to "+connected.Name:scan?"Scan requested; waiting for Windows results…":"Wi-Fi disconnected";
            if(connectionFeedback!=null)Status+=" · "+connectionFeedback;
        }
        catch(Win32Exception e){if(!disposed){AccessDenied=e.NativeErrorCode==5;Status=AccessDenied?"Windows denied Wi-Fi discovery. Location permission may be required.":"Wi-Fi unavailable: "+e.Message;Networks=Array.Empty<WirelessNetwork>();if(e.NativeErrorCode is 6 or 1062 or 1722){if(handle!=IntPtr.Zero)WlanCloseHandle(handle,IntPtr.Zero);handle=IntPtr.Zero;}}}
        catch(Exception e){if(!disposed)Status="Wi-Fi unavailable: "+e.Message;}
        finally{gate.Release();if(!disposed)Changed?.Invoke();}
    }
    internal async Task Connect(WirelessNetwork network,bool disconnect)
    {
        await gate.WaitAsync();
        try
        {
            if(disposed)return;
            if(!disconnect&&string.IsNullOrEmpty(network.Profile)){Status="This network needs a Windows profile or credentials. Use Windows network configuration.";return;}
            uint error=await Task.Run(()=>{var id=network.Adapter;var request=new Connection{Mode=0,Profile=network.Profile,BssType=1};return disconnect?WlanDisconnect(handle,ref id,IntPtr.Zero):WlanConnect(handle,ref id,ref request,IntPtr.Zero);});
            Check(error);connectionFeedback=null;Status=disconnect?"Disconnect requested; waiting for Windows confirmation…":"Connecting; waiting for Windows confirmation…";
        }catch(Exception e){Status=e.Message;}finally{gate.Release();if(!disposed)Changed?.Invoke();}
    }
    internal async Task Provision(WirelessNetwork network,string password)
    {
        if(network.Secured&&network.Authentication!=7){Status="This security type requires Windows configuration (for example enterprise or WPA3).";Changed?.Invoke();return;}
        if(network.Secured&&(password.Length<8||password.Length>63)){Status="Enter a WPA2 password of 8–63 characters.";Changed?.Invoke();return;}
        string Escape(string value)=>System.Security.SecurityElement.Escape(value)??"";
        string credentials=network.Secured?$"<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{Escape(password)}</keyMaterial></sharedKey>":"";
        string xml=$"<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>{Escape(network.Name)}</name><SSIDConfig><SSID><name>{Escape(network.Name)}</name></SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>manual</connectionMode><MSM><security><authEncryption><authentication>{(network.Secured?"WPA2PSK":"open")}</authentication><encryption>{(network.Secured?"AES":"none")}</encryption><useOneX>false</useOneX></authEncryption>{credentials}</security></MSM></WLANProfile>";
        await gate.WaitAsync();bool saved=false;
        try{if(disposed)return;uint result=await Task.Run(()=>{var id=network.Adapter;return WlanSetProfile(handle,ref id,2,xml,null,false,IntPtr.Zero,out _);});Check(result);saved=true;}
        catch(Exception e){Status="Windows could not save the network profile: "+e.Message;}
        finally{gate.Release();if(!disposed)Changed?.Invoke();}
        if(saved&&!disposed)await Connect(network with{Profile=network.Name},false);
    }
    public void Dispose()
    {
        disposed=true;
        _=Task.Run(async()=>{await gate.WaitAsync();try{if(handle!=IntPtr.Zero){WlanRegisterNotification(handle,0,true,null,IntPtr.Zero,IntPtr.Zero,out _);WlanCloseHandle(handle,IntPtr.Zero);handle=IntPtr.Zero;}}finally{gate.Release();}});
    }
}
