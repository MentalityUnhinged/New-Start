using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Windows.System.Power;
using WinBattery=Windows.Devices.Power.Battery;

namespace NewStart;
internal sealed record PowerPlan(Guid Id,string Name);
internal sealed record PowerSnapshot(BatteryReading Battery,string State,TimeSpan? Remaining,long? DesignCapacity,long? FullChargeCapacity,IReadOnlyList<BatteryDeviceTelemetry> Devices,string EffectiveMode,string EnergySaver,PowerPlan? ActivePlan,IReadOnlyList<PowerPlan> Plans,string? Note,DateTime Updated)
{
    internal static double? Health(long? design,long? full)=>design is >0&&full is >0?Math.Round(full.Value*100d/design.Value):null;
    internal double? BatteryHealth=>Health(DesignCapacity,FullChargeCapacity);
    internal static PowerSnapshot Empty=>new(default,"Reading power information…",null,null,null,Array.Empty<BatteryDeviceTelemetry>(),"Unavailable","Unavailable",null,Array.Empty<PowerPlan>(),null,DateTime.MinValue);
}
internal sealed class AppBarPower:IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate void EffectiveModeCallback(uint mode,IntPtr context);
    [DllImport("powrprof.dll")]static extern int PowerRegisterForEffectivePowerModeNotifications(uint version,EffectiveModeCallback callback,IntPtr context,out IntPtr registration);
    [DllImport("powrprof.dll")]static extern int PowerUnregisterFromEffectivePowerModeNotifications(IntPtr registration);
    [DllImport("powrprof.dll")]static extern uint PowerGetActiveScheme(IntPtr root,out IntPtr scheme);
    [DllImport("powrprof.dll")]static extern uint PowerSetActiveScheme(IntPtr root,ref Guid scheme);
    [DllImport("powrprof.dll")]static extern uint PowerEnumerate(IntPtr root,IntPtr scheme,IntPtr subgroup,uint accessor,uint index,out Guid buffer,ref uint size);
    [DllImport("powrprof.dll")]static extern uint PowerReadFriendlyName(IntPtr root,ref Guid scheme,IntPtr subgroup,IntPtr setting,IntPtr buffer,ref uint size);
    [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll")]static extern ulong GetTickCount64();
    readonly Dispatcher ui;readonly EffectiveModeCallback modeCallback;readonly List<Action> unsubscribe=new();readonly SemaphoreSlim reads=new(1,1);readonly DispatcherTimer coalesce;
    WinBattery? aggregate;IntPtr modeRegistration;bool disposed,refreshAgain;int mode=-1;DateTime lastRead,lastDevices;IReadOnlyList<BatteryDeviceTelemetry> devices=Array.Empty<BatteryDeviceTelemetry>();
    internal PowerSnapshot Current {get;private set;}=PowerSnapshot.Empty;
    internal event Action? Changed;
    internal AppBarPower(Dispatcher dispatcher)
    {
        ui=dispatcher;coalesce=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};coalesce.Tick+=(_,_)=>{coalesce.Stop();_=RefreshAsync();};
        modeCallback=(value,_)=>{Volatile.Write(ref mode,(int)value);QueueRefresh();};
        try{PowerRegisterForEffectivePowerModeNotifications(2,modeCallback,IntPtr.Zero,out modeRegistration);}catch(EntryPointNotFoundException){}catch(DllNotFoundException){}
        TrySubscribe(()=>PowerManager.EnergySaverStatusChanged+=PowerChanged,()=>PowerManager.EnergySaverStatusChanged-=PowerChanged);
        TrySubscribe(()=>PowerManager.PowerSupplyStatusChanged+=PowerChanged,()=>PowerManager.PowerSupplyStatusChanged-=PowerChanged);
        TrySubscribe(()=>PowerManager.BatteryStatusChanged+=PowerChanged,()=>PowerManager.BatteryStatusChanged-=PowerChanged);
        TrySubscribe(()=>PowerManager.RemainingChargePercentChanged+=PowerChanged,()=>PowerManager.RemainingChargePercentChanged-=PowerChanged);
        TrySubscribe(()=>PowerManager.RemainingDischargeTimeChanged+=PowerChanged,()=>PowerManager.RemainingDischargeTimeChanged-=PowerChanged);
        try{aggregate=WinBattery.AggregateBattery;aggregate.ReportUpdated+=ReportUpdated;unsubscribe.Add(()=>aggregate.ReportUpdated-=ReportUpdated);}catch{}
        _=RefreshAsync();
    }
    void TrySubscribe(Action add,Action remove){try{add();unsubscribe.Add(remove);}catch{}}
    void PowerChanged(object? sender,object args)=>QueueRefresh();
    void ReportUpdated(WinBattery sender,object args)=>QueueRefresh();
    void QueueRefresh()
    {
        if(disposed||ui.HasShutdownStarted)return;ui.BeginInvoke(new Action(()=>{if(!disposed&&!coalesce.IsEnabled)coalesce.Start();}),DispatcherPriority.Background);
    }
    internal async Task RefreshAsync(bool force=false)
    {
        if(disposed)return;
        if(!force&&DateTime.UtcNow-lastRead<TimeSpan.FromMilliseconds(750)){QueueRefresh();return;}
        if(!await reads.WaitAsync(0)){refreshAgain=true;return;}
        try
        {
            // One background read at a time. Unsupported/slow firmware never runs on the UI thread.
            var next=await Task.Run(()=>Read(force));if(disposed)return;lastRead=DateTime.UtcNow;Current=next;Changed?.Invoke();
        }
        catch(Exception error){if(!disposed){Current=Current with{Note="Power information unavailable: "+error.Message};Changed?.Invoke();}}
        finally{reads.Release();if(refreshAgain&&!disposed){refreshAgain=false;QueueRefresh();}}
    }
    PowerSnapshot Read(bool force)
    {
        var battery=AppBarBattery.Read();long? design=null,full=null;string? note=null;
        string state=!battery.Present?"No system battery reported":battery.Charging?"Charging":battery.PluggedIn?"Plugged in · not charging":"Discharging";
        try
        {
            var report=aggregate?.GetReport();if(report!=null)
            {
                if(report.DesignCapacityInMilliwattHours is >0)design=report.DesignCapacityInMilliwattHours.Value;
                if(report.FullChargeCapacityInMilliwattHours is >0)full=report.FullChargeCapacityInMilliwattHours.Value;
            }
        }catch{note="Some battery details are not exposed by this device.";}
        if(force||DateTime.UtcNow-lastDevices>TimeSpan.FromMinutes(5))
        {
            try{devices=PowerBatteryDevices.Read();lastDevices=DateTime.UtcNow;}catch{note="Detailed battery-controller telemetry is unavailable.";}
        }
        // Never sum unknown or relative capacities with absolute mWh telemetry.
        if(devices.Count>0&&devices.All(device=>!device.Relative&&device.DesignCapacity.HasValue&&device.FullChargeCapacity.HasValue))
        {design??=devices.Sum(device=>device.DesignCapacity!.Value);full??=devices.Sum(device=>device.FullChargeCapacity!.Value);}
        TimeSpan? remaining=!battery.Charging&&!battery.PluggedIn&&battery.Present?AppBarBattery.RemainingTime():null;
        if(remaining==null&&battery.Present&&!battery.PluggedIn)
        {try{var value=PowerManager.RemainingDischargeTime;if(value>=TimeSpan.Zero&&value<TimeSpan.FromDays(365))remaining=value;}catch{}}
        string saver;try{saver=SaverName(PowerManager.EnergySaverStatus);}catch{saver="Unavailable";}
        var plans=ReadPlans();return new(battery,state,remaining,design,full,devices,ModeName(Volatile.Read(ref mode)),saver,ReadActivePlan(),plans,note,DateTime.UtcNow);
    }
    internal static string ModeName(int value)=>value switch{0=>"Energy saver",1=>"Better battery",2=>"Balanced",3=>"High performance",4=>"Best performance",5=>"Game mode",6=>"Mixed reality",_=>"Not reported"};
    internal static string SaverName(EnergySaverStatus status)=>status switch{EnergySaverStatus.On=>"On",EnergySaverStatus.Off=>"Off · automatic",EnergySaverStatus.Disabled=>"Off · disabled or on mains",_=>"Unavailable"};
    static PowerPlan? ReadActivePlan()
    {
        if(PowerGetActiveScheme(IntPtr.Zero,out var pointer)!=0||pointer==IntPtr.Zero)return null;
        try{var id=Marshal.PtrToStructure<Guid>(pointer);return new(id,PlanName(id));}finally{LocalFree(pointer);}
    }
    static string PlanName(Guid id)
    {
        uint size=0;if(PowerReadFriendlyName(IntPtr.Zero,ref id,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,ref size)!=0||size is <2 or >8192)return "Power plan";
        var buffer=Marshal.AllocHGlobal((int)size);try{return PowerReadFriendlyName(IntPtr.Zero,ref id,IntPtr.Zero,IntPtr.Zero,buffer,ref size)==0?Marshal.PtrToStringUni(buffer)??"Power plan":"Power plan";}finally{Marshal.FreeHGlobal(buffer);}
    }
    static IReadOnlyList<PowerPlan> ReadPlans()
    {
        var result=new List<PowerPlan>();for(uint index=0;index<32;index++){uint size=16;if(PowerEnumerate(IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,16,index,out var id,ref size)!=0)break;result.Add(new(id,PlanName(id)));}return result;
    }
    internal async Task SelectPlanAsync(PowerPlan plan)
    {
        if(disposed||!Current.Plans.Any(item=>item.Id==plan.Id))return;
        try
        {
            var result=await Task.Run(()=>{var id=plan.Id;uint status=PowerSetActiveScheme(IntPtr.Zero,ref id);return (Status:status,Active:ReadActivePlan()?.Id);});
            if(result.Status!=0)throw new InvalidOperationException("Windows denied changing the power plan ("+result.Status+").");
            if(result.Active!=plan.Id)throw new InvalidOperationException("Windows did not activate the selected plan.");
            await RefreshAsync(true);
        }
        catch(Exception error){if(!disposed){Current=Current with{Note=error.Message};Changed?.Invoke();}}
    }
    internal static string Capacity(long? value,bool relative=false)=>value is >0?(relative?$"{value.Value:N0} relative units":$"{value.Value:N0} mWh"):"Not reported";
    internal static string Duration(TimeSpan? value)=>value is {} time?$"{(int)time.TotalHours}h {time.Minutes}m":"Not reported";
    internal FrameworkElement BuildContent()
    {
        var panel=new StackPanel();
        void Row(string label,string value)
        {
            var row=new Grid{Margin=new Thickness(0,2,0,2)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            row.Children.Add(new TextBlock{Text=label,Opacity=.75,Margin=new Thickness(0,0,16,0),VerticalAlignment=VerticalAlignment.Center});var text=new TextBlock{Text=value,TextWrapping=TextWrapping.Wrap,MaxWidth=210,TextAlignment=TextAlignment.Right};Grid.SetColumn(text,1);row.Children.Add(text);panel.Children.Add(row);
        }
        void Button(string label,Action action){var button=new Button{Content=label,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,5,0,0)};button.Click+=(_,_)=>action();panel.Children.Add(button);}
        void Render()
        {
            if(disposed)return;panel.Children.Clear();var snapshot=Current;
            panel.Children.Add(new TextBlock{Text="Power & battery",FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,7)});
            Row("Battery",snapshot.Battery.Present?(snapshot.Battery.Percentage is int percent?percent+"%":"Level not reported"):"Not present");Row("Status",snapshot.State);
            if(snapshot.Battery.Present)
            {
                Row("Time remaining",Duration(snapshot.Remaining));Row("Battery health",snapshot.BatteryHealth is double health?$"{health:0}% of design capacity":"Not reported");Row("Original design capacity",Capacity(snapshot.DesignCapacity));Row("Full-charge capacity",Capacity(snapshot.FullChargeCapacity));
                if(snapshot.Devices.Count==1)Row("Charge cycles",snapshot.Devices[0].CycleCount is uint cycles?cycles.ToString("N0"):"Not reported");
                else if(snapshot.Devices.Count==0)Row("Charge cycles","Not reported");
                else foreach(var device in snapshot.Devices)
                {
                    panel.Children.Add(new TextBlock{Text=device.Name,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,2)});Row("Design / full",Capacity(device.DesignCapacity,device.Relative)+" / "+Capacity(device.FullChargeCapacity,device.Relative));Row("Health",device.Health is double value?$"{value:0}%":"Not reported");Row("Charge cycles",device.CycleCount is uint valueCycles?valueCycles.ToString("N0"):"Not reported");
                }
            }
            Row("System uptime",Duration(TimeSpan.FromMilliseconds(GetTickCount64())));Row("Effective power mode",snapshot.EffectiveMode);Row("Energy saver",snapshot.EnergySaver);Row("Power plan",snapshot.ActivePlan?.Name??"Not reported");
            if(snapshot.Plans.Count>1)
            {
                panel.Children.Add(new TextBlock{Text="Choose power plan",Margin=new Thickness(0,7,0,3)});
                var plans=new ComboBox{ItemsSource=snapshot.Plans,DisplayMemberPath=nameof(PowerPlan.Name),SelectedItem=snapshot.Plans.FirstOrDefault(plan=>plan.Id==snapshot.ActivePlan?.Id)};
                plans.SelectionChanged+=(_,_)=>{if(plans.SelectedItem is PowerPlan selected&&selected.Id!=Current.ActivePlan?.Id)_=SelectPlanAsync(selected);};panel.Children.Add(plans);
            }
            if(snapshot.Note!=null)panel.Children.Add(new TextBlock{Text=snapshot.Note,TextWrapping=TextWrapping.Wrap,FontSize=11,Opacity=.8,Margin=new Thickness(0,7,0,2)});
            Button("Energy saver and power mode settings",()=>Open("ms-settings:powersleep"));Button("Battery settings",()=>Open("ms-settings:batterysaver"));Button("Windows power management",()=>Open("control.exe","powercfg.cpl"));Button("Refresh power information",()=>_=RefreshAsync(true));
        }
        panel.Loaded+=(_,_)=>{Changed+=Render;Render();_=RefreshAsync();};panel.Unloaded+=(_,_)=>Changed-=Render;Render();return panel;
    }
    static void Open(string target,string? arguments=null){try{Process.Start(new ProcessStartInfo(target,arguments??""){UseShellExecute=true});}catch(Exception error){MessageBox.Show(error.Message,"New Start power settings");}}
    public void Dispose()
    {
        if(disposed)return;disposed=true;coalesce.Stop();foreach(var remove in unsubscribe){try{remove();}catch{}}unsubscribe.Clear();if(modeRegistration!=IntPtr.Zero){try{PowerUnregisterFromEffectivePowerModeNotifications(modeRegistration);}catch{}}modeRegistration=IntPtr.Zero;aggregate=null;
    }
}
