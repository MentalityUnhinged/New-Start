using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NewStart;
internal sealed record PluginLink(string Id,string Label,string Uri);
internal sealed class PluginManifest
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public string Version {get;set;}="1.0.0";
    public int ApiVersion {get;set;}=1;
    public string Provider {get;set;}="links";
    public string Description {get;set;}="";
    public string[] Permissions {get;set;}=Array.Empty<string>();
    public List<PluginLink> Actions {get;set;}=new();
}
internal sealed class PluginConfiguration
{
    public bool Enabled {get;set;}
    public bool Removed {get;set;}
    public bool AppBar {get;set;}=true;
    public bool Dock {get;set;}
    public int RefreshMinutes {get;set;}=5;
    public string? CodexExecutable {get;set;}
    public List<string> GrantedPermissions {get;set;}=new();
}
internal sealed record PluginCapability(string Id,string Label,Uri Target);
internal sealed record PluginComponent(string PluginId,string Label,string Glyph);
internal sealed record PluginRow(string Label,string Value,double? Percent=null);
internal sealed record PluginSnapshot(string CompactText,string Status,IReadOnlyList<PluginRow> Rows,DateTimeOffset? UpdatedAt,bool Stale=false);
internal sealed class PluginEntry
{
    internal PluginManifest Manifest {get;}
    internal PluginConfiguration Configuration {get;}
    internal bool Bundled {get;}
    internal string? ManifestPath {get;}
    internal bool Compatible {get;}
    internal string? Problem {get;}
    internal PluginSnapshot Snapshot {get;set;}=new("—","Disabled",Array.Empty<PluginRow>(),null);
    internal DateTimeOffset NextRefresh {get;set;}
    internal DateTimeOffset LastAttempt {get;set;}
    internal string? Diagnostic {get;set;}
    internal bool Busy {get;set;}
    internal CancellationTokenSource? Lifetime {get;set;}
    internal IPluginProvider? Provider {get;set;}
    internal int Generation {get;set;}
    internal PluginEntry(PluginManifest manifest,PluginConfiguration config,bool bundled,string? problem,string? manifestPath=null){Manifest=manifest;Configuration=config;Bundled=bundled;Compatible=problem==null;Problem=problem;ManifestPath=manifestPath;}
}
// Version 1 accepts data-only manifests and trusted bundled providers, not arbitrary executable code.
internal sealed class PluginManager:IDisposable
{
    internal const int ApiVersion=1;
    internal const string CodexId="newstart.codex-usage";
    readonly Dispatcher ui;readonly DispatcherTimer timer;readonly List<PluginEntry> entries=new();readonly List<string> discoveryDiagnostics=new();readonly string folder;Dictionary<string,PluginConfiguration> saved;bool disposed;
    internal IReadOnlyList<PluginEntry> Entries=>entries;
    internal IReadOnlyList<string> Diagnostics=>discoveryDiagnostics;
    internal event Action? Changed;
    internal PluginManager(Dispatcher dispatcher,string? storageFolder=null)
    {
        ui=dispatcher;folder=storageFolder??Preferences.Folder;saved=Load();Discover();timer=new DispatcherTimer(DispatcherPriority.Background,dispatcher){Interval=TimeSpan.FromSeconds(30)};timer.Tick+=(_,_)=>{foreach(var entry in entries.Where(entry=>entry.Configuration.Enabled&&entry.Compatible&&entry.NextRefresh<=DateTimeOffset.UtcNow).ToArray())_=RefreshAsync(entry.Manifest.Id);};timer.Start();
    }
    Dictionary<string,PluginConfiguration> Load()
    {
        try{return (JsonSerializer.Deserialize<Dictionary<string,PluginConfiguration>>(File.ReadAllText(Path.Combine(folder,"plugins-state.json")))??new()).Where(item=>item.Value!=null).ToDictionary(item=>item.Key,item=>item.Value);}catch{return new();}
    }
    void Save()
    {
        Directory.CreateDirectory(folder);string path=Path.Combine(folder,"plugins-state.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(saved,new JsonSerializerOptions{WriteIndented=true}));File.Move(path+".tmp",path,true);
    }
    internal void Discover()
    {
        if(disposed)return;foreach(var entry in entries)Unload(entry);entries.Clear();discoveryDiagnostics.Clear();
        Add(new PluginManifest{Id=CodexId,Name="Codex usage",Version="1.0.0",Provider="codex-usage",Description="Reported Codex allowance windows using your existing official Codex CLI sign-in. General ChatGPT chat limits and API billing are separate.",Permissions=new[]{"codex-account-usage","launch-codex-app-server"}},true);
        string root=Path.Combine(folder,"Plugins");
        try
        {
            if(Directory.Exists(root))foreach(var directory in Directory.EnumerateDirectories(root).Take(64))
            {
                try
                {
                    if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0){discoveryDiagnostics.Add("Skipped a linked plugin directory.");continue;}
                    string path=Path.Combine(directory,"plugin.json");var file=new FileInfo(path);if(!file.Exists)continue;if(file.Length>64*1024){discoveryDiagnostics.Add("A plugin manifest exceeded 64 KB.");continue;}
                    if((file.Attributes&FileAttributes.ReparsePoint)!=0){discoveryDiagnostics.Add("Skipped a linked manifest.");continue;}
                    var manifest=JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});if(manifest==null)continue;
                    if(entries.Any(entry=>entry.Manifest.Id.Equals(manifest.Id,StringComparison.OrdinalIgnoreCase))){discoveryDiagnostics.Add("Ignored a duplicate plugin identifier.");continue;}Add(manifest,false,path);
                }catch{discoveryDiagnostics.Add("A plugin manifest could not be read.");}
            }
        }catch{discoveryDiagnostics.Add("The plugin discovery folder is unavailable.");}
        foreach(var entry in entries.Where(entry=>entry.Configuration.Enabled&&entry.Compatible&&HasPermissions(entry)).ToArray())LoadProvider(entry);
        Changed?.Invoke();
    }
    void Add(PluginManifest manifest,bool bundled,string? manifestPath=null)
    {
        string? problem=Validate(manifest,bundled);if(string.IsNullOrWhiteSpace(manifest.Id)||manifest.Id.Length>100){discoveryDiagnostics.Add("Ignored an invalid plugin identifier.");return;}
        manifest.Name=Clean(manifest.Name,100);manifest.Description=Clean(manifest.Description,600);
        if(!saved.TryGetValue(manifest.Id,out var config))saved[manifest.Id]=config=new();Normalize(config);
        if(problem!=null||config.Removed||!manifest.Permissions.All(config.GrantedPermissions.Contains))config.Enabled=false;
        entries.Add(new(manifest,config,bundled,problem,manifestPath));
    }
    internal static string? Validate(PluginManifest manifest,bool bundled)
    {
        manifest.Permissions??=Array.Empty<string>();manifest.Actions??=new();
        if(string.IsNullOrWhiteSpace(manifest.Id)||manifest.Id.Length>100||!manifest.Id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_'))return "Invalid plugin ID.";
        if(string.IsNullOrWhiteSpace(manifest.Name)||manifest.Name.Length>100)return "Invalid plugin name.";
        if(!System.Version.TryParse(manifest.Version,out _))return "Invalid plugin version.";
        if(manifest.ApiVersion!=ApiVersion)return "This plugin requires a different New Start plugin API version.";
        if(!PluginProviderRegistry.TryGet(manifest.Provider,out var provider)||!bundled&&!provider.AllowExternalManifests)return "Executable plugin providers are not supported by the data-only API.";
        if(manifest.Permissions.Any(permission=>!provider.Permissions.Contains(permission,StringComparer.Ordinal)))return "Unknown or unsupported plugin permission.";
        if(manifest.Actions.Count>24)return "Too many plugin actions.";
        var identifiers=new HashSet<string>(StringComparer.Ordinal);
        foreach(var action in manifest.Actions)
        {
            if(action==null||string.IsNullOrWhiteSpace(action.Id)||action.Id.Length>80||!action.Id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_')||!identifiers.Add(action.Id)||string.IsNullOrWhiteSpace(action.Label)||action.Label.Length>100||!SafeTarget(action.Uri,out var target))return "A plugin action contains an unsupported target or duplicate identifier.";
            string permission=target!.Scheme=="https"?"open-web-links":"open-system-settings";if(!manifest.Permissions.Contains(permission,StringComparer.Ordinal))return "A plugin action lacks its declared permission.";
        }
        return null;
    }
    internal static bool SafeTarget(string value,out Uri? target)
    {
        target=null;if(string.IsNullOrWhiteSpace(value)||value.Length>2048||value.Any(char.IsControl)||!Uri.TryCreate(value,UriKind.Absolute,out var uri))return false;
        if(uri.Scheme!="https"&&uri.Scheme!="ms-settings")return false;if(uri.Scheme=="https"&&(string.IsNullOrWhiteSpace(uri.Host)||!string.IsNullOrEmpty(uri.UserInfo)))return false;target=uri;return true;
    }
    static string Clean(string? text,int maximum)=>new((text??"").Where(c=>!char.IsControl(c)).Take(maximum).ToArray());
    static void Normalize(PluginConfiguration config){config.RefreshMinutes=Math.Clamp(config.RefreshMinutes,1,60);config.GrantedPermissions=(config.GrantedPermissions??new()).Distinct(StringComparer.Ordinal).ToList();if(config.CodexExecutable?.Length>32768)config.CodexExecutable=null;}
    bool HasPermissions(PluginEntry entry)=>entry.Manifest.Permissions.All(entry.Configuration.GrantedPermissions.Contains);
    internal bool SetEnabled(string id,bool enabled,bool grantPermissions=false)
    {
        var entry=entries.FirstOrDefault(entry=>entry.Manifest.Id==id);if(entry==null||!entry.Compatible)return false;
        if(enabled&&!HasPermissions(entry)){if(!grantPermissions)return false;entry.Configuration.GrantedPermissions=entry.Manifest.Permissions.ToList();}
        entry.Configuration.Enabled=enabled;if(enabled){entry.Configuration.Removed=false;LoadProvider(entry);}else Unload(entry);Save();Changed?.Invoke();return true;
    }
    void LoadProvider(PluginEntry entry)
    {
        Unload(entry);if(!PluginProviderRegistry.TryGet(entry.Manifest.Provider,out var registration))return;
        try{entry.Provider=registration.Create(entry.Manifest);}catch{entry.Diagnostic="The trusted provider could not be loaded.";entry.Snapshot=new(entry.Manifest.Name,entry.Diagnostic,Array.Empty<PluginRow>(),null);return;}
        entry.Lifetime=new();entry.Snapshot=new(entry.Manifest.Provider=="codex-usage"?"Codex —":entry.Manifest.Name,"Loading…",Array.Empty<PluginRow>(),null);entry.NextRefresh=entry.LastAttempt.AddSeconds(10);_=RefreshAsync(entry.Manifest.Id,true);
    }
    static void Unload(PluginEntry entry){entry.Generation++;entry.Lifetime?.Cancel();entry.Lifetime?.Dispose();entry.Lifetime=null;try{entry.Provider?.Dispose();}catch{entry.Diagnostic="The plugin provider reported an error while unloading.";}entry.Provider=null;entry.Busy=false;entry.Snapshot=new("—","Disabled",Array.Empty<PluginRow>(),null);}
    internal void Configure(string id,Action<PluginConfiguration> update)
    {
        var entry=entries.FirstOrDefault(entry=>entry.Manifest.Id==id);if(entry==null)return;int previousInterval=entry.Configuration.RefreshMinutes;update(entry.Configuration);Normalize(entry.Configuration);if(previousInterval!=entry.Configuration.RefreshMinutes)entry.NextRefresh=entry.LastAttempt.AddMinutes(entry.Configuration.RefreshMinutes);Save();Changed?.Invoke();
    }
    internal IReadOnlyList<PluginComponent> ComponentsFor(string location)=>entries.Where(entry=>entry.Configuration.Enabled&&entry.Compatible&&HasPermissions(entry)&&(location.Equals("Dock",StringComparison.OrdinalIgnoreCase)?entry.Configuration.Dock:entry.Configuration.AppBar)).Select(entry=>new PluginComponent(entry.Manifest.Id,entry.Snapshot.CompactText,entry.Manifest.Provider=="codex-usage"?"\uE945":"\uE8A5")).ToArray();
    internal IReadOnlyList<PluginCapability> Capabilities=>entries.Where(entry=>entry.Configuration.Enabled&&entry.Compatible&&HasPermissions(entry)).SelectMany(entry=>entry.Manifest.Actions.Where(action=>SafeTarget(action.Uri,out _)).Select(action=>new PluginCapability(entry.Manifest.Id+":"+action.Id,action.Label,new Uri(action.Uri)))).ToArray();
    internal async Task RefreshAsync(string id,bool force=false)
    {
        var entry=entries.FirstOrDefault(entry=>entry.Manifest.Id==id);if(disposed||entry==null||entry.Busy||!entry.Configuration.Enabled||!entry.Compatible||!HasPermissions(entry)||entry.Lifetime==null||entry.Provider==null)return;
        var now=DateTimeOffset.UtcNow;if(now-entry.LastAttempt<TimeSpan.FromSeconds(10)||!force&&now<entry.NextRefresh)return;
        entry.LastAttempt=now;entry.NextRefresh=now.AddMinutes(entry.Configuration.RefreshMinutes);entry.Busy=true;int generation=entry.Generation;var cancellation=entry.Lifetime.Token;var old=entry.Snapshot;Changed?.Invoke();
        try
        {
            using var request=CancellationTokenSource.CreateLinkedTokenSource(cancellation);request.CancelAfter(TimeSpan.FromSeconds(25));var provider=entry.Provider;
            PluginSnapshot result=await Task.Run(()=>provider.ReadAsync(entry.Configuration,request.Token),request.Token).WaitAsync(request.Token);
            if(disposed||cancellation.IsCancellationRequested||entry.Generation!=generation)return;entry.Snapshot=result;entry.Diagnostic=null;
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested){}
        catch(OperationCanceledException){entry.Diagnostic="The plugin provider timed out.";entry.Snapshot=old.UpdatedAt!=null?old with{Status=entry.Diagnostic,Stale=true}:new(entry.Manifest.Name,entry.Diagnostic,Array.Empty<PluginRow>(),null);}
        catch(Exception error)
        {
            if(disposed||cancellation.IsCancellationRequested||entry.Generation!=generation)return;
            // Provider exceptions are reduced to bounded known status; no stdout, stderr, auth tokens or raw responses are persisted.
            string diagnostic=entry.Manifest.Provider=="codex-usage"&&error is InvalidOperationException?Clean(error.Message,400):"The plugin provider is unavailable ("+error.GetType().Name+").";
            entry.Diagnostic=diagnostic;entry.Snapshot=old.UpdatedAt!=null?old with{Status=diagnostic,Stale=true}:new(entry.Manifest.Provider=="codex-usage"?"Codex —":entry.Manifest.Name,diagnostic,Array.Empty<PluginRow>(),null);
        }
        finally{if(!disposed&&entry.Generation==generation){entry.Busy=false;Changed?.Invoke();}}
    }
    internal bool Execute(string capabilityId)
    {
        var action=Capabilities.FirstOrDefault(item=>item.Id==capabilityId);if(action==null||!SafeTarget(action.Target.AbsoluteUri,out _))return false;
        try{Process.Start(new ProcessStartInfo(action.Target.AbsoluteUri){UseShellExecute=true});return true;}catch{return false;}
    }
    internal bool Remove(string id)
    {
        var entry=entries.FirstOrDefault(entry=>entry.Manifest.Id==id);if(entry==null)return false;
        Unload(entry);entry.Configuration.Enabled=false;entry.Configuration.Removed=true;entry.Configuration.GrantedPermissions.Clear();entry.Configuration.CodexExecutable=null;entry.Configuration.RefreshMinutes=5;
        // Keep a reversible manifest backup; never recursively delete a plugin or user-owned directory.
        if(!entry.Bundled&&entry.ManifestPath is {} path&&File.Exists(path)){string backup=path+".removed";if(File.Exists(backup))backup=path+".removed-"+Guid.NewGuid().ToString("N");File.Move(path,backup);entries.Remove(entry);}
        Save();Changed?.Invoke();return true;
    }
    internal string DiscoveryFolder=>Path.Combine(folder,"Plugins");
    internal Button BuildCompactButton(string id,Action open)
    {
        var button=new Button{Padding=new Thickness(7,2,7,2),MinWidth=32,Height=30,FontSize=12};StartMenuWindow.Borderless(button);
        void Update(){var entry=entries.FirstOrDefault(item=>item.Manifest.Id==id);button.Content=entry?.Snapshot.CompactText??"Plugin";button.ToolTip=entry?.Snapshot.Status;System.Windows.Automation.AutomationProperties.SetName(button,(entry?.Manifest.Name??"Plugin")+" status");}
        button.Click+=(_,_)=>open();button.Loaded+=(_,_)=>{Changed+=Update;Update();};button.Unloaded+=(_,_)=>Changed-=Update;Update();return button;
    }
    internal FrameworkElement BuildPanel(string id)
    {
        var root=new StackPanel();
        void Text(string text,int size=12){root.Children.Add(new TextBlock{Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,7)});}
        void Update()
        {
            root.Children.Clear();var entry=entries.FirstOrDefault(item=>item.Manifest.Id==id);if(entry==null){Text("Plugin unavailable");return;}Text(entry.Manifest.Name,16);
            if(!entry.Configuration.Enabled){Text("This plugin is disabled.");return;}
            foreach(var row in entry.Snapshot.Rows)
            {
                Text(row.Label);Text(row.Value);if(row.Percent is double percent)root.Children.Add(new ProgressBar{Minimum=0,Maximum=100,Value=percent,Height=3,Margin=new Thickness(0,0,0,9)});
            }
            if(entry.Snapshot.Stale)Text("Last successful values are stale.");Text(entry.Snapshot.Status);
            if(entry.Snapshot.UpdatedAt is {} updated)Text("Updated "+updated.LocalDateTime.ToString("g"));
            var refresh=new Button{Content=entry.Busy?"Refreshing…":"Refresh",IsEnabled=!entry.Busy,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,3,0,0)};refresh.Click+=(_,_)=>_=RefreshAsync(id,true);root.Children.Add(refresh);
            foreach(var capability in Capabilities.Where(item=>item.Id.StartsWith(id+":",StringComparison.Ordinal))){var action=capability;var button=new Button{Content=action.Label,Margin=new Thickness(0,5,0,0),HorizontalAlignment=HorizontalAlignment.Left};button.Click+=(_,_)=>Execute(action.Id);root.Children.Add(button);}
        }
        root.Loaded+=(_,_)=>{Changed+=Update;Update();};root.Unloaded+=(_,_)=>Changed-=Update;Update();return root;
    }
    internal void OpenConfiguration(Window owner,Preferences prefs)=>PluginConfigurationWindow.Open(this,owner,prefs);
    public void Dispose(){if(disposed)return;disposed=true;timer.Stop();foreach(var entry in entries)Unload(entry);}
}
