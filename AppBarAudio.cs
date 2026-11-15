using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace NewStart;
internal record AudioOutput(string Name,string Id);
internal sealed class AppBarAudio:IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void Notification();
    [DllImport("NewStart.Backdrop.dll")]static extern int AudioCreate(Notification callback,out IntPtr state);
    [DllImport("NewStart.Backdrop.dll",CharSet=CharSet.Unicode)]static extern int AudioRead(IntPtr state,out float level,out bool muted,StringBuilder name,int capacity,StringBuilder outputs,int outputCapacity);
    [DllImport("NewStart.Backdrop.dll")]static extern int AudioWrite(IntPtr state,float level,bool muted);
    [DllImport("NewStart.Backdrop.dll")]static extern void AudioDestroy(IntPtr state);
    [DllImport("NewStart.Backdrop.dll",CharSet=CharSet.Unicode)]static extern int AudioSelectOutput(IntPtr state,string id);
    readonly BlockingCollection<Action> work=new();readonly Thread thread;readonly Dispatcher ui;readonly Notification notification;IntPtr handle;bool disposed;int pending;
    internal double Level {get;private set;}
    internal bool Muted {get;private set;}
    internal string Output {get;private set;}="Audio unavailable";
    internal AudioOutput[] Outputs {get;private set;}=Array.Empty<AudioOutput>();
    internal string? Error {get;private set;}
    internal event Action? Changed;
    internal long Refreshes {get;private set;}
    internal AppBarAudio(Dispatcher dispatcher)
    {
        ui=dispatcher;notification=Refresh;
        thread=new Thread(()=>{try{foreach(var action in work.GetConsumingEnumerable())action();}finally{if(handle!=IntPtr.Zero)AudioDestroy(handle);}}){IsBackground=true,Name="New Start Core Audio"};thread.SetApartmentState(ApartmentState.MTA);thread.Start();Refresh();
    }
    void Enqueue(Action action){if(disposed)return;try{work.Add(action);}catch(InvalidOperationException){}}
    internal void Refresh()
    {
        if(disposed||Interlocked.Exchange(ref pending,1)!=0)return;
        Enqueue(()=>{
            Refreshes++;
            string? error=null;float level=0;bool muted=false;var name=new StringBuilder(512);var outputs=new StringBuilder(4096);
            try{if(handle==IntPtr.Zero)Marshal.ThrowExceptionForHR(AudioCreate(notification,out handle));Marshal.ThrowExceptionForHR(AudioRead(handle,out level,out muted,name,name.Capacity,outputs,outputs.Capacity));}catch(Exception e){error=e.Message;}
            Interlocked.Exchange(ref pending,0);
            ui.BeginInvoke(new Action(()=>{if(disposed)return;Error=error;Level=Math.Round(level*100);Muted=muted;Output=name.Length>0?name.ToString():"Audio unavailable";Outputs=outputs.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>line.Split('\t',2)).Where(parts=>parts.Length==2).Select(parts=>new AudioOutput(parts[0],parts[1])).ToArray();Changed?.Invoke();}));
        });
    }
    internal void Set(double volume,bool muted)=>Enqueue(()=>{try{if(handle==IntPtr.Zero)throw new InvalidOperationException("Audio endpoint unavailable.");Marshal.ThrowExceptionForHR(AudioWrite(handle,(float)Math.Clamp(volume/100,0,1),muted));Refresh();}catch(Exception e){ui.BeginInvoke(new Action(()=>{Error=e.Message;Changed?.Invoke();}));}});
    internal void SelectOutput(AudioOutput output)=>Enqueue(()=>{try{if(handle==IntPtr.Zero)throw new InvalidOperationException("Audio unavailable.");Marshal.ThrowExceptionForHR(AudioSelectOutput(handle,output.Id));Refresh();}catch(Exception e){ui.BeginInvoke(new Action(()=>{Error="Windows output switching is unavailable: "+e.Message;Changed?.Invoke();}));}});
    public void Dispose(){if(disposed)return;disposed=true;work.CompleteAdding();}
}
