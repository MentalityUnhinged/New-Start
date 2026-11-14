using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NewStart;
internal static class CustomBackdrop
{
    [DllImport("NewStart.Backdrop.dll",CallingConvention=CallingConvention.StdCall)]static extern int CreateBackdrop(IntPtr hwnd,float radius,[MarshalAs(UnmanagedType.Bool)]bool visible,out IntPtr state);
    [DllImport("NewStart.Backdrop.dll",CallingConvention=CallingConvention.StdCall)]static extern int UpdateBackdrop(IntPtr state,float radius,[MarshalAs(UnmanagedType.Bool)]bool visible);
    [DllImport("NewStart.Backdrop.dll",CallingConvention=CallingConvention.StdCall)]static extern bool IsBackdropVisible(IntPtr state);
    [DllImport("NewStart.Backdrop.dll",CallingConvention=CallingConvention.StdCall)]static extern void DestroyBackdrop(IntPtr state);
    sealed class State{internal IntPtr Handle;internal float Radius;}
    static readonly ConditionalWeakTable<Window,State> states=new();
    internal static int LastError {get;private set;}
    internal static bool Visible(Window window)=>states.TryGetValue(window,out var state)&&state.Handle!=IntPtr.Zero&&IsBackdropVisible(state.Handle);
    internal static void Hide(Window window){if(states.TryGetValue(window,out var state)&&state.Handle!=IntPtr.Zero)UpdateBackdrop(state.Handle,state.Radius,false);}
    internal static bool Apply(Window window,double radius)
    {
        var hwnd=new WindowInteropHelper(window).Handle;if(hwnd==IntPtr.Zero)return false;
        var state=states.GetValue(window,w=>{
            var s=new State();
            void Update(bool visible){if(s.Handle!=IntPtr.Zero)UpdateBackdrop(s.Handle,s.Radius,visible);}
            w.IsVisibleChanged+=(_,e)=>Update((bool)e.NewValue);w.LocationChanged+=(_,_)=>Update(w.IsVisible);w.SizeChanged+=(_,_)=>Update(w.IsVisible);
            w.Activated+=(_,_)=>Update(w.IsVisible);
            w.Closed+=(_,_)=>{if(s.Handle!=IntPtr.Zero){DestroyBackdrop(s.Handle);s.Handle=IntPtr.Zero;}};return s;
        });
        state.Radius=(float)radius;
        try{
            LastError=state.Handle==IntPtr.Zero?CreateBackdrop(hwnd,(float)radius,window.IsVisible,out state.Handle):UpdateBackdrop(state.Handle,(float)radius,window.IsVisible);
            return LastError>=0;
        }catch(DllNotFoundException){LastError=unchecked((int)0x8007007E);return false;}catch(EntryPointNotFoundException){LastError=unchecked((int)0x8007007F);return false;}
    }
}
