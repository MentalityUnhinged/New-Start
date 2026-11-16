using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace NewStart;
internal sealed class PluginConfigurationWindow:Window
{
    readonly PluginManager manager;readonly Preferences preferences;readonly Border frame;readonly ListBox list=new();readonly StackPanel details=new();readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};readonly TextBlock diagnostics=new(){TextWrapping=TextWrapping.Wrap,FontSize=11};
    string? selected;bool rebuilding,closed;
    internal static void Open(PluginManager manager,Window owner,Preferences preferences)
    {
        var existing=Application.Current?.Windows.OfType<PluginConfigurationWindow>().FirstOrDefault(window=>ReferenceEquals(window.manager,manager));
        if(existing!=null){if(existing.WindowState==WindowState.Minimized)existing.WindowState=WindowState.Normal;existing.Activate();return;}
        new PluginConfigurationWindow(manager,owner,preferences).Show();
    }
    internal PluginConfigurationWindow(PluginManager manager,Window? owner,Preferences preferences)
    {
        this.manager=manager;this.preferences=preferences;Title="New Start · Plugins";Width=760;Height=620;MinWidth=620;MinHeight=420;WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.CanResizeWithGrip;ShowInTaskbar=owner==null;WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;if(owner!=null)Owner=owner;NativeBackdrop.Configure(this);
        var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var header=new DockPanel{Margin=new Thickness(14,10,10,10),LastChildFill=true};header.MouseLeftButtonDown+=(_,eventArgs)=>{if(eventArgs.OriginalSource is TextBlock&&eventArgs.ClickCount==1)DragMove();};
        var close=Button("✕",Close);close.Width=30;close.Height=28;close.Padding=new Thickness(0);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);header.Children.Add(new TextBlock{Text="Plugins",FontSize=20,VerticalAlignment=VerticalAlignment.Center});root.Children.Add(header);
        var content=new Grid{Margin=new Thickness(12,0,12,8)};content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});content.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(content,1);root.Children.Add(content);
        var sidebar=new DockPanel{Margin=new Thickness(0,0,12,0)};var tools=new StackPanel{Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(tools,Dock.Bottom);tools.Children.Add(Button("Find installed plugins",()=>{manager.Discover();BuildList();}));tools.Children.Add(Button("Open plugin folder",()=>{Directory.CreateDirectory(manager.DiscoveryFolder);Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=true,ArgumentList={manager.DiscoveryFolder}});}));sidebar.Children.Add(tools);
        list.BorderThickness=new Thickness(0);list.Background=Brushes.Transparent;list.SetResourceReference(Control.ForegroundProperty,"Text");list.SelectionChanged+=(_,_)=>{if(rebuilding)return;selected=(list.SelectedItem as ListBoxItem)?.Tag as string;BuildDetails();};sidebar.Children.Add(list);content.Children.Add(sidebar);
        var scroll=new ScrollViewer{Content=details,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,PanningMode=PanningMode.VerticalOnly};Grid.SetColumn(scroll,1);content.Children.Add(scroll);
        var footer=new StackPanel{Margin=new Thickness(14,0,14,12)};footer.Children.Add(new TextBlock{Text="Plugin API 1 · Only declared, approved capabilities are loaded.",FontSize=11,Margin=new Thickness(0,0,0,4)});footer.Children.Add(diagnostics);Grid.SetRow(footer,2);root.Children.Add(footer);
        frame=new Border{CornerRadius=new CornerRadius(10),Child=root,BorderThickness=new Thickness(1)};Content=frame;ApplyTheme();SourceInitialized+=(_,_)=>ApplyTheme();Activated+=(_,_)=>ApplyTheme();manager.Changed+=OnChanged;SystemEvents.UserPreferenceChanged+=ThemeChanged;
        Closed+=(_,_)=>{closed=true;manager.Changed-=OnChanged;SystemEvents.UserPreferenceChanged-=ThemeChanged;CustomBackdrop.Hide(this);};PreviewKeyDown+=(_,eventArgs)=>{if(eventArgs.Key==Key.Escape){Close();eventArgs.Handled=true;}};BuildList();
    }
    void ApplyTheme()
    {
        bool appBar=preferences.AppBarEnabled,dark=SurfaceAppearance.WhiteInk(SurfaceAppearance.Effective(preferences,appBar));Appearance.Apply(this,dark);Background=Brushes.Transparent;double transparency=SurfaceAppearance.Transparency(preferences,appBar);if(transparency>0&&transparency<1&&NativeBackdrop.TransparencyAllowed())CustomBackdrop.Apply(this,SurfaceAppearance.Blur(preferences,appBar));else CustomBackdrop.Hide(this);
        var material=SurfaceAppearance.Material(SurfaceAppearance.Background(preferences,appBar),SurfaceAppearance.Transparency(preferences,appBar));frame.Background=SystemParameters.HighContrast?SystemColors.WindowBrush:material.Tint;frame.BorderBrush=SystemParameters.HighContrast?SystemColors.WindowTextBrush:material.Rim;
    }
    void ThemeChanged(object sender,UserPreferenceChangedEventArgs args){if(!closed)Dispatcher.BeginInvoke(new Action(()=>{if(!closed)ApplyTheme();}));}
    void OnChanged()
    {
        if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(OnChanged));return;}UpdateStatus();
        if(list.Items.Count!=manager.Entries.Count)BuildList();
        else foreach(ListBoxItem item in list.Items){var entry=manager.Entries.FirstOrDefault(entry=>entry.Manifest.Id==(string)item.Tag);if(entry!=null)item.Content=ListLabel(entry);}
    }
    static string ListLabel(PluginEntry entry)=>entry.Manifest.Name+"\n"+(entry.Configuration.Removed?"Not installed":!entry.Compatible?"Unavailable":entry.Configuration.Enabled?"Enabled":"Disabled");
    void BuildList()
    {
        rebuilding=true;list.Items.Clear();foreach(var entry in manager.Entries){var item=new ListBoxItem{Tag=entry.Manifest.Id,Content=ListLabel(entry),Padding=new Thickness(8,7,8,7),HorizontalContentAlignment=HorizontalAlignment.Stretch};list.Items.Add(item);if(entry.Manifest.Id==selected)list.SelectedItem=item;}
        if(list.SelectedItem==null&&list.Items.Count>0)list.SelectedIndex=0;selected=(list.SelectedItem as ListBoxItem)?.Tag as string;rebuilding=false;BuildDetails();
    }
    void BuildDetails()
    {
        details.Children.Clear();var entry=manager.Entries.FirstOrDefault(entry=>entry.Manifest.Id==selected);if(entry==null){details.Children.Add(Label("No installed plugins."));UpdateStatus();return;}
        details.Children.Add(Label(entry.Manifest.Name,18));details.Children.Add(Label($"Version {entry.Manifest.Version} · API {entry.Manifest.ApiVersion} · {(entry.Bundled?"Bundled provider":"Data-only manifest")}",11));details.Children.Add(Label(entry.Manifest.Description));if(entry.Problem!=null)details.Children.Add(Label(entry.Problem));
        var enable=new CheckBox{Content=entry.Configuration.Removed?"Install and enable plugin":"Enable plugin",IsChecked=entry.Configuration.Enabled,IsEnabled=entry.Compatible,Margin=new Thickness(0,4,0,10)};
        enable.Checked+=(_,_)=>ChangeEnable(entry,enable,true);enable.Unchecked+=(_,_)=>ChangeEnable(entry,enable,false);details.Children.Add(enable);
        details.Children.Add(Label("Placement",14));details.Children.Add(Check("Show in AppBar",entry.Configuration.AppBar,value=>Save(entry,config=>config.AppBar=value)));details.Children.Add(Check("Show in Dock",entry.Configuration.Dock,value=>Save(entry,config=>config.Dock=value)));
        details.Children.Add(Label("Permissions",14));if(entry.Manifest.Permissions.Length==0)details.Children.Add(Label("No additional permissions."));foreach(string permission in entry.Manifest.Permissions)details.Children.Add(Label("• "+Permission(permission),11));
        if(entry.Manifest.Provider=="codex-usage")
        {
            details.Children.Add(Label("Usage refresh",14));var valueLabel=Label(entry.Configuration.RefreshMinutes+" minutes",11);details.Children.Add(valueLabel);var interval=new Slider{Minimum=1,Maximum=60,TickFrequency=1,IsSnapToTickEnabled=true,Value=entry.Configuration.RefreshMinutes,Margin=new Thickness(0,0,0,6)};interval.ValueChanged+=(_,_)=>{int minutes=(int)Math.Round(interval.Value);valueLabel.Text=minutes+" minutes";Save(entry,config=>config.RefreshMinutes=minutes);};details.Children.Add(interval);
            details.Children.Add(Label("Official Codex CLI",14));var executable=new TextBox{Text=entry.Configuration.CodexExecutable??"Automatic discovery of installed codex.exe",IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Padding=new Thickness(7,5,7,5),Margin=new Thickness(0,0,0,5)};details.Children.Add(executable);
            var paths=new WrapPanel();paths.Children.Add(Button("Choose codex.exe",()=>{var choose=new OpenFileDialog{Title="Select the official Codex CLI",Filter="Codex executable (codex.exe)|codex.exe",CheckFileExists=true,Multiselect=false};if(choose.ShowDialog(this)==true){if(CodexUsageClient.FindExecutable(choose.FileName)==null){Notice("Choose the native codex.exe from an official Codex installation.");return;}Save(entry,config=>config.CodexExecutable=choose.FileName);executable.Text=choose.FileName;}}));paths.Children.Add(Button("Use automatic discovery",()=>{Save(entry,config=>config.CodexExecutable=null);executable.Text="Automatic discovery of installed codex.exe";}));details.Children.Add(paths);
            details.Children.Add(Label("Uses your existing Codex sign-in through its documented local app-server. New Start never reads authentication files. It does not report general ChatGPT chat limits or OpenAI API billing.",11));
        }
        details.Children.Add(Label("Status and diagnostics",14));status.Margin=new Thickness(0,0,0,6);details.Children.Add(status);var actions=new WrapPanel();actions.Children.Add(Button("Refresh now",()=>_ =manager.RefreshAsync(entry.Manifest.Id,true)));actions.Children.Add(Button(entry.Bundled?"Remove plugin registration":"Remove plugin",()=>{string explanation=entry.Bundled?"Disable this plugin, cancel its work and remove its saved permission grants and configuration? Its bundled provider can be enabled again here.":"Disable this plugin and move its data-only manifest to a reversible .removed backup? No application files or user folders will be deleted.";if(MessageBox.Show(this,explanation,"Remove plugin",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes){manager.Remove(entry.Manifest.Id);BuildList();}}));details.Children.Add(actions);UpdateStatus();
    }
    void ChangeEnable(PluginEntry entry,CheckBox checkbox,bool enabled)
    {
        if(rebuilding)return;bool permitted=entry.Manifest.Permissions.All(entry.Configuration.GrantedPermissions.Contains);
        if(enabled&&!permitted)
        {
            string permissions=string.Join("\n",entry.Manifest.Permissions.Select(value=>"• "+Permission(value)));string launch=entry.Manifest.Provider=="codex-usage"?"\n\nThis starts the installed official Codex CLI in read-only app-server mode using its existing configuration and sign-in. No agent task is started. Usage data stays on this device.":"\n\nLinks open only when you choose an action. No plugin code will execute.";
            if(MessageBox.Show(this,"Allow "+entry.Manifest.Name+" to use:\n\n"+permissions+launch,"Plugin permissions",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes){rebuilding=true;checkbox.IsChecked=false;rebuilding=false;return;}
        }
        try{if(!manager.SetEnabled(entry.Manifest.Id,enabled,enabled&&!permitted)){rebuilding=true;checkbox.IsChecked=false;rebuilding=false;Notice("This plugin cannot be enabled with its current API version or permissions.");}}catch(IOException){Notice("Plugin settings could not be saved. Check the New Start settings folder.");}catch(UnauthorizedAccessException){Notice("New Start cannot write its plugin settings folder.");}
    }
    void Save(PluginEntry entry,Action<PluginConfiguration> update){try{manager.Configure(entry.Manifest.Id,update);}catch(IOException){Notice("Plugin settings could not be saved.");}catch(UnauthorizedAccessException){Notice("New Start cannot write its plugin settings folder.");}}
    void UpdateStatus()
    {
        var entry=manager.Entries.FirstOrDefault(entry=>entry.Manifest.Id==selected);status.Text=entry==null?"":entry.Configuration.Removed?"Registration removed. Enable this bundled provider to install it again.":entry.Busy?"Refreshing…":entry.Snapshot.Status+(entry.Snapshot.Stale?"\nLast successful values are stale.":"")+(entry.Snapshot.UpdatedAt is {} at?"\nLast successful update: "+at.LocalDateTime.ToString("g"):"");diagnostics.Text=string.Join("\n",manager.Diagnostics.Take(4));
    }
    void Notice(string message){status.Text=message;}
    static string Permission(string value)=>value switch{"codex-account-usage"=>"Read reported Codex usage limits from your existing authorised sign-in","launch-codex-app-server"=>"Launch and stop the official local Codex app-server","open-web-links"=>"Open declared HTTPS links in your default browser","open-system-settings"=>"Open declared Windows Settings pages",_=>value};
    static TextBlock Label(string text,int size=12)=>new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,size>=14?10:0,0,7)};
    static Button Button(string text,Action action)
    {
        var button=new Button{Content=text,Padding=new Thickness(9,5,9,5),Margin=new Thickness(0,0,6,6),HorizontalAlignment=HorizontalAlignment.Left,MinHeight=28};button.Click+=(_,_)=>{try{action();}catch(IOException){MessageBox.Show("This plugin operation could not access its files.","New Start",MessageBoxButton.OK,MessageBoxImage.Information);}catch(UnauthorizedAccessException){MessageBox.Show("New Start cannot access this plugin location.","New Start",MessageBoxButton.OK,MessageBoxImage.Information);}catch(System.ComponentModel.Win32Exception){MessageBox.Show("Windows could not open this item.","New Start",MessageBoxButton.OK,MessageBoxImage.Information);}};return button;
    }
    static CheckBox Check(string text,bool initial,Action<bool> change){var check=new CheckBox{Content=text,IsChecked=initial,Margin=new Thickness(0,0,0,8)};check.Checked+=(_,_)=>change(true);check.Unchecked+=(_,_)=>change(false);return check;}
}
