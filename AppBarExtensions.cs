using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NewStart;
internal sealed partial class AdaptiveAppBarWindow
{
    readonly StackPanel categoryButtons=new(){Orientation=Orientation.Horizontal},pluginButtons=new(){Orientation=Orientation.Horizontal};
    Button? trayButton;string? selectedPlugin;string menuSignature="",pluginSignature="";
    Panel? leftHeader,rightHeader;
    Preferences ContextPreferences()
    {
        return SurfaceAppearance.MenuPreferences(Prefs,true,Display.Name);
    }
    internal void InvalidateIcons(){iconIdentity=null;ApplicationChanged();}
    void InitialiseEnhancements(Panel left,Panel right,Button start,Button app)
    {
        leftHeader=left;rightHeader=right;left.ClipToBounds=true;right.ClipToBounds=true;
        left.Children.Add(categoryButtons);right.Children.Add(pluginButtons);
        var power=new Button{Content=battery,Padding=new Thickness(0),Height=BarHeight,ToolTip="Power and battery"};StartMenuWindow.Borderless(power);power.SetBinding(VisibilityProperty,new System.Windows.Data.Binding("Visibility"){Source=battery});System.Windows.Automation.AutomationProperties.SetName(power,"Power and battery");right.Children.Remove(battery);right.Children.Add(power);buttons[AppBarSection.Power]=power;power.Click+=(_,_)=>Toggle(AppBarSection.Power);
        trayButton=NotificationTray.BuildButton(this,ContextPreferences(),host.RevealTray,ContextPreferences);right.Children.Add(trayButton);
        root.ContextMenu=GeneralMenu();
        foreach(var button in buttons.Values.Distinct())button.ContextMenu=ComponentMenu(System.Windows.Automation.AutomationProperties.GetName(button));
        app.ContextMenuOpening+=(_,_)=>{app.ContextMenu=GeneralMenu();if(host.Applications.Current is {} current){string target=current.IconTarget.Length>0?current.IconTarget:current.Path;CustomIcons.AddMenu(app.ContextMenu,this,Prefs,target,"appbar");}};
        start.ContextMenu=ComponentMenu("Start");RefreshEnhancements();
    }
    ContextMenu GeneralMenu()
    {
        var menu=ThemedMenus.Create(ContextPreferences(),ContextPreferences);
        void Add(string label,Action action){var item=new MenuItem{Header=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Add("Open Start",()=>Toggle(AppBarSection.Start));Add("Power and battery",()=>Toggle(AppBarSection.Power));Add("AppBar settings",()=>host.OpenCategory("AppBar"));Add("Appearance and customisation",()=>host.OpenCategory("Appearance"));Add("Plugins…",()=>host.Plugins.OpenConfiguration(this,Prefs));Add("Custom Icons…",()=>CustomIcons.Open(this,Prefs));Add("Advanced settings",()=>{Prefs.AdvancedSettings=true;Prefs.Save();host.OpenCategory("Advanced");});return menu;
    }
    ContextMenu ComponentMenu(string name)
    {
        var menu=GeneralMenu();if(name=="Start")CustomIcons.AddMenu(menu,this,Prefs,"component:start","appbar");return menu;
    }
    internal void RefreshEnhancements()
    {
        if(closing)return;header.Height=BarHeight;header.Margin=new Thickness(Prefs.AppBarPadding,0,Prefs.AppBarPadding,0);
        if(current==AppBarSection.None&&Math.Abs(Height-BarHeight)>.1)AnimateHeight(BarHeight,false);
        foreach(var button in buttons.Values)button.Height=BarHeight;
        if(buttons.TryGetValue(AppBarSection.Start,out var start))
        {
            start.Visibility=Prefs.ShowAppBarStart?Visibility.Visible:Visibility.Collapsed;
            var custom=CustomIcons.Cached("component:start","appbar");
            if(custom!=null)start.Content=new Image{Source=custom,Width=18,Height=18};else if(start.Content is not Ellipse){var circle=new Ellipse{Width=16,Height=16,StrokeThickness=1.5};circle.SetBinding(Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground"){Source=start});start.Content=circle;}
        }
        if(trayButton!=null)trayButton.Visibility=Prefs.AppBarTaskTray?Visibility.Visible:Visibility.Collapsed;
        RefreshMenuCategories();
        string next=string.Join("|",host.Plugins.ComponentsFor("AppBar").Select(component=>component.PluginId));
        if(next!=pluginSignature)
        {
            pluginSignature=next;pluginButtons.Children.Clear();
            foreach(var component in host.Plugins.ComponentsFor("AppBar")){string id=component.PluginId;var button=host.Plugins.BuildCompactButton(id,()=>{selectedPlugin=id;Toggle(AppBarSection.Plugin);});button.ContextMenu=GeneralMenu();pluginButtons.Children.Add(button);}
        }
    }
    void RefreshMenuCategories()
    {
        if(closing)return;categoryButtons.Visibility=Prefs.AppBarApplicationMenu?Visibility.Visible:Visibility.Collapsed;
        var context=host.Applications.Current;string next=$"{context?.Handle}:{context?.ProcessId}:{context?.Started}:"+string.Join("|",host.Capabilities.MenuCategories.Select(item=>item.Label));if(menuSignature==next)return;menuSignature=next;categoryButtons.Children.Clear();
        foreach(var category in host.Capabilities.MenuCategories.Take(5))
        {
            string label=category.Label;var button=Compact(label,label+" application menu",async()=>{var now=host.Applications.Current;if(now?.Handle!=context?.Handle||now?.ProcessId!=context?.ProcessId||now?.Started!=context?.Started)return;var selected=host.Capabilities.MenuCategories.FirstOrDefault(item=>item.Label==label&&item.CategoryName==category.CategoryName);if(selected==null)return;Expand(AppBarSection.Application);await host.Capabilities.OpenMenu(selected);if(!closing&&current==AppBarSection.Application)BuildBody();});button.ContextMenu=GeneralMenu();categoryButtons.Children.Add(button);
        }
    }
    void BuildApplicationMenuContents()
    {
        if(!Prefs.AppBarApplicationMenu)return;
        var categories=new WrapPanel();body.Children.Add(categories);
        foreach(var category in host.Capabilities.MenuCategories){string label=category.Label;Action(label,async()=>{var selected=host.Capabilities.MenuCategories.FirstOrDefault(item=>item.Label==label);if(selected!=null)await host.Capabilities.OpenMenu(selected);},categories);}
        if(string.IsNullOrEmpty(host.Capabilities.SelectedMenuCategory))return;
        Text(host.Capabilities.SelectedMenuCategory,14);
        foreach(var action in host.Capabilities.MenuActions){var selected=action;Action(action.Label,async()=>{await host.Capabilities.Execute(selected);if(current!=AppBarSection.Application)return;if(selected.Method!="Expand")Collapse();else BuildBody();});}
    }
    void FitHeader()
    {
        double half=Math.Max(60,ActualWidth/2-Prefs.AppBarPadding-(Prefs.AppBarClock?clock.ActualWidth/2+12:0));
        if(leftHeader!=null)leftHeader.MaxWidth=half;if(rightHeader!=null)rightHeader.MaxWidth=half;
        application.MaxWidth=Math.Max(24,Math.Min(180,half*.42));categoryButtons.MaxWidth=Math.Max(0,half-application.MaxWidth-64);categoryButtons.ClipToBounds=true;
    }
    void BuildMediaContents()
    {
        var media=host.Media;Text(media.Title,14);if(media.Artist.Length>0)Text(media.Artist,12);
        if(media.Selected!=null)Text(media.Source,11);
        if(media.Artwork!=null)body.Children.Add(new Image{Source=media.Artwork,Width=64,Height=64,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,6)});
        var controls=media.Playback?.Controls;var transport=new StackPanel{Orientation=Orientation.Horizontal};body.Children.Add(transport);
        Action("Previous",()=>_=media.Command("previous"),transport,controls?.IsPreviousEnabled==true);
        Action(media.Playback?.PlaybackStatus==Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing?"Pause":"Play",()=>_=media.Command("toggle"),transport,controls?.IsPlayPauseToggleEnabled==true);
        Action("Next",()=>_=media.Command("next"),transport,controls?.IsNextEnabled==true);
        var timeline=media.Timeline;
        if(timeline!=null&&timeline.EndTime>timeline.StartTime)
        {
            var progress=new Slider{Minimum=timeline.StartTime.TotalSeconds,Maximum=timeline.EndTime.TotalSeconds,Value=Math.Clamp(timeline.Position.TotalSeconds,timeline.StartTime.TotalSeconds,timeline.EndTime.TotalSeconds),IsEnabled=media.CanSeek,ToolTip="Playback position"};body.Children.Add(progress);
            progress.PreviewMouseLeftButtonUp+=(_,_)=>_=media.Seek(TimeSpan.FromSeconds(progress.Value));
            progress.PreviewKeyUp+=(_,args)=>{if(args.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or System.Windows.Input.Key.Home or System.Windows.Input.Key.End or System.Windows.Input.Key.PageUp or System.Windows.Input.Key.PageDown)_=media.Seek(TimeSpan.FromSeconds(progress.Value));};
            Text(timeline.Position.ToString(@"h\:mm\:ss")+" / "+timeline.EndTime.ToString(@"h\:mm\:ss"),11);
        }
        if(media.Sessions.Count>1)foreach(var session in media.Sessions){var selected=session;Action((selected==media.Selected?"✓ ":"")+AppBarMedia.SourceName(session),()=>media.Select(selected));}
        if(media.Error!=null)Text(media.Error,12);
    }
}
