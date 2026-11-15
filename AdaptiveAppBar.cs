using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace NewStart;
internal enum AppBarSection { None,Media,Audio,Network,Bluetooth,Clock,Application,Settings,Start }
internal sealed class AdaptiveAppBars:IDisposable
{
    readonly DockWindow dock;internal readonly Preferences Preferences;
    readonly Dictionary<string,AdaptiveAppBarWindow> bars=new();bool disposed,queued;
    internal readonly ActiveApplication Applications;
    internal readonly BorderlessManager Borderless;
    internal readonly AppBarAudio Audio;
    internal readonly AppBarMedia Media;
    internal readonly AppBarNetwork Network;
    internal readonly AppBarBluetooth Bluetooth;
    internal readonly ApplicationCapabilities Capabilities;
    internal IEnumerable<AdaptiveAppBarWindow> Windows=>bars.Values;
    internal AdaptiveAppBars(DockWindow host,Preferences prefs)
    {
        dock=host;Preferences=prefs;Applications=new(host.Dispatcher);Borderless=new(prefs,Applications);Audio=new(host.Dispatcher);Media=new(host.Dispatcher);Network=new(host.Dispatcher);Bluetooth=new(host.Dispatcher);Capabilities=new(Applications,Media,host.Dispatcher);
        SystemEvents.DisplaySettingsChanged+=DisplaysChanged;SystemEvents.UserPreferenceChanged+=UserPreferenceChanged;
        try{Reconcile();Borderless.Update();}catch{Dispose();throw;}
    }
    void DisplaysChanged(object? sender,EventArgs args)=>QueueDisplays();
    void UserPreferenceChanged(object sender,UserPreferenceChangedEventArgs args)=>QueueDisplays();
    internal void QueueDisplays()
    {
        if(disposed||queued)return;queued=true;dock.Dispatcher.BeginInvoke(new Action(()=>{queued=false;if(!disposed)Reconcile();}));
    }
    void Reconcile()
    {
        var displays=DesktopDisplays.All().Where(display=>Preferences.AppBarAllMonitors||display.Primary).ToArray();
        foreach(string name in bars.Keys.Except(displays.Select(display=>display.Name)).ToArray()){bars[name].Close();bars.Remove(name);}
        foreach(var display in displays)
        {
            if(!bars.TryGetValue(display.Name,out var bar)){bar=new(this,display);bars.Add(display.Name,bar);bar.Show();}
            else bar.Display=display;
            bar.Reposition();bar.UpdateAppearance();
        }
        dock.AppBarDisplaysChanged();
    }
    internal void Refresh(bool displays=false){if(disposed)return;if(displays)Reconcile();Borderless.Update();foreach(var bar in bars.Values)bar.RefreshContent();}
    internal void CollapseOthers(AdaptiveAppBarWindow selected){foreach(var bar in bars.Values)if(bar!=selected)bar.Collapse(false);}
    internal void OpenStart(AdaptiveAppBarWindow bar,FrameworkElement button)
    {
        bar.Toggle(AppBarSection.Start);
    }
    internal bool OpenSettings(){var monitor=DesktopDisplays.MonitorFromWindow(Native.GetForegroundWindow(),2);var bar=bars.Values.FirstOrDefault(bar=>bar.Display.Handle==monitor)??bars.Values.FirstOrDefault();if(bar==null)return false;bar.Toggle(AppBarSection.Settings);return true;}
    internal FrameworkElement SettingsContents(Window owner,Action dismiss,out Action release)=>dock.SettingsContents(owner,dismiss,true,out release);
    internal FrameworkElement StartContents(double width,double height,Action dismiss,out Action release)=>dock.EmbeddedStartContents(width,height,dismiss,out release);
    internal bool FocusStart()=>dock.FocusEmbeddedStart();
    public void Dispose()
    {
        if(disposed)return;disposed=true;SystemEvents.DisplaySettingsChanged-=DisplaysChanged;SystemEvents.UserPreferenceChanged-=UserPreferenceChanged;
        foreach(var bar in bars.Values.ToArray())bar.Close();bars.Clear();Capabilities.Dispose();Borderless.Dispose();Applications.Dispose();Audio.Dispose();Media.Dispose();Network.Dispose();Bluetooth.Dispose();
    }
}

