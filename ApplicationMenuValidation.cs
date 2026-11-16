using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace NewStart;
internal static class ApplicationMenuValidation
{
    static string Result(string token)=>Path.Combine(Preferences.Folder,"application-menu-fixture-"+token+".txt");
    internal static void Fixture(string token)
    {
        if(!Guid.TryParseExact(token,"N",out _))return;
        Directory.CreateDirectory(Preferences.Folder);
        System.Windows.Forms.Application.EnableVisualStyles();
        using var form=new System.Windows.Forms.Form{Text="New Start application-menu fixture "+token,Width=620,Height=280};
        var menu=new System.Windows.Forms.MenuStrip();form.MainMenuStrip=menu;form.Controls.Add(menu);
        var file=new System.Windows.Forms.ToolStripMenuItem("&File");menu.Items.Add(file);
        file.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("&Save",null,(_,_)=>File.WriteAllText(Result(token),"Save")));
        file.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("Unavailable command"){Enabled=false});
        var saveAs=new System.Windows.Forms.ToolStripMenuItem("Save as");saveAs.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("Plain text",null,(_,_)=>File.WriteAllText(Result(token),"Plain text")));file.DropDownItems.Add(saveAs);
        var edit=new System.Windows.Forms.ToolStripMenuItem("&Edit");edit.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("&Copy",null,(_,_)=>File.WriteAllText(Result(token),"Copy")));menu.Items.Add(edit);
        var view=new System.Windows.Forms.ToolStripMenuItem("&View");view.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("Details"){CheckOnClick=true});menu.Items.Add(view);
        var help=new System.Windows.Forms.ToolStripMenuItem("&Help");help.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("About",null,(_,_)=>File.WriteAllText(Result(token),"About")));menu.Items.Add(help);
        System.Windows.Forms.Application.Run(form);
    }
    internal static async Task Run()
    {
        var checks=new Dictionary<string,bool>();foreach(string check in new[]{"RealMenuCategoriesDiscovered","NoSystemMenuCategory","MenuSnapshotAvailable","UnavailableMenuActionOmitted","InspectionDoesNotExecuteCommands","MenuCommandActuallyExecuted","StaleMenuIdentityRejected","SubmenuPathDiscovered","SubmenuCommandActuallyExecuted"})checks[check]=false;
        var observations=new List<object>();var previous=Native.GetForegroundWindow();var token=Guid.NewGuid().ToString("N");Process? fixture=null;
        var button=new System.Windows.Controls.Button{Content="Run application-menu checks",Margin=new Thickness(20)};var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        button.Click+=(_,_)=>{button.IsEnabled=false;ready.TrySetResult();};var owner=new Window{Title="New Start application-menu verification",Width=420,Height=160,Content=button,WindowStartupLocation=WindowStartupLocation.CenterScreen};owner.Show();
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(120));
            var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};info.ArgumentList.Add("--application-menu-fixture");info.ArgumentList.Add(token);fixture=Process.Start(info)!;
            ActiveWindow? target=null;
            for(int attempt=0;attempt<50&&target==null;attempt++){await Task.Delay(100);Native.EnumWindows((window,_)=>{ApplicationIdentity.GetWindowThreadProcessId(window,out uint pid);if(pid==fixture.Id&&ActiveApplication.Read(window) is {} item&&item.Title.StartsWith("New Start application-menu fixture",StringComparison.Ordinal)){target=item;return false;}return true;},IntPtr.Zero);}
            if(target==null)throw new InvalidOperationException("Application-menu fixture unavailable.");
            var request=new CapabilityRequest(target.Handle.ToInt64(),target.ProcessId,target.Started,Environment.ProcessId,"discover",null);
            var discovery=await ApplicationCapabilities.RunWorker(request,CancellationToken.None,6000);var categories=discovery.Actions.Where(action=>action.IsMenuCategory).ToArray();
            checks["RealMenuCategoriesDiscovered"]=new[]{"File","Edit","View","Help"}.All(label=>categories.Any(category=>category.Label==label));
            checks["NoSystemMenuCategory"]=categories.All(category=>category.Label!="System");observations.Add(new{Discovery=discovery.Status,Categories=categories.Select(category=>category.Label)});
            var file=categories.FirstOrDefault(category=>category.Label=="File");if(file==null)throw new InvalidOperationException("Fixture File category unavailable.");
            owner.Activate();await Task.Delay(100);
            var expanded=await ApplicationCapabilities.RunWorker(request with{Operation="openMenu",Action=file},CancellationToken.None,6000);observations.Add(new{Menu=expanded.Status,Actions=expanded.Actions.Select(action=>new{action.Label,action.Method,action.MenuPath})});
            checks["MenuSnapshotAvailable"]=expanded.Success&&expanded.Actions.Any(action=>action.Label=="Save");checks["UnavailableMenuActionOmitted"]=expanded.Actions.All(action=>action.Label!="Unavailable command");
            checks["InspectionDoesNotExecuteCommands"]=!File.Exists(Result(token));
            var save=expanded.Actions.FirstOrDefault(action=>action.Label=="Save");if(save!=null)
            {
                owner.Activate();await Task.Delay(100);var execution=await ApplicationCapabilities.RunWorker(request with{Operation="executeMenu",Action=save},CancellationToken.None,6000);
                for(int attempt=0;execution.Success&&attempt<20&&(!File.Exists(Result(token))||File.ReadAllText(Result(token))!="Save");attempt++)await Task.Delay(25);
                checks["MenuCommandActuallyExecuted"]=execution.Success&&File.Exists(Result(token))&&File.ReadAllText(Result(token))=="Save";observations.Add(new{Save=execution.Status,execution.Success,ResultExists=File.Exists(Result(token))});
                var invalid=await ApplicationCapabilities.RunWorker(request with{Operation="executeMenu",Started=target.Started+1,Action=save},CancellationToken.None);checks["StaleMenuIdentityRejected"]=!invalid.Success&&invalid.Actions.Count==0;
            }
            var submenu=expanded.Actions.FirstOrDefault(action=>action.Label=="Save as");
            if(submenu!=null)
            {
                owner.Activate();await Task.Delay(100);var nested=await ApplicationCapabilities.RunWorker(request with{Operation="executeMenu",Action=submenu},CancellationToken.None,6000);var leaf=nested.Actions.FirstOrDefault(action=>action.Label=="Plain text");
                checks["SubmenuPathDiscovered"]=nested.Success&&leaf?.MenuPath?.SequenceEqual(new[]{"Save as","Plain text"})==true;observations.Add(new{Submenu=nested.Status,nested.Success});
                if(leaf!=null){owner.Activate();await Task.Delay(100);var executed=await ApplicationCapabilities.RunWorker(request with{Operation="executeMenu",Action=leaf},CancellationToken.None,6000);for(int attempt=0;executed.Success&&attempt<20&&(!File.Exists(Result(token))||File.ReadAllText(Result(token))!="Plain text");attempt++)await Task.Delay(25);checks["SubmenuCommandActuallyExecuted"]=executed.Success&&File.Exists(Result(token))&&File.ReadAllText(Result(token))=="Plain text";observations.Add(new{SubmenuCommand=executed.Status,executed.Success,ResultExists=File.Exists(Result(token))});}
            }
        }
        catch(Exception error){checks["UnhandledFailure"]=false;observations.Add(new{Error=error.ToString()});}
        finally
        {
            if(fixture!=null){try{if(!fixture.HasExited)fixture.Kill();}catch{}fixture.Dispose();}owner.Close();if(ActiveApplication.IsWindow(previous))Native.SetForegroundWindow(previous);
            try{File.Delete(Result(token));}catch{}Directory.CreateDirectory(Preferences.Folder);File.WriteAllText(Path.Combine(Preferences.Folder,"application-menu-verification.json"),JsonSerializer.Serialize(new{Checks=checks,Observations=observations},new JsonSerializerOptions{WriteIndented=true}));
        }
        if(checks.Values.Contains(false))throw new InvalidOperationException("Application-menu checks failed.");
    }
}
