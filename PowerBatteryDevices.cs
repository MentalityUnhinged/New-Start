using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NewStart;
internal sealed record BatteryDeviceTelemetry(string Name,long? DesignCapacity,long? FullChargeCapacity,uint? CycleCount,bool Relative)
{
    internal double? Health=>PowerSnapshot.Health(DesignCapacity,FullChargeCapacity);
}
// Read-only battery miniport queries. No device identifiers or serial numbers are exposed or persisted.
internal static class PowerBatteryDevices
{
    [StructLayout(LayoutKind.Sequential)]struct DeviceInterface{public uint Size;public Guid Class;public uint Flags;public UIntPtr Reserved;}
    [StructLayout(LayoutKind.Sequential)]struct Information{public uint Capabilities;public byte Technology,Reserved1,Reserved2,Reserved3;public uint Chemistry,Designed,Full,Alert1,Alert2,Critical,Cycles;}
    [StructLayout(LayoutKind.Sequential)]struct Query{public uint Tag,Level;public int AtRate;}
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr SetupDiGetClassDevs(ref Guid category,IntPtr enumerator,IntPtr parent,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)]static extern bool SetupDiEnumDeviceInterfaces(IntPtr devices,IntPtr device,ref Guid category,uint index,ref DeviceInterface information);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr devices,ref DeviceInterface information,IntPtr detail,uint size,out uint required,IntPtr device);
    [DllImport("setupapi.dll")]static extern bool SetupDiDestroyDeviceInfoList(IntPtr devices);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool DeviceIoControl(SafeFileHandle device,uint code,IntPtr input,uint inputSize,IntPtr output,uint outputSize,out uint returned,IntPtr overlapped);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool CancelIoEx(SafeFileHandle device,IntPtr overlapped);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetOverlappedResult(SafeFileHandle device,IntPtr overlapped,out uint transferred,bool wait);
    [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateEvent(IntPtr security,bool manual,bool initial,string? name);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)]struct Overlapped{public UIntPtr Internal,InternalHigh;public uint Offset,OffsetHigh;public IntPtr Event;}
    static readonly Guid Category=new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");
    internal static IReadOnlyList<BatteryDeviceTelemetry> Read()
    {
        var result=new List<BatteryDeviceTelemetry>();var category=Category;var devices=SetupDiGetClassDevs(ref category,IntPtr.Zero,IntPtr.Zero,0x12);
        if(devices==new IntPtr(-1))return result;
        try
        {
            for(uint index=0;index<16;index++)
            {
                var data=new DeviceInterface{Size=(uint)Marshal.SizeOf<DeviceInterface>()};if(!SetupDiEnumDeviceInterfaces(devices,IntPtr.Zero,ref category,index,ref data))break;
                SetupDiGetDeviceInterfaceDetail(devices,ref data,IntPtr.Zero,0,out uint size,IntPtr.Zero);if(size is <8 or >32768)continue;
                var detail=Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);if(!SetupDiGetDeviceInterfaceDetail(devices,ref data,detail,size,out _,IntPtr.Zero))continue;
                    string? path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));if(string.IsNullOrWhiteSpace(path))continue;
                    using var device=CreateFile(path,0x80000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);if(device.IsInvalid)continue;
                    var tag=QueryValue<uint,uint>(device,0x294040,0);if(tag is null or 0)continue;
                    var info=QueryValue<Query,Information>(device,0x294044,new Query{Tag=tag.Value,Level=0});if(info==null||(info.Value.Capabilities&0x80000000)==0)continue;
                    bool relative=(info.Value.Capabilities&0x40000000)!=0;
                    result.Add(new("Battery "+(result.Count+1),ValidCapacity(info.Value.Designed),ValidCapacity(info.Value.Full),info.Value.Cycles==0?null:info.Value.Cycles,relative));
                }finally{Marshal.FreeHGlobal(detail);}
            }
        }finally{SetupDiDestroyDeviceInfoList(devices);}
        return result;
    }
    internal static long? ValidCapacity(uint capacity)=>capacity is 0 or uint.MaxValue?null:capacity;
    static TOutput? QueryValue<TInput,TOutput>(SafeFileHandle device,uint code,TInput value) where TInput:struct where TOutput:struct
    {
        var input=Marshal.AllocHGlobal(Marshal.SizeOf<TInput>());var output=Marshal.AllocHGlobal(Marshal.SizeOf<TOutput>());var overlapped=Marshal.AllocHGlobal(Marshal.SizeOf<Overlapped>());var signal=CreateEvent(IntPtr.Zero,true,false,null);
        bool started=false,completed=false;
        try
        {
            if(signal==IntPtr.Zero)return null;Marshal.StructureToPtr(value,input,false);Marshal.StructureToPtr(new Overlapped{Event=signal},overlapped,false);
            bool ok=DeviceIoControl(device,code,input,(uint)Marshal.SizeOf<TInput>(),output,(uint)Marshal.SizeOf<TOutput>(),out uint bytes,overlapped);
            if(!ok&&Marshal.GetLastWin32Error()!=997)return null;started=true;
            if(!ok&&WaitForSingleObject(signal,500)!=0){CancelIoEx(device,overlapped);return null;}
            completed=GetOverlappedResult(device,overlapped,out bytes,false);if(!completed||bytes<Marshal.SizeOf<TOutput>())return null;
            return Marshal.PtrToStructure<TOutput>(output);
        }
        finally
        {
            if(started&&!completed){CancelIoEx(device,overlapped);GetOverlappedResult(device,overlapped,out _,true);} // Keep buffers alive until cancelled I/O has completed.
            if(signal!=IntPtr.Zero)CloseHandle(signal);Marshal.FreeHGlobal(overlapped);Marshal.FreeHGlobal(output);Marshal.FreeHGlobal(input);
        }
    }
}
