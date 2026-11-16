using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace NewStart;
internal sealed record IconOverride(string Identity,string Scope,string File);
internal sealed record IconChoice(string Name,string Value){public override string ToString()=>Name;}
internal static class CustomIcons
{
    static readonly List<IconOverride> entries=new();static readonly Dictionary<string,ImageSource?> cache=new(StringComparer.OrdinalIgnoreCase);static string loaded="";
    internal static event Action? Changed;
    static string Folder=>Path.Combine(Preferences.Folder,"CustomIcons");
    internal static string Identity(string target)=>target.StartsWith("component:",StringComparison.Ordinal)?target:ApplicationIdentity.StableTarget(PinIdentity.Resolve(target)).ToUpperInvariant();
    static bool Asset(string? file)=>!string.IsNullOrWhiteSpace(file)&&Path.GetFileName(file)==file&&Path.GetExtension(file).ToLowerInvariant() is ".ico" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";
    static bool Valid(IconOverride? item)=>item!=null&&!string.IsNullOrWhiteSpace(item.Identity)&&item.Scope is "application" or "dock" or "appbar"&&Asset(item.File);
    static BitmapImage Decode(Stream stream)
    {
        var frame=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None).Frames[0];int width=frame.PixelWidth,height=frame.PixelHeight;if(width<=0||height<=0||width>32768||height>32768)throw new InvalidOperationException("The image dimensions are unsupported.");stream.Position=0;
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;if(width>=height)image.DecodePixelWidth=Math.Min(128,width);else image.DecodePixelHeight=Math.Min(128,height);image.StreamSource=stream;image.EndInit();image.Freeze();return image;
    }
    static void Load(){if(loaded==Folder)return;loaded=Folder;entries.Clear();cache.Clear();try{entries.AddRange((JsonSerializer.Deserialize<List<IconOverride>>(File.ReadAllText(Path.Combine(Folder,"overrides.json")))??new()).Where(Valid));}catch{}}
    internal static IReadOnlyList<IconOverride> Entries{get{Load();return entries.ToArray();}}
    internal static ImageSource? Cached(string target,string scope)
    {
        Load();string id=Identity(target);var item=entries.LastOrDefault(item=>Identity(item.Identity)==id&&item.Scope==scope)??entries.LastOrDefault(item=>Identity(item.Identity)==id&&item.Scope=="application");
        if(item==null||!Asset(item.File))return null;
        if(cache.TryGetValue(item.File,out var found))return found;
        try{using var file=File.OpenRead(Path.Combine(Folder,item.File));if(file.Length>10*1024*1024)return cache[item.File]=null;return cache[item.File]=Decode(file);}catch{return cache[item.File]=null;}
    }
    internal static async Task<ImageSource?> Resolve(string target,string scope)=>Cached(target,scope)??await StartCatalog.Icon(target);
    internal static void Set(string target,string scope,string file)
    {
        Load();if(string.IsNullOrWhiteSpace(target))throw new ArgumentException("Choose an application or supported component.");if(scope is not ("application" or "dock" or "appbar"))throw new ArgumentException("Unknown icon location.");
        string ext=Path.GetExtension(file).ToLowerInvariant();if(ext is not (".ico" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")||new FileInfo(file).Length>10*1024*1024)throw new InvalidOperationException("Choose an ICO, PNG, JPEG, BMP or GIF smaller than 10 MB.");
        // Decode before persisting, so a corrupt image cannot replace a working icon.
        using(var stream=File.OpenRead(file))_ =Decode(stream);
        Directory.CreateDirectory(Folder);string name=Guid.NewGuid().ToString("N")+ext;File.Copy(file,Path.Combine(Folder,name));var next=entries.Where(item=>Identity(item.Identity)!=Identity(target)||item.Scope!=scope).ToList();next.Add(new(Identity(target),scope,name));try{Save(next);}catch{try{File.Delete(Path.Combine(Folder,name));}catch{}throw;}
    }
    internal static void Remove(string target,string scope){Load();Save(entries.Where(item=>Identity(item.Identity)!=Identity(target)||item.Scope!=scope).ToList());}
    static void Save(List<IconOverride> next)
    {
        Directory.CreateDirectory(Folder);string path=Path.Combine(Folder,"overrides.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(next));File.Move(path+".tmp",path,true);
        foreach(var old in entries.Where(item=>!next.Any(current=>current.File==item.File)))if(Guid.TryParseExact(Path.GetFileNameWithoutExtension(old.File),"N",out _))try{File.Delete(Path.Combine(Folder,old.File));}catch{}
        entries.Clear();entries.AddRange(next);cache.Clear();if(Changed is {} changed)foreach(Action callback in changed.GetInvocationList())try{callback();}catch{ /* A renderer must not invalidate an already persisted asset. */ }
    }
    internal static void AddMenu(ContextMenu menu,Window owner,Preferences prefs,string target,string scope)
    {
        menu.Items.Add(new Separator());var choose=new MenuItem{Header="Custom icon…"};choose.Click+=(_,_)=>Open(owner,prefs,target,scope);menu.Items.Add(choose);
        var reset=new MenuItem{Header="Restore original icon"};reset.Click+=(_,_)=>Remove(target,scope);menu.Items.Add(reset);
    }
    internal static void Open(Window owner,Preferences prefs,string? target=null,string scope="application")
    {
        var panel=new StackPanel{Margin=new Thickness(12)};var identity=new TextBox{Text=target??Entries.FirstOrDefault()?.Identity??"component:start",Margin=new Thickness(0,6,0,8)};
        panel.Children.Add(new TextBlock{Text="Choose an installed application, a component, or enter an application path.",TextWrapping=TextWrapping.Wrap});
        var choices=new[]{new IconChoice("Start","component:start"),new IconChoice("Task View","component:task-view"),new IconChoice("Show Desktop","component:show-desktop"),new IconChoice("System controls","component:system-controls"),new IconChoice("Recycle Bin","component:recycle-bin")};
        var allChoices=choices.Concat(((System.Collections.Generic.IEnumerable<StartApp>?)StartCatalog.Current?.Apps??Array.Empty<StartApp>()).OrderBy(app=>app.Name,StringComparer.CurrentCultureIgnoreCase).Select(app=>new IconChoice(app.Name,app.Target))).ToArray();
        var picker=new ComboBox{ItemsSource=allChoices,DisplayMemberPath="Name",SelectedItem=allChoices.FirstOrDefault(item=>Identity(item.Value)==Identity(identity.Text)),Margin=new Thickness(0,6,0,6),ToolTip="Application or component"};picker.SelectionChanged+=(_,_)=>{if(picker.SelectedItem is IconChoice selected)identity.Text=selected.Value;};panel.Children.Add(picker);panel.Children.Add(new Expander{Header="Choose by application path",Content=identity,Margin=new Thickness(0,0,0,8)});
        var selectedName=new TextBlock{FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8),TextWrapping=TextWrapping.Wrap};panel.Children.Add(selectedName);
        var scopes=new[]{new IconChoice("Everywhere","application"),new IconChoice("Dock only","dock"),new IconChoice("AppBar only","appbar")};
        var location=new ComboBox{ItemsSource=scopes,DisplayMemberPath="Name",SelectedValuePath="Value",SelectedValue=scope,Margin=new Thickness(0,0,0,8)};panel.Children.Add(location);string Scope()=>location.SelectedValue as string??"application";
        var preview=new Image{Width=64,Height=64,Margin=new Thickness(0,6,0,12)};panel.Children.Add(preview);var status=new TextBlock{TextWrapping=TextWrapping.Wrap};panel.Children.Add(status);
        bool refreshing=false;int previewVersion=0;string Name(string value)=>choices.FirstOrDefault(choice=>choice.Value==value)?.Name??StartCatalog.Current?.Apps.FirstOrDefault(app=>Identity(app.Target)==Identity(value))?.Name??Path.GetFileName(value);
        async void Refresh()
        {
            if(refreshing)return;refreshing=true;string value=identity.Text;int version=++previewVersion;
            try{bool dockOnly=value.StartsWith("component:",StringComparison.Ordinal)&&value!="component:start";var allowed=scopes.Where(item=>!dockOnly||item.Value!="appbar").ToArray();string selected=Scope();location.ItemsSource=allowed;location.SelectedValue=allowed.Any(item=>item.Value==selected)?selected:"application";picker.SelectedItem=allChoices.FirstOrDefault(item=>Identity(item.Value)==Identity(value));selectedName.Text=Name(value);preview.Source=Cached(value,Scope());}finally{refreshing=false;}
            if(preview.Source==null&&!value.StartsWith("component:",StringComparison.Ordinal))try{var original=await StartCatalog.Icon(value);if(version==previewVersion&&identity.Text==value)preview.Source=original;}catch{ /* The override picker remains usable without an original icon. */ }
        }
        var choose=new Button{Content="Choose image…",Margin=new Thickness(0,6,0,6)};panel.Children.Add(choose);
        Window? window=null;
        var savedList=new StackPanel();void RebuildSaved(){savedList.Children.Clear();foreach(var item in Entries){var saved=item;var button=new Button{Content=Name(item.Identity)+" · "+scopes.First(choice=>choice.Value==item.Scope).Name,Margin=new Thickness(0,4,0,0)};button.Click+=(_,_)=>{identity.Text=saved.Identity;location.SelectedValue=saved.Scope;Refresh();};savedList.Children.Add(button);}}
        choose.Click+=(_,_)=>{var picker=new OpenFileDialog{Filter="Icons and images|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif"};if(picker.ShowDialog(window)==true)try{Set(identity.Text,Scope(),picker.FileName);Refresh();RebuildSaved();status.Text="Icon applied.";}catch(Exception error){status.Text=error.Message;}};
        var restore=new Button{Content="Restore original",Margin=new Thickness(0,0,0,12)};restore.Click+=(_,_)=>{Remove(identity.Text,Scope());Refresh();RebuildSaved();status.Text="Original icon restored.";};panel.Children.Add(restore);
        panel.Children.Add(new TextBlock{Text="Saved overrides",FontWeight=FontWeights.SemiBold});
        panel.Children.Add(savedList);RebuildSaved();
        identity.TextChanged+=(_,_)=>Refresh();location.SelectionChanged+=(_,_)=>Refresh();Refresh();window=ConfigurationWindow.Open(owner,prefs,"Custom Icons",panel,520,560);
    }
}
internal static class ConfigurationWindow
{
    internal static Window Open(Window owner,Preferences prefs,string title,FrameworkElement content,double width=560,double height=520)
    {
        var window=new Window{Owner=owner,Title="New Start · "+title,Width=width,Height=height,MinWidth=360,MinHeight=250,WindowStartupLocation=WindowStartupLocation.CenterOwner,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false};
        var layout=new DockPanel();var header=new DockPanel{Margin=new Thickness(14,10,10,8)};var close=new Button{Content="×",Width=32,Height=28};close.Click+=(_,_)=>window.Close();DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);var caption=new TextBlock{Text=title,FontSize=18,VerticalAlignment=VerticalAlignment.Center};caption.MouseLeftButtonDown+=(_,_)=>window.DragMove();header.Children.Add(caption);DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);layout.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var frame=new Border{CornerRadius=new CornerRadius(UiMetrics.Corner),Child=layout,BorderThickness=new Thickness(1)};window.Content=frame;
        void Theme(){bool appBar=prefs.AppBarEnabled;bool dark=SurfaceAppearance.WhiteInk(SurfaceAppearance.Effective(prefs,appBar));Appearance.Apply(window,dark);double transparency=SurfaceAppearance.Transparency(prefs,appBar),blur=SurfaceAppearance.Blur(prefs,appBar);window.Background=Brushes.Transparent;if(transparency>0&&transparency<1&&NativeBackdrop.TransparencyAllowed())CustomBackdrop.Apply(window,blur);else CustomBackdrop.Hide(window);var material=SurfaceAppearance.Material(SurfaceAppearance.Background(prefs,appBar),transparency);frame.Background=SystemParameters.HighContrast?SystemColors.WindowBrush:material.Tint;frame.BorderBrush=SystemParameters.HighContrast?SystemColors.WindowTextBrush:material.Rim;}
        bool closed=false;void ThemeChanged(object sender,Microsoft.Win32.UserPreferenceChangedEventArgs args){if(!closed)window.Dispatcher.BeginInvoke(new Action(()=>{if(!closed)Theme();}));}
        NativeBackdrop.Configure(window);window.Loaded+=(_,_)=>Theme();window.Activated+=(_,_)=>Theme();Microsoft.Win32.SystemEvents.UserPreferenceChanged+=ThemeChanged;window.PreviewKeyDown+=(_,args)=>{if(args.Key==System.Windows.Input.Key.Escape)window.Close();};window.Closed+=(_,_)=>{closed=true;Microsoft.Win32.SystemEvents.UserPreferenceChanged-=ThemeChanged;CustomBackdrop.Hide(window);};window.Show();return window;
    }
}
