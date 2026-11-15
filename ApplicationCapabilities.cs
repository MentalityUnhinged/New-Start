using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Threading;

namespace NewStart;
internal enum CapabilityKind { Back,Forward,Refresh,Search,Save,Undo,Redo,Copy,Paste,Find,Minimise,Maximise,Restore,Close,Play,Pause,Previous,Next,Additional }
internal sealed record ApplicationCapability(CapabilityKind Kind,string Label,string Method,string Source,int[]? RuntimeId=null,string Name="",int ControlType=0);
internal sealed record CapabilityRequest(long Window,uint ProcessId,long Started,int Caller,string Operation,ApplicationCapability? Action);
internal sealed record CapabilityReply(List<ApplicationCapability> Actions,string Status,bool Success=false,int Inspected=0,string[]? Patterns=null);
internal interface IApplicationCapabilityAdapter
{
    string Name {get;}
    bool Supports(ActiveWindow application);
    ApplicationCapability? Recognise(string accessibleName,int controlType,string method,int[] runtimeId);
    Task<IReadOnlyList<ApplicationCapability>> DiscoverAsync(ActiveWindow application,CancellationToken cancellation)=>Task.FromResult<IReadOnlyList<ApplicationCapability>>(Array.Empty<ApplicationCapability>());
    Task<bool> ExecuteAsync(ActiveWindow application,ApplicationCapability action,CancellationToken cancellation)=>Task.FromResult(false);
}
// These adapters add accessible commands, never fabricated browser/ChatGPT APIs or guessed shortcuts.
internal sealed class ChromiumCapabilityAdapter:IApplicationCapabilityAdapter
{
    public string Name=>"Chromium accessibility";
    public bool Supports(ActiveWindow app)=>System.IO.Path.GetFileName(app.Path).ToLowerInvariant() is "chrome.exe" or "msedge.exe";
    public ApplicationCapability? Recognise(string name,int controlType,string method,int[] id)
    {
        if(method!="Invoke")return null;
        if(name=="New tab")return new(CapabilityKind.Additional,"New tab",method,Name,id,name,controlType);
        return CapabilityRegistry.Kind(name) is CapabilityKind.Back or CapabilityKind.Forward or CapabilityKind.Refresh?new(CapabilityRegistry.Kind(name)!.Value,name,method,Name,id,name,controlType):null;
    }
}
internal sealed class ChatGptCapabilityAdapter:IApplicationCapabilityAdapter
{
    public string Name=>"ChatGPT accessibility";
    public bool Supports(ActiveWindow app)=>System.IO.Path.GetFileName(app.Path).Equals("ChatGPT.exe",StringComparison.OrdinalIgnoreCase)||app.AppId.StartsWith("OpenAI.ChatGPT",StringComparison.OrdinalIgnoreCase);
    public ApplicationCapability? Recognise(string name,int type,string method,int[] id)=>name is "New chat" or "Search chats" or "Open sidebar" or "Close sidebar"?new(CapabilityKind.Additional,name,method,Name,id,name,type):null;
}
internal static class CapabilityRegistry
{
    internal static readonly IReadOnlyList<IApplicationCapabilityAdapter> Adapters=new IApplicationCapabilityAdapter[]{new ChromiumCapabilityAdapter(),new ChatGptCapabilityAdapter()};
    internal static string Normalise(string name)
    {
        string text=name.Replace("&","").Split('\t')[0].Trim().TrimEnd('.','…');
        int shortcut=text.IndexOf(" (",StringComparison.Ordinal);if(shortcut>=0&&text.EndsWith(')')&&(text[(shortcut+2)..].StartsWith("Ctrl+",StringComparison.OrdinalIgnoreCase)||text[(shortcut+2)..].StartsWith("Alt+",StringComparison.OrdinalIgnoreCase)))text=text[..shortcut];return text;
    }
    internal static CapabilityKind? AccessibleKind(string name,string id,int type)
    {
        if(Kind(name) is {} kind)return kind;
        if(type==ControlType.Edit.Id&&name.StartsWith("Search ",StringComparison.OrdinalIgnoreCase))return CapabilityKind.Search;
        if(type==ControlType.Button.Id&&name.StartsWith("Refresh \"",StringComparison.Ordinal))return CapabilityKind.Refresh;
        return id switch{"backButton"=>CapabilityKind.Back,"forwardButton"=>CapabilityKind.Forward,"refreshButton"=>CapabilityKind.Refresh,_=>null};
    }
    internal static CapabilityKind? Kind(string name)=>Normalise(name).ToLowerInvariant() switch{
        "back" or "go back"=>CapabilityKind.Back,"forward" or "go forward"=>CapabilityKind.Forward,"refresh" or "reload" or "reload this page"=>CapabilityKind.Refresh,
        "search"=>CapabilityKind.Search,"save"=>CapabilityKind.Save,"undo"=>CapabilityKind.Undo,"redo"=>CapabilityKind.Redo,"copy"=>CapabilityKind.Copy,"paste"=>CapabilityKind.Paste,"find" or "find in page"=>CapabilityKind.Find,
        "play"=>CapabilityKind.Play,"pause"=>CapabilityKind.Pause,"previous" or "previous track"=>CapabilityKind.Previous,"next track"=>CapabilityKind.Next,_=>null};
}
internal sealed class ApplicationCapabilities:IDisposable
{
    readonly ActiveApplication applications;readonly AppBarMedia media;readonly DispatcherTimer debounce;CancellationTokenSource? pending,execution;bool disposed,executing;int generation;
    readonly Dictionary<(IntPtr,uint,long), (DateTime At,CapabilityReply Reply)> cache=new();DateTime lastDiscovery;
    internal IReadOnlyList<ApplicationCapability> Actions {get;private set;}=Array.Empty<ApplicationCapability>();
    internal ActiveWindow? Context {get;private set;}
    internal string Status {get;private set;}="Select an application to see available actions.";
    internal event Action? Changed;
    internal ApplicationCapabilities(ActiveApplication active,AppBarMedia transport,Dispatcher ui)
    {
        applications=active;media=transport;debounce=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(450)};debounce.Tick+=(_,_)=>{debounce.Stop();_=Discover(true);};
        applications.Changed+=ContextChanged;applications.ContextInvalidated+=Invalidate;media.Changed+=MediaChanged;ContextChanged();
    }
    void MediaChanged(){if(!disposed&&!executing&&Context!=null){Compose(cache.GetValueOrDefault(Key(Context)).Reply);Changed?.Invoke();}}
    static (IntPtr,uint,long) Key(ActiveWindow app)=>(app.Handle,app.ProcessId,app.Started);
    void ContextChanged()
    {
        if(disposed)return;var next=applications.Current;
        if(next!=null&&Context!=null&&Key(next)==Key(Context)){bool changed=Context!=next;Context=next;if(changed){Compose(cache.GetValueOrDefault(Key(next)).Reply);Changed?.Invoke();Invalidate();}return;}
        Context=next;pending?.Cancel();execution?.Cancel();++generation;debounce.Stop();Actions=Array.Empty<ApplicationCapability>();Status=next==null?"No application selected.":"Discovering available actions…";Changed?.Invoke();_=Discover(false);
    }
    void Invalidate(){if(disposed||Context==null||executing)return;cache.Remove(Key(Context));if(!debounce.IsEnabled){Compose(null);Status="Updating available actions…";Changed?.Invoke();debounce.Interval=TimeSpan.FromMilliseconds(Math.Max(450,2000-(DateTime.UtcNow-lastDiscovery).TotalMilliseconds));debounce.Start();}}
    internal void Refresh(){if(Context!=null)cache.Remove(Key(Context));_=Discover(true);}
    async Task Discover(bool force)
    {
        var app=Context;if(disposed||app==null||executing)return;
        if(app.Started==0){Actions=Array.Empty<ApplicationCapability>();Status="Windows restricts this application's identity; actions are unavailable.";Changed?.Invoke();return;}
        CapabilityReply? preview=null;
        if(!force&&cache.TryGetValue(Key(app),out var hit)&&DateTime.UtcNow-hit.At<TimeSpan.FromSeconds(12))preview=hit.Reply;
        lastDiscovery=DateTime.UtcNow;
        pending?.Cancel();pending?.Dispose();pending=new();var cancel=pending.Token;int version=++generation;
        Compose(preview);if(preview!=null)Status="Checking available actions…";Changed?.Invoke();
        var result=await RunWorker(new(app.Handle.ToInt64(),app.ProcessId,app.Started,Environment.ProcessId,"discover",null),cancel);
        foreach(var adapter in CapabilityRegistry.Adapters)
        {
            try
            {
                if(!adapter.Supports(app))continue;
                var extra=await Task.Run(()=>adapter.DiscoverAsync(app,cancel),cancel).WaitAsync(TimeSpan.FromMilliseconds(800),cancel);
                result.Actions.InsertRange(0,extra.Take(12).Select(action=>action with{Source=adapter.Name,Method="Adapter:"+adapter.Name}));
            }catch{ /* Missing, denied or slow optional integrations never disable generic discovery. */ }
        }
        if(disposed||cancel.IsCancellationRequested||version!=generation||Context==null||Key(Context)!=Key(app))return;
        if(cache.Count>=32)cache.Clear();cache[Key(app)]=(DateTime.UtcNow,result);Compose(result);Changed?.Invoke();
    }
    void Compose(CapabilityReply? reply)
    {
        var app=Context;if(app==null)return;var actions=new List<ApplicationCapability>(reply?.Actions??new());
        if(!app.Fullscreen&&app.Started!=0)
        {
            if((app.Style&0x20000)!=0)actions.Add(new(CapabilityKind.Minimise,"Minimise","Window","Windows"));
            if((app.Style&0x10000)!=0)actions.Add(new(app.Maximized?CapabilityKind.Restore:CapabilityKind.Maximise,app.Maximized?"Restore":"Maximise","Window","Windows"));
            if((app.Style&0x80000)!=0)actions.Add(new(CapabilityKind.Close,"Close window","Window","Windows"));
        }
        var session=media.Sessions.FirstOrDefault(session=>MatchesMedia(app,session.SourceAppUserModelId));
        try{var controls=session?.GetPlaybackInfo().Controls;if(controls!=null){if(controls.IsPlayEnabled)actions.Add(new(CapabilityKind.Play,"Play","Media","Windows media"));if(controls.IsPauseEnabled)actions.Add(new(CapabilityKind.Pause,"Pause","Media","Windows media"));if(controls.IsPreviousEnabled)actions.Add(new(CapabilityKind.Previous,"Previous track","Media","Windows media"));if(controls.IsNextEnabled)actions.Add(new(CapabilityKind.Next,"Next track","Media","Windows media"));}}catch{}
        Actions=actions.GroupBy(action=>(action.Kind,action.Kind==CapabilityKind.Additional?action.Label:"")).Select(group=>group.OrderBy(action=>action.Source=="Application accessibility"?1:0).First()).ToArray();Status=reply?.Status??"Discovering accessible actions…";
    }
    internal static bool MatchesMedia(ActiveWindow app,string source)=>app.AppId.Length>0?string.Equals(app.AppId,source,StringComparison.OrdinalIgnoreCase):string.Equals(System.IO.Path.GetFileName(app.Path),source,StringComparison.OrdinalIgnoreCase);
    internal async Task Execute(ApplicationCapability action)
    {
        var app=Context;if(disposed||executing||app==null||!Actions.Contains(action)||!applications.ValidTarget(app)){Status="The application changed. Select it again before using this action.";Changed?.Invoke();return;}
        executing=true;execution=new CancellationTokenSource();pending?.Cancel();debounce.Stop();int version=++generation;
        try
        {
            if(action.Method=="Window")
            {
                // Re-read the window identity immediately before posting a supported system command.
                if(!applications.ValidTarget(app))return;
                bool sent=action.Kind==CapabilityKind.Close?Native.CloseWindow(app.Handle):Native.ShowWindowAsync(app.Handle,action.Kind==CapabilityKind.Minimise?6:action.Kind==CapabilityKind.Restore?9:3);
                Status=sent?"Window command sent.":"Windows did not accept the window command; the application may have higher privileges.";
            }
            else if(action.Method=="Media")
            {
                var session=media.Sessions.FirstOrDefault(session=>MatchesMedia(app,session.SourceAppUserModelId));bool accepted=session!=null&&(action.Kind switch{CapabilityKind.Play=>await session.TryPlayAsync(),CapabilityKind.Pause=>await session.TryPauseAsync(),CapabilityKind.Previous=>await session.TrySkipPreviousAsync(),CapabilityKind.Next=>await session.TrySkipNextAsync(),_=>false});Status=accepted?"Media command accepted.":"The media application did not accept the command.";
            }
            else if(action.Method.StartsWith("Adapter:",StringComparison.Ordinal))
            {
                var adapter=CapabilityRegistry.Adapters.FirstOrDefault(adapter=>"Adapter:"+adapter.Name==action.Method);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(execution.Token);timeout.CancelAfter(1800);
                bool accepted=adapter!=null&&adapter.Supports(app)&&applications.ValidTarget(app)&&await Task.Run(()=>adapter.ExecuteAsync(app,action,timeout.Token)).WaitAsync(timeout.Token);
                Status=accepted?"Application command accepted.":"The application integration did not accept the action.";
            }
            else
            {
                var result=await RunWorker(new(app.Handle.ToInt64(),app.ProcessId,app.Started,Environment.ProcessId,"execute",action),execution.Token);
                if(!disposed&&generation==version)Status=result.Status;
            }
        }catch{if(!disposed)Status="The application did not accept the action.";}
        finally{execution?.Dispose();execution=null;executing=false;if(!disposed){Changed?.Invoke();Invalidate();}}
    }
    internal static async Task<CapabilityReply> RunWorker(CapabilityRequest request,CancellationToken cancellation,int timeout=2600)
    {
        if(cancellation.IsCancellationRequested)return new(new(),"Application changed.");
        // Separate process bounds all UIA/MSAA calls, including provider calls that ignore managed cancellation.
        var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true};info.ArgumentList.Add("--capability-worker");
        try
        {
            using var worker=Process.Start(info)!;using var limit=CancellationTokenSource.CreateLinkedTokenSource(cancellation);limit.CancelAfter(timeout);
            try{await worker.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));worker.StandardInput.Close();var read=worker.StandardOutput.ReadToEndAsync(limit.Token);await worker.WaitForExitAsync(limit.Token);return JsonSerializer.Deserialize<CapabilityReply>(await read)??new(new(),"No accessible actions reported.");}
            catch(OperationCanceledException){try{if(!worker.HasExited)worker.Kill(true);}catch{}return new(new(),cancellation.IsCancellationRequested?"Application changed.":"Accessibility provider timed out; window controls remain available.");}
        }catch{return new(new(),"Accessibility unavailable; window controls remain available.");}
    }
    public void Dispose(){disposed=true;++generation;pending?.Cancel();pending?.Dispose();execution?.Cancel();debounce.Stop();applications.Changed-=ContextChanged;applications.ContextInvalidated-=Invalidate;media.Changed-=MediaChanged;}
}

