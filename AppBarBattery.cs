using System;
using System.Runtime.InteropServices;

namespace NewStart;
internal readonly record struct BatteryReading(bool Present,int? Percentage,bool Charging,bool PluggedIn)
{
    internal static BatteryReading FromStatus(bool success,byte flags,byte percentage,byte ac)
        =>new(success&&flags!=255&&(flags&128)==0,percentage<=100?percentage:null,(flags&8)!=0,ac==1);
}
internal static class AppBarBattery
{
    [StructLayout(LayoutKind.Sequential)]struct Status{public byte AC,Battery,Percent,Reserved;public uint Life,FullLife;}
    [DllImport("kernel32.dll")]static extern bool GetSystemPowerStatus(out Status status);
    [DllImport("user32.dll")]internal static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient,ref Guid setting,uint flags);
    [DllImport("user32.dll")]internal static extern bool UnregisterPowerSettingNotification(IntPtr handle);
    internal static BatteryReading Read(){bool success=GetSystemPowerStatus(out var status);return BatteryReading.FromStatus(success,status.Battery,status.Percent,status.AC);}
    internal static readonly Guid[] Notifications={new("a7ad8041-b45a-4cae-87a3-eecbb468a9e1"),new("5d3e9a59-e9d5-4b00-a6bd-ff34ff516548")};
}
