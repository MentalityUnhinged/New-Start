using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NewStart;
internal static class DockActivationValidation
{
    const string Prefix="New Start Dock activation fixture ";
    internal static void Fixture(string token)
    {
        if(!Guid.TryParseExact(token,"N",out _))return;
        System.Windows.Forms.Application.EnableVisualStyles();using var context=new System.Windows.Forms.ApplicationContext();int remaining=2;
        using var first=Create("A",0);using var second=Create("B",1);first.Show();second.Show();System.Windows.Forms.Application.Run(context);
        System.Windows.Forms.Form Create(string label,int index)
        {
            var area=System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea??new System.Drawing.Rectangle(0,0,1280,720);
            var form=new System.Windows.Forms.Form{Text=Prefix+label+" "+token,Width=360,Height=210,StartPosition=System.Windows.Forms.FormStartPosition.Manual,Location=new System.Drawing.Point(area.Left+40+index*380,area.Top+100),ShowInTaskbar=true};
            form.Controls.Add(new System.Windows.Forms.Label{Text="Disposable New Start activation test · "+label+"\r\n\r\nOnly these two test windows are minimised, restored or closed. No personal files, settings or application window styles are changed.",Dock=System.Windows.Forms.DockStyle.Fill,Padding=new System.Windows.Forms.Padding(18),AutoSize=false});
            form.FormClosed+=(_,_)=>{if(--remaining==0)context.ExitThread();};return form;
        }
    }
    internal static async Task Run()
    {
        var checks=new Dictionary<string,bool>();var observations=new List<object>();IntPtr previous=Native.GetForegroundWindow();Process? fixture=null;string token=Guid.NewGuid().ToString("N");bool completed=false;
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var run=new Button{Content="Run Dock activation checks",Padding=new Thickness(16,10,16,10),Margin=new Thickness(18),HorizontalAlignment=HorizontalAlignment.Center};
        var panel=new StackPanel();panel.Children.Add(new TextBlock{Text="This check uses two disposable test windows. Click Run to provide normal Windows foreground permission.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(18,18,18,0)});panel.Children.Add(run);
        var owner=new Window{Title="New Start Dock activation verification",Width=440,Height=200,WindowStartupLocation=WindowStartupLocation.CenterScreen,Content=panel};run.Click+=(_,_)=>{run.IsEnabled=false;ready.TrySetResult();};owner.Closed+=(_,_)=>{if(!completed)ready.TrySetCanceled();};owner.Show();
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(120));fixture=Launch(token);var windows=await Windows(fixture,token);checks["ActivationTwoIndependentFixtureWindows"]=windows.Length==2;
            if(windows.Length!=2)throw new InvalidOperationException("Fixture windows were not available.");
            var first=windows.Single(window=>window.Title==Prefix+"A "+token);var second=windows.Single(window=>window.Title==Prefix+"B "+token);
            async Task<bool> Foreground(Native.TaskWindow window,string stage)
            {
                bool accepted=Native.SetForegroundWindow(window.Handle);bool focused=await Wait(()=>Native.GetForegroundWindow()==window.Handle&&!Native.IsIconic(window.Handle));observations.Add(new{Stage=stage,ApiAccepted=accepted,ForegroundMatched=focused,FixtureMinimised=Native.IsIconic(window.Handle)});return focused;
            }
            bool firstReady=await Foreground(first,"Initial fixture focus");checks["ActivationForegroundPermissionAvailable"]=firstReady;
            if(firstReady)
            {
                bool accepted=ApplicationActivation.Toggle(new[]{first},Native.GetForegroundWindow());bool minimised=await Wait(()=>Native.IsIconic(first.Handle));checks["ActivationActiveWindowClickMinimises"]=accepted&&minimised;observations.Add(new{Stage="Active → minimise",ApiAccepted=accepted,Minimised=minimised});
                accepted=ApplicationActivation.Toggle(new[]{first},Native.GetForegroundWindow());bool restored=await Wait(()=>!Native.IsIconic(first.Handle)&&Native.GetForegroundWindow()==first.Handle);checks["ActivationMinimisedWindowClickRestoresAndFocuses"]=accepted&&restored;observations.Add(new{Stage="Minimised → restore and focus",ApiAccepted=accepted,RestoredAndForeground=restored});
            }
            else{checks["ActivationActiveWindowClickMinimises"]=false;checks["ActivationMinimisedWindowClickRestoresAndFocuses"]=false;observations.Add(new{Stage="Focus restriction",Status="Windows denied fixture foreground activation; no input or security workaround was used."});}
            bool secondReady=await Foreground(second,"Second fixture focus");checks["ActivationSecondFixtureCanReceiveFocus"]=secondReady;
            if(secondReady)
            {
                bool accepted=ApplicationActivation.Toggle(new[]{first},Native.GetForegroundWindow());bool focused=await Wait(()=>Native.GetForegroundWindow()==first.Handle&&!Native.IsIconic(first.Handle));checks["ActivationUnfocusedWindowClickFocuses"]=accepted&&focused;observations.Add(new{Stage="Unfocused → foreground",ApiAccepted=accepted,ForegroundMatched=focused});
            }
            else checks["ActivationUnfocusedWindowClickFocuses"]=false;
            secondReady=await Foreground(second,"Multiwindow fixture focus");checks["ActivationMultiwindowExactForegroundChosen"]=secondReady&&ReferenceEquals(ApplicationActivation.Choose(new[]{first,second},second.Handle),second);
            if(secondReady)
            {
                bool accepted=ApplicationActivation.Toggle(new[]{first,second},Native.GetForegroundWindow());bool exact=await Wait(()=>Native.IsIconic(second.Handle)&&!Native.IsIconic(first.Handle));checks["ActivationMultiwindowOnlyActiveWindowMinimised"]=accepted&&exact;observations.Add(new{Stage="Multiwindow exact foreground toggle",ApiAccepted=accepted,SecondMinimised=Native.IsIconic(second.Handle),FirstMinimised=Native.IsIconic(first.Handle)});
                checks["ActivationVisibleWindowPreferredOverMinimised"]=ReferenceEquals(ApplicationActivation.Choose(new[]{second,first},IntPtr.Zero),first);
            }
            else{checks["ActivationMultiwindowOnlyActiveWindowMinimised"]=false;checks["ActivationVisibleWindowPreferredOverMinimised"]=false;}
            checks["ActivationDoesNotCreateAdditionalInstances"]=!fixture.HasExited&&(await Windows(fixture,token)).Length==2;
            bool closeAccepted=Native.CloseWindow(first.Handle);bool closed=await Wait(()=>!ActiveApplication.IsWindow(first.Handle));checks["ActivationFixtureCloseObserved"]=closeAccepted&&closed;
            // Never send a stale handle if Windows has already reused it for another window.
            bool rejected=closed&&!ApplicationActivation.Toggle(new[]{first},Native.GetForegroundWindow());checks["ActivationClosedWindowRejected"]=rejected;observations.Add(new{Stage="Closed fixture rejection",Closed=closed,ToggleRejected=rejected});checks["ActivationEmptyListRejected"]=!ApplicationActivation.Toggle(Array.Empty<Native.TaskWindow>(),Native.GetForegroundWindow());
        }
        catch(Exception error){checks["ActivationHarnessCompleted"]=false;observations.Add(new{Stage="Harness failure",ErrorType=error.GetType().Name});}
        finally
        {
            completed=true;if(fixture!=null){try{if(!fixture.HasExited){Native.EnumWindows((window,_)=>{ApplicationIdentity.GetWindowThreadProcessId(window,out uint pid);if(pid==fixture.Id)Native.CloseWindow(window);return true;},IntPtr.Zero);if(!fixture.WaitForExit(1200))fixture.Kill(true);}}catch{}fixture.Dispose();}
            owner.Close();if(ActiveApplication.IsWindow(previous))Native.SetForegroundWindow(previous);checks.TryAdd("ActivationHarnessCompleted",true);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"dock-activation-verification.json"),JsonSerializer.Serialize(new{Checks=checks,Observations=observations},new JsonSerializerOptions{WriteIndented=true}));
        }
        if(checks.Values.Any(passed=>!passed))throw new InvalidOperationException("Dock activation checks failed. See the recorded foreground observations.");
    }
    static Process Launch(string token)
    {
        string executable=Environment.ProcessPath??throw new InvalidOperationException("The validation host executable is unavailable.");var info=new ProcessStartInfo(executable){UseShellExecute=false};
        if(Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase))info.ArgumentList.Add(typeof(DockActivationValidation).Assembly.Location);info.ArgumentList.Add("--dock-activation-fixture");info.ArgumentList.Add(token);return Process.Start(info)??throw new InvalidOperationException("Fixture could not be launched.");
    }
    static async Task<Native.TaskWindow[]> Windows(Process fixture,string token)
    {
        Native.TaskWindow[] result=Array.Empty<Native.TaskWindow>();for(int attempt=0;attempt<60;attempt++)
        {
            if(fixture.HasExited)return result;result=Native.Windows().Where(window=>{ApplicationIdentity.GetWindowThreadProcessId(window.Handle,out uint pid);return pid==fixture.Id&&window.Title.StartsWith(Prefix,StringComparison.Ordinal)&&window.Title.EndsWith(token,StringComparison.Ordinal);}).ToArray();if(result.Length==2)return result;await Task.Delay(50);
        }
        return result;
    }
    static async Task<bool> Wait(Func<bool> predicate)
    {
        for(int attempt=0;attempt<50;attempt++){if(predicate())return true;await Task.Delay(40);}return predicate();
    }
}