internal static class CapabilityWorker
{
    internal static void Run()
    {
        var thread=new Thread(RunCore){IsBackground=false,Name="New Start accessibility client"};thread.SetApartmentState(ApartmentState.MTA);thread.Start();thread.Join();
    }
    static void RunCore()
    {
        CapabilityReply reply;
        try{var input=Console.ReadLine();if(input==null||input.Length>16000)return;var request=JsonSerializer.Deserialize<CapabilityRequest>(input);reply=request==null?new(new(),"Invalid request."):Process(request);}catch(Exception error){reply=new(new(),"Accessibility unavailable ("+error.GetType().Name+"); window controls remain available.");}
        Console.WriteLine(JsonSerializer.Serialize(reply));
    }
    static bool SameWindow(CapabilityRequest request)=>ActiveApplication.Read(new IntPtr(request.Window)) is {} app&&app.ProcessId==request.ProcessId&&app.Started!=0&&app.Started==request.Started;
    static bool ForegroundAllowed(CapabilityRequest request)
    {
        var foreground=Native.GetForegroundWindow();ApplicationIdentity.GetWindowThreadProcessId(foreground,out uint pid);
        return foreground==new IntPtr(request.Window)||pid==request.Caller;
    }
    static CapabilityReply Process(CapabilityRequest request)
    {
        if(!SameWindow(request))return new(new(),"The application window has changed.");
        var window=new IntPtr(request.Window);var root=AutomationElement.FromHandle(window);if(root==null)return new(new(),"No accessibility provider.");
        var app=ApplicationIdentity.Enrich(ActiveApplication.Read(window)!);var adapters=CapabilityRegistry.Adapters.Where(adapter=>{try{return adapter.Supports(app);}catch{return false;}}).ToArray();
        var found=new List<ApplicationCapability>();var patterns=new HashSet<string>();var queue=new Queue<(AutomationElement Element,int Depth)>();queue.Enqueue((root,0));var clock=Stopwatch.StartNew();int inspected=0;
        var walker=TreeWalker.ControlViewWalker;
        while(queue.Count>0&&inspected<300&&clock.ElapsedMilliseconds<1600)
        {
            var (element,depth)=queue.Dequeue();inspected++;
            try
            {
                var state=element.Current;
                if(state.IsPassword)continue;
                string name=state.Name??"";int type=state.ControlType.Id;
                if(state.IsEnabled&&!state.IsOffscreen&&name.Length is >0 and <100)
                {
                    string method="";
                    if(element.TryGetCurrentPattern(InvokePattern.Pattern,out _)){method="Invoke";patterns.Add(method);}
                    if(element.TryGetCurrentPattern(TogglePattern.Pattern,out _)){patterns.Add("Toggle");if(method.Length==0)method="Toggle";}
                    if(element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out _)){patterns.Add("ExpandCollapse");if(method.Length==0)method="Expand";}
                    if(element.TryGetCurrentPattern(SelectionPattern.Pattern,out _))patterns.Add("Selection");
                    if(element.TryGetCurrentPattern(SelectionItemPattern.Pattern,out _))patterns.Add("SelectionItem");
                    if(element.TryGetCurrentPattern(ValuePattern.Pattern,out _))patterns.Add("Value");
                    if(element.TryGetCurrentPattern(RangeValuePattern.Pattern,out _))patterns.Add("RangeValue");
                    var kind=CapabilityRegistry.AccessibleKind(name,state.AutomationId,type);
                    if(type==ControlType.Edit.Id&&state.IsKeyboardFocusable&&kind is CapabilityKind.Search or CapabilityKind.Find)method="Focus";
                    if(method.Length==0&&LegacyCapability.CanInvoke(new IntPtr(state.NativeWindowHandle),name)){method="MSAA";patterns.Add("MSAA default action");}
                    var id=element.GetRuntimeId();ApplicationCapability? action=null;
                    if(method.Length>0)
                    {
                        foreach(var adapter in adapters){try{action=adapter.Recognise(CapabilityRegistry.Normalise(name),type,method,id);if(action!=null){action=action with{Name=name};break;}}catch{}}
                        if(action==null&&kind is {} supported&&(method!="Expand"||type==ControlType.MenuItem.Id))action=new(supported,name,method,"Application accessibility",id,name,type);
                        if(action!=null)found.Add(action);
                    }
                    if(request.Operation=="execute"&&request.Action is {} selected&&selected.RuntimeId!=null&&id.SequenceEqual(selected.RuntimeId)&&name==selected.Name&&type==selected.ControlType)
                    {
                        if(!SameWindow(request)||!ForegroundAllowed(request))return new(new(),"Focus changed; action cancelled.");
                        Native.SetForegroundWindow(window);if(Native.GetForegroundWindow()!=window)return new(new(),"Windows did not allow activating this application.");
                        if(!SameWindow(request)||!element.Current.IsEnabled)return new(new(),"The action is no longer available.");
                        // Verify the exact advertised method again. Never substitute keyboard input.
                        switch(selected.Method)
                        {
                            case "Invoke":((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();break;
                            case "Toggle":((TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern)).Toggle();break;
                            case "Expand":((ExpandCollapsePattern)element.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();break;
                            case "Focus":element.SetFocus();break;
                            case "MSAA":if(!LegacyCapability.Invoke(new IntPtr(element.Current.NativeWindowHandle),name))return new(new(),"Legacy action is no longer available.");break;
                            default:return new(new(),"Unsupported action method.");
                        }
                        return new(new(),"Action accepted by the application.",true,inspected);
                    }
                }
            }catch(Exception error) when(error is not OutOfMemoryException){ /* One incomplete provider node must not discard other capabilities. */ }
            try
            {
                if(depth<8)
                {
                    var child=walker.GetFirstChild(element);int siblings=0;
                    while(child!=null&&siblings++<70&&queue.Count+inspected<400){queue.Enqueue((child,depth+1));child=walker.GetNextSibling(child);}
                }
            }catch(Exception error) when(error is not OutOfMemoryException){}
        }
        if(request.Operation=="execute")return new(new(),"The accessible control changed or is outside the bounded discovery region. Refresh actions.",false,inspected);
        return new(found.GroupBy(action=>(action.Kind,action.Label)).Select(group=>group.First()).Take(24).ToList(),found.Count>0?"Available actions from this window":"No supported accessible commands found; window controls remain available.",false,inspected,patterns.ToArray());
    }
}
