using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NewStart;
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct AppBar { public uint Size; public IntPtr Hwnd; public uint Callback,Edge; public Rect Rect; public IntPtr Param; }
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message, ref AppBar data);
    internal static bool AutoHide { get { var d=new AppBar{Size=(uint)Marshal.SizeOf<AppBar>()}; return (SHAppBarMessage(4,ref d).ToUInt64()&1)!=0; } }
    internal delegate bool EnumCallback(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumCallback cb,IntPtr data);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint cmd);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr hwnd,StringBuilder text,int size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int size);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attr,out int value,int size);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr hwnd,int command);
    [DllImport("user32.dll",SetLastError=true)]static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    internal static bool CloseWindow(IntPtr hwnd)=>PostMessage(hwnd,0x0112,new IntPtr(0xF060),IntPtr.Zero);
    internal record TaskWindow(IntPtr Handle,string Title,string Path);
    internal static List<TaskWindow> Windows()
    {
        var list=new List<TaskWindow>();
        EnumWindows((h,_)=>{
            if(!IsWindowVisible(h)||GetWindow(h,4)!=IntPtr.Zero) return true;
            long extended=GetWindowLongPtr(h,-20).ToInt64();if((extended&0x08000080L)!=0)return true;
            var className=new StringBuilder(256);GetClassName(h,className,256);
            if(className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")return true;
            if(DwmGetWindowAttribute(h,14,out int cloaked,4)==0&&cloaked!=0) return true;
            var title=new StringBuilder(512); GetWindowText(h,title,512); if(title.Length==0) return true;
            GetWindowThreadProcessId(h,out uint pid); if(pid==Environment.ProcessId) return true;
            var identity=ApplicationIdentity.ProcessInfo(pid);string path=identity.Path;
            if(className.ToString()=="ApplicationFrameWindow"&&ActiveApplication.Read(h) is {} frame)path=ApplicationIdentity.Enrich(frame).Path;
            if(path.Length>0)list.Add(new(h,title.ToString(),path));
            return true;
        },IntPtr.Zero); return list;
    }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public Keyboard Keyboard; [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] struct Keyboard { public ushort Key,Scan; public uint Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct Mouse { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] inputs,int size);
    internal static bool TaskView()
        => WindowsShortcut(0x09);
    internal static bool ShowDesktop()
        => WindowsShortcut(0x44);
    static bool WindowsShortcut(ushort shortcut)
    {
        Input Key(ushort key,bool up=false)=>new(){Type=1,Data=new InputUnion{Keyboard=new Keyboard{Key=key,Flags=up?2u:0u}}};
        var inputs=new[]{Key(0x5b),Key(shortcut),Key(shortcut,true),Key(0x5b,true)};
        return SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<Input>())==inputs.Length;
    }
}

