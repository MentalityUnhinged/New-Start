using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace NewStart;
internal record PairedBluetooth(string Id,string Name,bool? Connected);
internal sealed class AppBarBluetooth:IDisposable
{
    readonly Dispatcher ui;readonly List<Radio> radios=new();readonly List<DeviceWatcher> watchers=new();readonly Dictionary<string,PairedBluetooth> devices=new();bool disposed;
    internal IReadOnlyList<PairedBluetooth> Devices=>devices.Values.OrderByDescending(d=>d.Connected==true).ThenBy(d=>d.Name).ToArray();
    internal string Status {get;private set;}="Checking Bluetooth…";
    internal bool Enabled=>radios.Any(r=>r.State==RadioState.On);
    internal bool CanToggle=>radios.Count>0;
    internal event Action? Changed;
    internal long Notifications {get;private set;}
    internal AppBarBluetooth(Dispatcher dispatcher){ui=dispatcher;_=Initialize();}
    async Task Initialize()
    {
        try
        {
            var available=await Radio.GetRadiosAsync();if(disposed)return;
            foreach(var radio in available.Where(r=>r.Kind==RadioKind.Bluetooth)){radios.Add(radio);radio.StateChanged+=RadioChanged;}
            foreach(string selector in new[]{BluetoothDevice.GetDeviceSelectorFromPairingState(true),BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)})
            {
                var watcher=DeviceInformation.CreateWatcher(selector,new[]{"System.Devices.Aep.IsConnected"});
                watcher.Added+=Added;watcher.Updated+=Updated;watcher.Removed+=Removed;watchers.Add(watcher);watcher.Start();
            }
            UpdateStatus();Changed?.Invoke();
        }catch(Exception e){Status="Bluetooth unavailable: "+e.Message;Changed?.Invoke();}
    }
    void Dispatch(Action action){Notifications++;if(!disposed)ui.BeginInvoke(new Action(()=>{if(!disposed){action();Changed?.Invoke();}}));}
    static bool? Connected(IReadOnlyDictionary<string,object> properties)=>properties.TryGetValue("System.Devices.Aep.IsConnected",out var value)&&value is bool connected?connected:null;
    void Added(DeviceWatcher sender,DeviceInformation item)=>Dispatch(()=>devices[item.Id]=new(item.Id,item.Name,Connected(item.Properties)));
    void Updated(DeviceWatcher sender,DeviceInformationUpdate item)=>Dispatch(()=>{if(devices.TryGetValue(item.Id,out var old))devices[item.Id]=old with{Connected=Connected(item.Properties)??old.Connected};});
    void Removed(DeviceWatcher sender,DeviceInformationUpdate item)=>Dispatch(()=>devices.Remove(item.Id));
    void RadioChanged(Radio sender,object args)=>Dispatch(UpdateStatus);
    void UpdateStatus(){Status=radios.Count==0?"No accessible Bluetooth radio.":Enabled?"Bluetooth on":"Bluetooth off";}
    internal async Task Toggle()
    {
        try
        {
            var access=await Radio.RequestAccessAsync();if(disposed)return;
            if(access!=RadioAccessStatus.Allowed){Status="Windows denied radio control. Use Bluetooth settings.";Changed?.Invoke();return;}
            var state=Enabled?RadioState.Off:RadioState.On;
            foreach(var radio in radios)if(await radio.SetStateAsync(state)!=RadioAccessStatus.Allowed){Status="Windows could not change the Bluetooth radio.";Changed?.Invoke();return;}
            UpdateStatus();Changed?.Invoke();
        }catch(Exception e){Status=e.Message;Changed?.Invoke();}
    }
    public void Dispose()
    {
        disposed=true;foreach(var radio in radios)radio.StateChanged-=RadioChanged;
        foreach(var watcher in watchers){watcher.Added-=Added;watcher.Updated-=Updated;watcher.Removed-=Removed;if(watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)watcher.Stop();}watchers.Clear();radios.Clear();
    }
}