internal sealed class AdaptiveAppBarWindow:Window
{
    internal const double RestHeight=36;
    readonly AdaptiveAppBars host;Preferences Prefs=>host.Preferences;
    internal DesktopDisplay Display;
    readonly Grid root=new(),header=new();readonly StackPanel body=new(){Margin=new Thickness(16,12,16,14)};
    readonly ScrollViewer bodyScroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    readonly Border bodyHit=new(){Background=Brushes.Transparent,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,ClipToBounds=true};
    readonly AppBarMaterial material=new();
    readonly Preferences surfacePrefs=new(){DockBackground=true};
    readonly TextBlock application=new(){Text="Desktop",MaxWidth=260,TextTrimming=TextTrimming.CharacterEllipsis};
    readonly Image applicationIcon=new(){Width=16,Height=16,Margin=new Thickness(0,0,7,0)};
    readonly DispatcherTimer clockTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    readonly DispatcherTimer leaveTimer=new(){Interval=TimeSpan.FromMilliseconds(450)};
    readonly DispatcherTimer hoverTimer=new(){Interval=TimeSpan.FromMilliseconds(300)};
    readonly Dictionary<AppBarSection,Button> buttons=new();
    readonly StackPanel windowControls=new(){Orientation=Orientation.Horizontal};
    readonly StackPanel battery=new(){Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,8,0),Visibility=Visibility.Collapsed};
    readonly Rectangle batteryFill=new(){Height=8,RadiusX=1,RadiusY=1};
    readonly TextBlock batteryPercent=new(){FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6,0,0,0)};
    readonly System.Windows.Shapes.Path chargingMark=new(){Data=Geometry.Parse("M 14,1 L 9,8 L 13,8 L 9,15"),StrokeThickness=1.7};
    readonly List<IntPtr> powerNotifications=new();
    Button clock=null!,media=null!,audio=null!,network=null!,bluetooth=null!;
    DockReservation? reservation;HwndSource? source;AppBarSection current;double expandedWidth=420;bool closing,repositioning;int appIconVersion;
    (IntPtr Window,uint Process,long Started,string Target)? iconIdentity;
    bool? dark;bool highContrast;
    int animationGeneration;
    double targetExtra=1;
    static readonly DependencyProperty LobeCentreProperty=DependencyProperty.Register("LobeCentre",typeof(double),typeof(AdaptiveAppBarWindow),new PropertyMetadata(0d,(owner,_)=>((AdaptiveAppBarWindow)owner).UpdateSurface()));
    static readonly DependencyProperty LobeWidthProperty=DependencyProperty.Register("LobeWidth",typeof(double),typeof(AdaptiveAppBarWindow),new PropertyMetadata(44d,(owner,_)=>((AdaptiveAppBarWindow)owner).UpdateSurface()));
    Slider? volumeSlider;Button? muteButton;TextBlock? audioStatus;bool syncingVolume;
    TextBlock? networkStatus;
    DateTime calendarMonth=new(DateTime.Today.Year,DateTime.Today.Month,1),calendarSelection=DateTime.Today;
    bool hoverExpansion,focusStartPending,settingsFitQueued,showAllActions;Action? releaseContent;
    internal AppBarSection Section=>current;
    internal bool Testing {get;set;}
    internal double ReservedHeight=>RestHeight;
    internal long SurfaceUpdates {get;private set;}
    internal long Repositions {get;private set;}
    internal bool BlurVisible=>material.NativeVisible;
    internal double MaterialOpacity=>material.TintOpacity;
    internal Color? MaterialColour=>material.TintColour;
    internal bool HasContinuousMaterial
    {
        get
        {
            if(material.Surface.Data is not Geometry outline||outline.GetFlattenedPathGeometry().Figures.Count!=1)return false;
            if(!material.NativeVisible)return true;
            double y=RestHeight+(ActualHeight-RestHeight)/2;
            for(int i=0;i<24;i++){var point=new Point(ActualWidth*(i+.5)/24,y);if(outline.FillContains(point)!=material.NativeContains(point))return false;}
            return material.NativeContains(new Point(ActualWidth/2,RestHeight/2));
        }
    }
    internal bool ClockIsCentered=>!Prefs.AppBarClock||Math.Abs(clock.TranslatePoint(new Point(clock.ActualWidth/2,0),this).X-ActualWidth/2)<1;
    internal DockReservation.Rect ReservedBounds=>reservation?.Bounds??default;
    internal AdaptiveAppBarWindow(AdaptiveAppBars owner,DesktopDisplay display)
    {
        host=owner;Display=display;Title="New Start Adaptive AppBar (Beta)";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;Height=RestHeight;Width=600;
        Content=root;root.Children.Add(material.Surface);root.Children.Add(bodyHit);root.Children.Add(header);
        header.Height=RestHeight;header.VerticalAlignment=VerticalAlignment.Top;header.Background=new SolidColorBrush(Color.FromArgb(1,0,0,0));header.Margin=new Thickness(8,0,8,0);
        header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var left=new StackPanel{Orientation=Orientation.Horizontal};header.Children.Add(left);
        var start=Compact(new Ellipse{Width=16,Height=16,StrokeThickness=1.5},"Start",()=>host.OpenStart(this,(FrameworkElement)left.Children[0]));
        buttons[AppBarSection.Start]=start;
        ((Ellipse)start.Content).SetBinding(Shape.StrokeProperty,new System.Windows.Data.Binding("Foreground"){Source=start});left.Children.Add(start);
        var titlePanel=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};titlePanel.Children.Add(applicationIcon);titlePanel.Children.Add(application);
        var appButton=Compact(titlePanel,"Active application",()=>Toggle(AppBarSection.Application));left.Children.Add(appButton);buttons[AppBarSection.Application]=appButton;
        var right=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetColumn(right,2);header.Children.Add(right);
        media=SectionButton(right,AppBarSection.Media,"\uE8D6","Media");
        media.MouseEnter+=(_,_)=>{if(Prefs.AppBarMediaHover&&current==AppBarSection.None)hoverTimer.Start();};media.MouseLeave+=(_,_)=>hoverTimer.Stop();
        audio=SectionButton(right,AppBarSection.Audio,"\uE767","Volume");network=SectionButton(right,AppBarSection.Network,"\uE701","Wi-Fi");bluetooth=SectionButton(right,AppBarSection.Bluetooth,"\uE702","Bluetooth");
        var batteryDrawing=new Canvas{Width=26,Height=16};var batteryOutline=new Rectangle{Width=21,Height=14,RadiusX=2,RadiusY=2,StrokeThickness=1.3};batteryOutline.SetResourceReference(Shape.StrokeProperty,"Text");Canvas.SetTop(batteryOutline,1);batteryDrawing.Children.Add(batteryOutline);
        var terminal=new Rectangle{Width=2,Height=6};terminal.SetResourceReference(Shape.FillProperty,"Text");Canvas.SetLeft(terminal,22);Canvas.SetTop(terminal,5);batteryDrawing.Children.Add(terminal);
        batteryFill.SetResourceReference(Shape.FillProperty,"Text");Canvas.SetLeft(batteryFill,3);Canvas.SetTop(batteryFill,4);batteryDrawing.Children.Add(batteryFill);chargingMark.SetResourceReference(Shape.StrokeProperty,"Accent");batteryDrawing.Children.Add(chargingMark);battery.Children.Add(batteryDrawing);battery.Children.Add(batteryPercent);right.Children.Add(battery);
        clock=Compact("", "Clock",()=>Toggle(AppBarSection.Clock));clock.HorizontalAlignment=HorizontalAlignment.Center;Grid.SetColumnSpan(clock,3);header.Children.Add(clock);buttons[AppBarSection.Clock]=clock;
        var settings=Compact("\uE713","New Start settings",()=>Toggle(AppBarSection.Settings),true);right.Children.Add(settings);buttons[AppBarSection.Settings]=settings;right.Children.Add(windowControls);
        bodyHit.Background=new SolidColorBrush(Color.FromArgb(1,0,0,0));bodyScroll.Content=body;bodyHit.Child=bodyScroll;bodyHit.Visibility=Visibility.Collapsed;
        MouseLeave+=(_,_)=>{if(hoverExpansion)leaveTimer.Start();};MouseEnter+=(_,_)=>leaveTimer.Stop();
        bodyHit.MouseLeave+=(_,_)=>{if(hoverExpansion)leaveTimer.Start();};bodyHit.MouseEnter+=(_,_)=>leaveTimer.Stop();
        leaveTimer.Tick+=(_,_)=>{leaveTimer.Stop();if(hoverExpansion&&!IsMouseOver){if(Mouse.LeftButton==MouseButtonState.Pressed)leaveTimer.Start();else Collapse();}};
        hoverTimer.Tick+=(_,_)=>{hoverTimer.Stop();if(media.IsMouseOver&&current==AppBarSection.None)Expand(AppBarSection.Media,true);};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Collapse();e.Handled=true;}};
        Deactivated+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>{
            if(closing||Testing||current==AppBarSection.None)return;
            var foreground=Native.GetForegroundWindow();var own=new WindowInteropHelper(this).Handle;
            if(foreground!=IntPtr.Zero&&foreground!=own&&!IsOwnedBy(foreground,own))Collapse();
        }),DispatcherPriority.Input);
        host.Applications.Changed+=ApplicationChanged;host.Audio.Changed+=AudioChanged;host.Media.Changed+=MediaChanged;host.Network.Changed+=NetworkChanged;host.Bluetooth.Changed+=BluetoothChanged;
        host.Capabilities.Changed+=CapabilitiesChanged;
        Loaded+=(_,_)=>{
            source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);source?.AddHook(Hook);
            foreach(var setting in AppBarBattery.Notifications){var id=setting;var token=AppBarBattery.RegisterPowerSettingNotification(source!.Handle,ref id,0);if(token!=IntPtr.Zero)powerNotifications.Add(token);}UpdateBattery();
            reservation=new DockReservation(this,()=>Display.Handle);reservation.Changed+=Reposition;Reposition();RefreshContent();clockTimer.Start();
        };
        SizeChanged+=(_,_)=>UpdateSurface();clockTimer.Tick+=(_,_)=>UpdateClock();
        Closed+=(_,_)=>{
            closing=true;ClearBody();clockTimer.Stop();leaveTimer.Stop();hoverTimer.Stop();source?.RemoveHook(Hook);reservation?.Dispose();material.Dispose();
            foreach(var token in powerNotifications)AppBarBattery.UnregisterPowerSettingNotification(token);powerNotifications.Clear();
            host.Applications.Changed-=ApplicationChanged;host.Audio.Changed-=AudioChanged;host.Media.Changed-=MediaChanged;host.Network.Changed-=NetworkChanged;host.Bluetooth.Changed-=BluetoothChanged;
            host.Capabilities.Changed-=CapabilitiesChanged;
        };
    }
    [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
    static bool IsOwnedBy(IntPtr window,IntPtr owner){for(int i=0;i<16&&window!=IntPtr.Zero;i++){window=GetWindow(window,4);if(window==owner)return true;}return false;}
    IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==0x007E||message==0x02E0){Collapse(false);host.QueueDisplays();}
        if(message==0x0218)UpdateBattery();
        return IntPtr.Zero;
    }
    Button Compact(object content,string name,Action action,bool glyph=false)
    {
        var button=new Button{Content=glyph?new TextBlock{Text=content.ToString(),FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=15}:content,Height=RestHeight,MinWidth=34,Padding=new Thickness(8,0,8,0),ToolTip=name,VerticalAlignment=VerticalAlignment.Top};
        StartMenuWindow.Borderless(button);System.Windows.Automation.AutomationProperties.SetName(button,name);button.Click+=(_,_)=>action();return button;
    }
    Button SectionButton(Panel parent,AppBarSection section,string glyph,string name)
    {var button=Compact(glyph,name,()=>Toggle(section),true);buttons[section]=button;parent.Children.Add(button);return button;}
    internal void Reposition()
    {
        Repositions++;
        if(closing||repositioning||!IsLoaded)return;repositioning=true;
        try
        {
            var hwnd=new WindowInteropHelper(this).Handle;
            // Move to the display before measuring its actual per-window DPI.
            DesktopDisplays.SetWindowPos(hwnd,IntPtr.Zero,Display.Bounds.Left,Display.Bounds.Top,0,0,0x0015);
            double scale=Math.Max(1,DesktopDisplays.GetDpiForWindow(hwnd))/96d;
            reservation?.Update(true,RestHeight,DockEdge.Top);var bounds=reservation?.Bounds??default;
            if(bounds.Right<=bounds.Left){bounds=new(){Left=Display.Bounds.Left,Top=Display.Bounds.Top,Right=Display.Bounds.Right,Bottom=Display.Bounds.Top+(int)Math.Ceiling(RestHeight*scale)};}
            Width=(bounds.Right-bounds.Left)/scale;
            DesktopDisplays.SetWindowPos(hwnd,IntPtr.Zero,bounds.Left,bounds.Top,bounds.Right-bounds.Left,(int)Math.Ceiling(Height*scale),0x0014);
            expandedWidth=Math.Min(440,Math.Max(100,Width-16));UpdateSurface();
        }
        finally{repositioning=false;}
    }
    internal void UpdateAppearance()
    {
        if(closing)return;bool next=SurfaceAppearance.WhiteInk(SurfaceAppearance.Effective(Prefs,true,Display.Name));
        if(dark!=next||highContrast!=SystemParameters.HighContrast){dark=next;highContrast=SystemParameters.HighContrast;Appearance.Apply(this,next);Background=Brushes.Transparent;}
        Foreground=SurfaceAppearance.Ink(Prefs,true,Display.Name);Resources["Text"]=Foreground;
        surfacePrefs.Theme=Prefs.Theme;surfacePrefs.MenuBlur=Prefs.AppBarBlur??Prefs.MenuBlur;surfacePrefs.StartTransparency=Prefs.AppBarTransparency??Prefs.StartTransparency;
        surfacePrefs.SurfaceColour=SurfaceAppearance.Background(Prefs,true,Display.Name);
        var hit=surfacePrefs.SurfaceColour.Value;header.Background=new SolidColorBrush(Color.FromArgb(1,hit.R,hit.G,hit.B));bodyHit.Background=header.Background;
        UpdateSurface();
    }
    void UpdateSurface()
    {
        SurfaceUpdates++;
        if(!IsLoaded||closing)return;
        application.MaxWidth=Math.Clamp(ActualWidth-540,0,260);
        windowControls.Visibility=ActualWidth<720?Visibility.Collapsed:Visibility.Visible;
        media.Visibility=ActualWidth<500?Visibility.Collapsed:Visibility.Visible;
        double extra=Math.Max(0,ActualHeight-RestHeight);
        double width=Math.Max(1,ActualWidth),lobeWidth=Math.Min(expandedWidth,(double)GetValue(LobeWidthProperty));
        double left=Math.Clamp((double)GetValue(LobeCentreProperty)-lobeWidth/2,8,Math.Max(8,width-lobeWidth-8));
        double shoulder=Math.Max(0,Math.Min(16,Math.Min(extra/2,Math.Min(left,width-left-lobeWidth))));
        double radius=Math.Min(16,Math.Min(extra/2,lobeWidth/2));
        material.Update(this,surfacePrefs,new AppBarOutline(width,RestHeight,left,lobeWidth,extra,shoulder,radius));
        if(extra<1){bodyHit.Visibility=Visibility.Collapsed;bodyScroll.Opacity=0;return;}
        bodyHit.Visibility=Visibility.Visible;bodyHit.Width=lobeWidth;bodyHit.Height=extra;bodyHit.Margin=new Thickness(left,RestHeight,0,0);
        bodyScroll.Width=expandedWidth;bodyScroll.HorizontalAlignment=HorizontalAlignment.Center;
        bodyScroll.Opacity=Math.Clamp((extra/Math.Max(1,targetExtra)-.3)/.7,0,1);
        if(focusStartPending&&current==AppBarSection.Start&&extra>24)focusStartPending=!host.FocusStart();
    }
    void Morph(DependencyProperty property,double target,bool animate)
    {
        double from=(double)GetValue(property);BeginAnimation(property,null);SetValue(property,target);
        if(animate&&Prefs.AppBarAnimations&&SystemParameters.ClientAreaAnimation&&Prefs.AppBarAnimationMilliseconds>0)
            BeginAnimation(property,new DoubleAnimation(from,target,TimeSpan.FromMilliseconds(Prefs.AppBarAnimationMilliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop},HandoffBehavior.SnapshotAndReplace);
    }
    void AnimateHeight(double height,bool animate=true)
    {
        if(height>RestHeight)targetExtra=height-RestHeight;
        int version=++animationGeneration;
        double from=ActualHeight;BeginAnimation(HeightProperty,null);Height=height;
        if(animate&&Prefs.AppBarAnimations&&SystemParameters.ClientAreaAnimation&&Prefs.AppBarAnimationMilliseconds>0)
        {
            var animation=new DoubleAnimation(from,height,TimeSpan.FromMilliseconds(Prefs.AppBarAnimationMilliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
            animation.Completed+=(_,_)=>{if(closing||version!=animationGeneration)return;BeginAnimation(HeightProperty,null);Height=height;UpdateSurface();if(current==AppBarSection.None)ClearBody();};
            BeginAnimation(HeightProperty,animation,HandoffBehavior.SnapshotAndReplace);
        }
    }
    internal void Toggle(AppBarSection section){Activate();if(current==section){if(hoverExpansion){hoverExpansion=false;leaveTimer.Stop();}else Collapse();}else Expand(section);}
    internal void Expand(AppBarSection section,bool fromHover=false)
    {
        bool opening=current==AppBarSection.None&&ActualHeight<=RestHeight+1;
        host.CollapseOthers(this);hoverTimer.Stop();leaveTimer.Stop();current=section;hoverExpansion=fromHover;
        expandedWidth=Math.Min(section==AppBarSection.Start?680:section==AppBarSection.Settings?580:440,Math.Max(100,ActualWidth-16));
        double anchor=ActualWidth/2;
        if(buttons.TryGetValue(section,out var button))anchor=button.TranslatePoint(new Point(button.ActualWidth/2,0),this).X;
        Morph(LobeCentreProperty,anchor,!opening);
        BuildBody();Morph(LobeWidthProperty,expandedWidth,true);FitExpansion();UpdateSurface();
        if(section==AppBarSection.Network)_=host.Network.Refresh(true);
    }
    void FitExpansion()
    {
        if(current==AppBarSection.None)return;
        double limit=current switch{AppBarSection.Start=>740,AppBarSection.Settings=>620,AppBarSection.Media=>220,AppBarSection.Audio=>300,AppBarSection.Application=>380,_=>300};
        body.Measure(new Size(expandedWidth,double.PositiveInfinity));double target=Math.Clamp(Math.Ceiling(body.DesiredSize.Height)+4,44,limit);
        double scale=VisualTreeHelper.GetDpi(this).DpiScaleY;double maximum=(Display.Bounds.Bottom-Display.Bounds.Top)/scale-RestHeight-24;
        double height=RestHeight+Math.Max(40,Math.Min(target,maximum));if(Math.Abs(Height-height)>.5)AnimateHeight(height);
    }
    internal void RefitSettings()
    {
        if(closing||current!=AppBarSection.Settings||settingsFitQueued)return;settingsFitQueued=true;
        Dispatcher.BeginInvoke(new Action(()=>{settingsFitQueued=false;if(!closing&&current==AppBarSection.Settings){body.InvalidateMeasure();FitExpansion();}}),DispatcherPriority.ContextIdle);
    }
    internal void Collapse(bool animate=true){if(current==AppBarSection.None&&Height==RestHeight)return;current=AppBarSection.None;hoverExpansion=false;focusStartPending=false;hoverTimer.Stop();leaveTimer.Stop();Morph(LobeWidthProperty,44,animate);AnimateHeight(RestHeight,animate);if(!animate||!Prefs.AppBarAnimations||!SystemParameters.ClientAreaAnimation||Prefs.AppBarAnimationMilliseconds==0)ClearBody();}
    void ClearBody(){body.Children.Clear();var release=releaseContent;releaseContent=null;release?.Invoke();}
    void Text(string value,double size=13){body.Children.Add(new TextBlock{Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});}
    Button Action(string label,Action action,Panel? parent=null,bool enabled=true){var button=new Button{Content=label,IsEnabled=enabled,Margin=new Thickness(0,0,6,6),Padding=new Thickness(10,6,10,6)};button.Click+=(_,_)=>action();(parent??body).Children.Add(button);return button;}
    internal static void Open(string uri){try{Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});}catch(Exception e){MessageBox.Show(e.Message,"New Start");}}
    void BuildBody()
    {
        ClearBody();volumeSlider=null;muteButton=null;audioStatus=null;networkStatus=null;
        switch(current)
        {
            case AppBarSection.Settings:
                body.Children.Add(host.SettingsContents(this,()=>Collapse(),out releaseContent));break;
            case AppBarSection.Start:
                double available=(Display.Bounds.Bottom-Display.Bounds.Top)/VisualTreeHelper.GetDpi(this).DpiScaleY-RestHeight-64;
                body.Children.Add(host.StartContents(expandedWidth-32,Math.Min(660,Math.Max(180,available)),()=>Collapse(),out releaseContent));focusStartPending=true;break;
            case AppBarSection.Media:
                var media=host.Media;
                var row=new StackPanel{Orientation=Orientation.Horizontal};body.Children.Add(row);
                if(media.Artwork!=null)row.Children.Add(new Image{Source=media.Artwork,Width=56,Height=56,Margin=new Thickness(0,0,12,8)});
                else row.Children.Add(new TextBlock{Text="\uE8D6",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=32,Width=56,Margin=new Thickness(0,0,12,8)});
                row.Children.Add(new TextBlock{Text=media.Title+"\n"+media.Artist,TextWrapping=TextWrapping.Wrap,MaxWidth=Math.Max(80,expandedWidth-120),VerticalAlignment=VerticalAlignment.Center});
                var transport=new StackPanel{Orientation=Orientation.Horizontal};body.Children.Add(transport);var controls=media.Playback?.Controls;
                Action("Previous",()=>_=media.Command("previous"),transport,controls?.IsPreviousEnabled==true);
                Action(media.Playback?.PlaybackStatus==Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing?"Pause":"Play",()=>_=media.Command("toggle"),transport,controls?.IsPlayPauseToggleEnabled==true);
                Action("Next",()=>_=media.Command("next"),transport,controls?.IsNextEnabled==true);
                if(media.Sessions.Count>1)foreach(var session in media.Sessions){var source=session;string name=StartCatalog.Current?.Apps.FirstOrDefault(app=>app.Target=="shell:AppsFolder\\"+source.SourceAppUserModelId)?.Name??source.SourceAppUserModelId;Action((source==media.Selected?"✓  ":"")+name,()=>media.Select(source));}
                if(media.Error!=null)Text(media.Error,12);break;
            case AppBarSection.Audio:
                var audio=host.Audio;Text(audio.Output,14);if(audio.Error!=null)Text(audio.Error,12);
                var volume=new Slider{Minimum=0,Maximum=100,Value=audio.Level,TickFrequency=1,IsSnapToTickEnabled=true,Margin=new Thickness(0,4,0,8),ToolTip="Master volume"};System.Windows.Automation.AutomationProperties.SetName(volume,"Master volume");volumeSlider=volume;
                volume.ValueChanged+=(_,_)=>{if(!syncingVolume)audio.Set(Math.Round(volume.Value),audio.Muted);};body.Children.Add(volume);
                muteButton=Action(audio.Muted?"Unmute":"Mute",()=>audio.Set(audio.Level,!audio.Muted),enabled:audio.Error==null);
                foreach(var output in audio.Outputs){var device=output;Action((output.Name==audio.Output?"✓  ":"")+output.Name,()=>audio.SelectOutput(device));}
                audioStatus=new TextBlock{Text=audio.Error??"Choose an audio output.",TextWrapping=TextWrapping.Wrap,FontSize=12};body.Children.Add(audioStatus);
                Action("More sound options",()=>Open("ms-settings:sound"));break;
            case AppBarSection.Network:
                networkStatus=new TextBlock{Text=host.Network.Status,FontSize=14,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};body.Children.Add(networkStatus);var networkActions=new StackPanel{Orientation=Orientation.Horizontal};body.Children.Add(networkActions);
                Action("Refresh",()=>_=host.Network.Refresh(true),networkActions);Action(host.Network.AccessDenied?"Location permission":"Network configuration",()=>Open(host.Network.AccessDenied?"ms-settings:privacy-location":"ms-settings:network-wifi"),networkActions);
                foreach(var item in host.Network.Networks)
                {
                    var network=item;var line=new StackPanel{Margin=new Thickness(0,4,0,5)};body.Children.Add(line);
                    line.Children.Add(new TextBlock{Text=network.Name+" · "+network.Quality+"%"+(network.Connected?" · Connected":""),TextTrimming=TextTrimming.CharacterEllipsis});
                    if(network.Connected)Action("Disconnect",()=>_=host.Network.Connect(network,true),line);
                    else if(network.Profile.Length>0)Action("Connect",()=>_=host.Network.Connect(network,false),line,network.Connectable);
                    else if(!network.Secured||network.Authentication==7)Action("Connect…",()=>{
                        var password=new PasswordBox{Margin=new Thickness(0,3,0,6),MaxLength=63};
                        System.Windows.Automation.AutomationProperties.SetName(password,"Wi-Fi password for "+network.Name);
                        if(network.Secured){line.Children.Add(new TextBlock{Text="Network password"});line.Children.Add(password);password.Focus();}
                        Action("Save profile and connect",()=>{string secret=password.Password;password.Clear();_=host.Network.Provision(network,secret);},line);
                        FitExpansion();
                    },line);
                    else Action("Configure enterprise/WPA3 network",()=>Open("ms-settings:network-wifi"),line);
                }break;
            case AppBarSection.Bluetooth:
                Text(host.Bluetooth.Status,14);Action(host.Bluetooth.Enabled?"Turn Bluetooth off":"Turn Bluetooth on",()=>_=host.Bluetooth.Toggle(),enabled:host.Bluetooth.CanToggle);
                if(host.Bluetooth.Devices.Count==0)Text("No paired devices reported by Windows.",12);
                foreach(var device in host.Bluetooth.Devices)Text(device.Name+" · "+(device.Connected is true?"Connected":device.Connected is false?"Not connected":"Paired; connection state unavailable"),12);
                Text("Connections depend on the device's Bluetooth profile. Windows manages devices without a supported connection adapter.",12);Action("Device connection options",()=>Open("ms-settings:bluetooth"));break;
            case AppBarSection.Clock:
                Text(calendarSelection.ToString("D",CultureInfo.CurrentCulture),14);BuildCalendar();break;
            case AppBarSection.Application:
                var app=host.Applications.Current;Text(app?.Name??"Desktop",14);Text(app?.Title??"",12);
                var contextual=new WrapPanel();body.Children.Add(contextual);
                foreach(var capability in host.Capabilities.Actions.OrderBy(action=>action.Method=="Window"?1:0).Take(showAllActions?32:8)){var selected=capability;Action(capability.Label,()=>_=host.Capabilities.Execute(selected),contextual);}
                if(host.Capabilities.Actions.Count>8)Action(showAllActions?"Fewer actions":"More actions",()=>{showAllActions=!showAllActions;BuildBody();});
                Text(host.Capabilities.Status,12);Action("Refresh available actions",()=>host.Capabilities.Refresh());Text(host.Borderless.Feedback,12);
                if(app!=null)Action("Exclude this app from borderless management",()=>{if(!Prefs.BorderlessExclusions.Contains(app.Path,StringComparer.OrdinalIgnoreCase))Prefs.BorderlessExclusions.Add(app.Path);Prefs.Save();host.Borderless.Update();BuildBody();});break;
        }
        if(IsLoaded)FitExpansion();
    }
    void BuildCalendar()
    {
        var navigation=new Grid{Margin=new Thickness(0,0,0,8)};
        navigation.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(34)});navigation.ColumnDefinitions.Add(new ColumnDefinition());navigation.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(34)});
        var title=new TextBlock{Text=calendarMonth.ToString("Y",CultureInfo.CurrentCulture),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(title,1);navigation.Children.Add(title);
        foreach(int direction in new[]{-1,1})
        {
            int step=direction;var arrow=new Button{Content=step<0?"‹":"›",Height=28,Padding=new Thickness(4),ToolTip=step<0?"Previous month":"Next month"};StartMenuWindow.Borderless(arrow);
            System.Windows.Automation.AutomationProperties.SetName(arrow,step<0?"Previous month":"Next month");
            arrow.Click+=(_,_)=>{if((step<0&&calendarMonth>new DateTime(1,1,1))||(step>0&&calendarMonth<new DateTime(9999,12,1))){calendarMonth=calendarMonth.AddMonths(step);BuildBody();}};
            Grid.SetColumn(arrow,step<0?0:2);navigation.Children.Add(arrow);
        }
        body.Children.Add(navigation);
        var days=new System.Windows.Controls.Primitives.UniformGrid{Columns=7};body.Children.Add(days);
        int first=(int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        for(int column=0;column<7;column++)days.Children.Add(new TextBlock{Text=CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(first+column)%7],FontSize=11,Opacity=.65,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,0,0,5)});
        int offset=((int)calendarMonth.DayOfWeek-first+7)%7,count=DateTime.DaysInMonth(calendarMonth.Year,calendarMonth.Month);
        int cells=((offset+count+6)/7)*7;
        for(int cell=0;cell<cells;cell++)
        {
            int number=cell-offset+1;
            if(number<1||number>count){days.Children.Add(new Border());continue;}
            var date=new DateTime(calendarMonth.Year,calendarMonth.Month,number);
            var label=new TextBlock{Text=number.ToString(CultureInfo.CurrentCulture),FontSize=12,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            var face=new Border{Width=30,Height=28,CornerRadius=new CornerRadius(8),Child=label,BorderThickness=new Thickness(date==DateTime.Today?1:0)};face.SetResourceReference(Border.BorderBrushProperty,"Accent");
            if(date==calendarSelection)face.Background=new SolidColorBrush(Color.FromArgb(50,SystemParameters.WindowGlassColor.R,SystemParameters.WindowGlassColor.G,SystemParameters.WindowGlassColor.B));
            var day=new Button{Content=face,Padding=new Thickness(0),Height=30,ToolTip=date.ToString("D",CultureInfo.CurrentCulture)};StartMenuWindow.Borderless(day);System.Windows.Automation.AutomationProperties.SetName(day,date.ToString("D",CultureInfo.CurrentCulture));
            day.Click+=(_,_)=>{calendarSelection=date;BuildBody();};days.Children.Add(day);
        }
    }
    void AudioChanged()
    {
        audio.ToolTip=host.Audio.Muted?"Muted":$"Volume {host.Audio.Level:0}% · {host.Audio.Output}";
        if(current!=AppBarSection.Audio)return;
        if(volumeSlider!=null&&!volumeSlider.IsMouseCaptureWithin){syncingVolume=true;volumeSlider.Value=host.Audio.Level;syncingVolume=false;}
        if(muteButton!=null)muteButton.Content=host.Audio.Muted?"Unmute":"Mute";
        if(audioStatus!=null)audioStatus.Text=host.Audio.Error??host.Audio.Output;
        if(!body.IsMouseOver&&!body.IsKeyboardFocusWithin)BuildBody();
    }
    void MediaChanged(){media.ToolTip=host.Media.Title;if(current==AppBarSection.Media)BuildBody();}
    void NetworkChanged(){network.ToolTip=host.Network.Status;if(networkStatus!=null)networkStatus.Text=host.Network.Status;if(current==AppBarSection.Network&&!body.IsKeyboardFocusWithin)BuildBody();}
    void BluetoothChanged(){bluetooth.ToolTip=host.Bluetooth.Status;bluetooth.Opacity=host.Bluetooth.Enabled?1:.55;if(current==AppBarSection.Bluetooth)BuildBody();}
    void CapabilitiesChanged(){if(!closing&&current==AppBarSection.Application)BuildBody();}
    async void ApplicationChanged()
    {
        var target=host.Applications.Current;application.Text=target==null?"Desktop":target.Name.Length>0?target.Name:target.Title;application.ToolTip=target?.Title;windowControls.Children.Clear();
        bool sameMonitor=target!=null&&DesktopDisplays.MonitorFromWindow(target.Handle,2)==Display.Handle;
        if(sameMonitor&&target is {Fullscreen:false,Started:>0})
        {
            var saved=target;
            void Command(int command){if(host.Applications.ValidTarget(saved))Native.ShowWindowAsync(saved.Handle,command);}
            if((saved.Style&0x20000)!=0)windowControls.Children.Add(Compact("\uE921","Minimise active window",()=>Command(6),true));
            if((saved.Style&0x10000)!=0)windowControls.Children.Add(Compact(saved.Maximized?"\uE923":"\uE922",saved.Maximized?"Restore active window":"Maximise active window",()=>Command(saved.Maximized?9:3),true));
            if((saved.Style&0x80000)!=0)windowControls.Children.Add(Compact("\uE8BB","Close active window",()=>{if(host.Applications.ValidTarget(saved))Native.CloseWindow(saved.Handle);},true));
        }
        var nextIcon=target==null?((IntPtr,uint,long,string)?)null:(target.Handle,target.ProcessId,target.Started,target.IconTarget);
        if(iconIdentity!=nextIcon)
        {
            iconIdentity=nextIcon;int version=++appIconVersion;applicationIcon.Source=null;
            if(target!=null){var icon=await ApplicationIdentity.Icon(target);if(closing||version!=appIconVersion)return;applicationIcon.Source=icon;}
        }
        if(current==AppBarSection.Application)BuildBody();
        if(target?.Fullscreen==true&&sameMonitor){Collapse(false);Topmost=false;}else Topmost=true;
    }
    void UpdateClock()
    {
        string format=Prefs.AppBarClockFormat switch{1=>"h:mm tt",2=>"HH:mm",_=>CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern};
        string text=DateTime.Now.ToString(format,CultureInfo.CurrentCulture)+(Prefs.AppBarDate?"  "+DateTime.Now.ToString("d",CultureInfo.CurrentCulture):"");
        if(clock.Content?.ToString()!=text)clock.Content=text;clock.Visibility=Prefs.AppBarClock?Visibility.Visible:Visibility.Collapsed;
    }
    void UpdateBattery()
    {
        var reading=AppBarBattery.Read();battery.Visibility=reading.Present?Visibility.Visible:Visibility.Collapsed;
        if(!reading.Present)return;
        batteryFill.Width=reading.Percentage is int percentage?16*percentage/100d:0;
        chargingMark.Visibility=reading.Charging?Visibility.Visible:Visibility.Collapsed;
        batteryPercent.Text=reading.Percentage is int value?value+"%":"—";batteryPercent.Visibility=Prefs.AppBarBatteryPercentage?Visibility.Visible:Visibility.Collapsed;
        string description=(reading.Percentage is int level?level+"% battery":"Battery level unavailable")+(reading.Charging?" · Charging":reading.PluggedIn?" · Plugged in":"");battery.ToolTip=description;System.Windows.Automation.AutomationProperties.SetName(battery,description);
    }
    internal void RefreshContent(){UpdateAppearance();UpdateClock();UpdateBattery();ApplicationChanged();AudioChanged();MediaChanged();NetworkChanged();BluetoothChanged();}
}
