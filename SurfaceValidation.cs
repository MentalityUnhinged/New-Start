using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
namespace NewStart;
internal static class SurfaceValidation
{
    internal static void Run()
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var checks=new Dictionary<string,bool>();ScrollPolicy.Install();
        var owner=new Window{Title="New Start surface verification",Width=220,Height=80,ShowActivated=false,ShowInTaskbar=false,Content=new System.Windows.Controls.TextBlock{Text="Checking disposable surfaces…",Margin=new Thickness(10)}};
        owner.Loaded+=async(_,_)=>{try{await BackdropStateValidation.Run(owner,checks);await ScrollRoutingValidation.Run(owner,checks);}catch{checks["SurfaceHarnessCompleted"]=false;}finally{owner.Close();File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"surface-verification.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(checks.Values.Any(passed=>!passed)?1:0);}};app.Run(owner);
    }
}
