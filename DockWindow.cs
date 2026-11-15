using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Windows.Input;
using System.Runtime.InteropServices;

namespace NewStart;
public sealed partial class DockWindow : Window
{
    readonly Preferences prefs=Preferences.Load();
    readonly StackPanel row=new(){Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Bottom};
    readonly ContextMenu tray;
    readonly TextBlock clock=new(){Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10)};
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    readonly DispatcherTimer controlsReleaseTimer=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(16)};
    List<Native.TaskWindow> windows=new(); string signature="";
    StartMenuWindow? startMenu;
    AdaptiveAppBars? appBars;
    string settingsCategory="Dock";
    bool suppressSettingsPersistence;
    DockReservation? reservation;
    RecycleBinWatcher? recycleWatcher;
    TaskbarSuppression? taskbarSuppression;
    MenuItem? emptyRecycleItem;
    readonly DockPins dockPins;
    readonly Grid dockSurface=new();
    readonly DockGlass dockGlass=new();
    Brush dockInk=Brushes.White;Color? lastInk;
    readonly Border blankSurface=new(){Background=new SolidColorBrush(Color.FromArgb(1,0,0,0)),VerticalAlignment=VerticalAlignment.Bottom};
    Button? startButton;
    readonly List<(string Path,Button Button)> pinButtons=new();
    bool dragging;DateTime suppressClickUntil;
    bool positionQueued;
    bool emptyingRecycleBin;
    [StructLayout(LayoutKind.Sequential)]struct CursorPoint{public int X,Y;}
    [DllImport("user32.dll")]static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    DateTime startClosed;
    Button? controlsButton;
    bool controlsDismissedByIcon,controlsKeyboardDismiss;
    readonly Image recycleImage=new(){Width=36,Height=36};
    internal async System.Threading.Tasks.Task VerifyReservation()
    {
        double originalSize=prefs.IconSize;bool originalReserve=prefs.ReserveDockSpace;
        object result;
        try
        {
            prefs.ReserveDockSpace=false;Position();await System.Threading.Tasks.Task.Delay(100);var baseline=DockReservation.WorkArea;
            prefs.ReserveDockSpace=true;Position();await System.Threading.Tasks.Task.Delay(100);var enabled=DockReservation.WorkArea;
            prefs.IconSize=16;Refresh(true);Position();await System.Threading.Tasks.Task.Delay(100);var small=DockReservation.WorkArea;
            prefs.IconSize=64;Refresh(true);Position();await System.Threading.Tasks.Task.Delay(100);var large=DockReservation.WorkArea;
            var firstIcon=row.Children.OfType<Button>().First();
            double normalTop=Top+firstIcon.TranslatePoint(new Point(0,0),this).Y+(firstIcon.ActualHeight-prefs.IconSize)/2;
            double clearance=normalTop-large.Bottom/VisualTreeHelper.GetDpi(this).DpiScaleY;
            var beforeMenu=DockReservation.WorkArea;startMenu!.OpenMenu();await System.Threading.Tasks.Task.Delay(100);var duringMenu=DockReservation.WorkArea;startMenu.Dismiss();
            prefs.ReserveDockSpace=false;Position();await System.Threading.Tasks.Task.Delay(100);var released=DockReservation.WorkArea;
            result=new{BaselineBottom=baseline.Bottom,EnabledBottom=enabled.Bottom,SmallIconsBottom=small.Bottom,LargeIconsBottom=large.Bottom,ReservationApplied=enabled.Bottom<baseline.Bottom,SmallerDockUsesLessSpace=small.Bottom>large.Bottom,MenusDoNotReserveSpace=beforeMenu.Bottom==duringMenu.Bottom,ReleaseRestoresWorkArea=baseline.Bottom==released.Bottom,NormalIconClearanceDip=clearance};
        }
        finally{prefs.IconSize=originalSize;prefs.ReserveDockSpace=originalReserve;Refresh(true);Position();Save();}
        File.WriteAllText(System.IO.Path.Combine(Preferences.Folder,"reservation-verification.json"),System.Text.Json.JsonSerializer.Serialize(result));
    }
    internal async System.Threading.Tasks.Task VerifyBackdropDismissal()
    {
        int visible=0,hidden=0,stayedHidden=0;
        for(int i=0;i<10;i++)
        {
            startMenu!.OpenMenu();if(CustomBackdrop.Visible(startMenu))visible++;
            startMenu.Dismiss();if(!CustomBackdrop.Visible(startMenu))hidden++;
            startMenu.UpdateAppearance();await System.Threading.Tasks.Task.Delay(150);
            if(!CustomBackdrop.Visible(startMenu))stayedHidden++;
        }
        double original=prefs.MenuBlur;bool zeroHidden;
        try{prefs.MenuBlur=0;startMenu!.OpenMenu();zeroHidden=!CustomBackdrop.Visible(startMenu);startMenu.Dismiss();}
        finally{prefs.MenuBlur=original;startMenu!.UpdateAppearance();}
        var result=new{Cycles=10,VisibleWhenOpen=visible,HiddenImmediately=hidden,RemainedHidden=stayedHidden,ZeroBlurHidden=zeroHidden};
        File.WriteAllText(System.IO.Path.Combine(Preferences.Folder,"backdrop-dismissal-verification.json"),System.Text.Json.JsonSerializer.Serialize(result));
    }
    object VerifyDockLayout()
    {
        var original=prefs.Pins.ToList();bool originalFollow=prefs.FollowWindowsTaskbarPins;
        try{
            prefs.FollowWindowsTaskbarPins=false;string target=windows.First().Path;prefs.Pins=new(){target};Refresh(true);
            string name=System.IO.Path.GetFileNameWithoutExtension(target);
            var buttons=row.Children.OfType<Button>().ToList();
            int firstApp=prefs.ShowTaskView?2:1;
            bool pinnedFirst=buttons.Count>firstApp&&buttons[firstApp].ToolTip?.ToString()==name;
            bool unique=buttons.Count(b=>b.ToolTip?.ToString()==name)==1;
            bool recycleLast=buttons.Last().ToolTip?.ToString()=="Recycle Bin";
            bool separator=row.Children.OfType<Grid>().Any(g=>g.Tag?.ToString()=="Pinned and running apps separator");
            return new{PinnedAppFirst=pinnedFirst,RunningPinNotDuplicated=unique,RunningSeparatorPresent=separator,RecycleBinLast=recycleLast};
        }finally{prefs.Pins=original;prefs.FollowWindowsTaskbarPins=originalFollow;Refresh(true);}
    }
    // Automation activates the dock's root owner before input. This preview
    // exposes Start independently and avoids that activation dismissing it.
    internal void PreviewStartActions(){startMenu!.KeepOpenForTesting=true;startMenu.Owner=null;startMenu.ShowInTaskbar=true;startMenu.OpenMenu();}
    internal async System.Threading.Tasks.Task VerifyStart()
    {
        await StartCatalog.Refresh();
        var snapshot=StartCatalog.Current!;
        foreach(var app in snapshot.Pins)await StartCatalog.Icon(app.Target);
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        var completion=new System.Threading.Tasks.TaskCompletionSource<double>();
        var watch=Stopwatch.StartNew();
        EventHandler? handler=null;handler=(_,_)=>{watch.Stop();startMenu!.ContentRendered-=handler;completion.TrySetResult(watch.Elapsed.TotalMilliseconds);};
        startMenu!.ContentRendered+=handler;startMenu.OpenMenu();
        var winner=await System.Threading.Tasks.Task.WhenAny(completion.Task,System.Threading.Tasks.Task.Delay(5000));
        var result=new {PreparedOpenToFrameMilliseconds=winner==completion.Task?await completion.Task:-1,Apps=snapshot.Apps.Count,Pins=snapshot.Pins.Count,PinnedIconsReady=snapshot.Pins.Count(a=>StartCatalog.CachedIcon(a.Target)!=null),PowerIconFitsWithInset=startMenu.PowerIconFits,CustomBlurError=CustomBackdrop.LastError,DockLayout=VerifyDockLayout()};
        File.WriteAllText(System.IO.Path.Combine(Preferences.Folder,"start-open-verification.json"),System.Text.Json.JsonSerializer.Serialize(result));
    }
    public DockWindow()
    {
        dockPins=new DockPins(prefs,()=>TaskbarPins.Current);
        tray=ThemedMenus.Create(prefs);
        Mouse.AddPreviewMouseDownOutsideCapturedElementHandler(tray,(_,e)=>{
            if(e.ChangedButton!=MouseButton.Left||controlsButton is not {IsLoaded:true} button)return;
            if(new Rect(0,0,button.ActualWidth,button.ActualHeight).Contains(e.GetPosition(button)))
            {
                tray.IsOpen=false;controlsDismissedByIcon=true;controlsReleaseTimer.Start();e.Handled=true;
            }
        });
        tray.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Escape or Key.Enter or Key.Space)controlsKeyboardDismiss=true;};
        tray.Closed+=(_,_)=>{
            controlsDismissedByIcon=false;
            if(!controlsKeyboardDismiss&&controlsButton is {IsLoaded:true} button&&GetCursorPos(out var cursor))
                controlsDismissedByIcon=new Rect(0,0,button.ActualWidth,button.ActualHeight).Contains(button.PointFromScreen(new Point(cursor.X,cursor.Y)));
            controlsKeyboardDismiss=false;
            if(controlsDismissedByIcon)controlsReleaseTimer.Start();
        };
        // A captured popup can consume MouseUp instead of delivering it to the
        // dock. Clear the click guard once the physical button has been released.
        controlsReleaseTimer.Tick+=(_,_)=>{if((GetAsyncKeyState(1)&0x8000)==0){controlsReleaseTimer.Stop();controlsDismissedByIcon=false;}};
        Closed+=(_,_)=>controlsReleaseTimer.Stop();
        Title="New Start"; WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false; Topmost=true; ShowActivated=false;
        SizeToContent=SizeToContent.Width; Height=90;
        dockSurface.Children.Add(dockGlass.Surface);dockSurface.Children.Add(blankSurface);dockSurface.Children.Add(row);Content=dockSurface;
        var barMenu=ThemedMenus.Create(prefs);
        Add(barMenu,"Task Manager",()=>Launch("taskmgr.exe"));
        Add(barMenu,"Taskbar settings",()=>Launch("ms-settings:taskbar"));
        barMenu.Items.Add(new Separator());Add(barMenu,"New Start settings",Settings);
        dockSurface.ContextMenu=barMenu;
        AllowDrop=true;DragOver+=DockDragOver;Drop+=DockDrop;
        WallpaperPalette? wallpaper=null;
        Loaded+=(_,_)=>{
            wallpaper=new WallpaperPalette(Dispatcher);wallpaper.Changed+=WallpaperChanged;
            taskbarSuppression=new TaskbarSuppression(this,prefs);
            TaskbarPins.Refresh();
            RecycleBinIcon.Changed+=RecycleChanged;recycleWatcher=new RecycleBinWatcher(this);RecycleBinIcon.Refresh();
            Refresh(); Position();
            reservation=new DockReservation(this);reservation.Changed+=Position;
            Position();timer.Start();
            startMenu=new StartMenuWindow(prefs){Owner=this,DockHost=this};
            startMenu.Dismissed+=()=>startClosed=DateTime.UtcNow;
            startMenu.Prepare();
            if(prefs.AppBarEnabled){if(prefs.Edge==DockEdge.Top)prefs.Edge=DockEdge.Bottom;UpdateAppBars();Refresh(true);Position();}
        };
        SizeChanged+=(_,_)=>Position();
        // Windows can move a window after an AppBar work-area change. Restore
        // the edge-relative position after that notification has settled.
        LocationChanged+=(_,_)=>QueuePosition();
        timer.Tick+=(_,_)=>{ Refresh(); Position();taskbarSuppression?.Refresh(); clock.Text=DateTime.Now.ToString(prefs.Edge==DockEdge.Left?"HH\nmm":"t"); _=StartCatalog.Refresh();RecycleBinIcon.Refresh();TaskbarPins.Refresh(); };
        Closed+=(_,_)=>{taskbarSuppression?.Dispose(); if(wallpaper!=null){wallpaper.Changed-=WallpaperChanged;wallpaper.Dispose();}timer.Stop(); tray.IsOpen=false;appBars?.Dispose();appBars=null; startMenu?.Close();dockGlass.Dispose();recycleWatcher?.Dispose();RecycleBinIcon.Changed-=RecycleChanged;reservation?.Dispose(); };
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!dragging){controlsKeyboardDismiss=tray.IsOpen;tray.IsOpen=false;controlsDismissedByIcon=false;startMenu?.Dismiss();e.Handled=true;}};
    }
    void RecycleChanged()
    {
        if(Dispatcher.HasShutdownStarted)return;
        Dispatcher.BeginInvoke(new Action(()=>{
            recycleImage.Source=RecycleBinIcon.Current;
            if(emptyRecycleItem!=null)emptyRecycleItem.IsEnabled=!emptyingRecycleBin&&RecycleBinIcon.HasItems!=false;
        }));
    }
    void WallpaperChanged(){if(!IsLoaded)return;UpdateDockBackground();appBars?.Refresh();}
    void QueuePosition()
    {
        if(positionQueued||!IsLoaded||Dispatcher.HasShutdownStarted)return;positionQueued=true;
        Dispatcher.BeginInvoke(new Action(()=>{positionQueued=false;if(IsLoaded)Position();}),DispatcherPriority.ContextIdle);
    }
    internal async System.Threading.Tasks.Task VerifyMenuPerformance()
    {
        Button? button=null;
        for(int attempt=0;attempt<40&&button==null;attempt++){await System.Threading.Tasks.Task.Delay(250);Refresh();button=row.Children.OfType<Button>().FirstOrDefault(b=>b.ToolTip?.ToString()=="File Explorer");}
        button??=row.Children.OfType<Button>().First(b=>b.ContextMenu!=null);
        await AuditValidation.MeasureMenu(this,button,prefs);
    }
    internal async System.Threading.Tasks.Task VerifyLocations()
    {
        var checks=LocationValidation.Geometry();var clearances=new Dictionary<string,double>();var originalEdge=prefs.Edge;var originalAlignment=prefs.Alignment;bool originalReserve=prefs.ReserveDockSpace;
        try
        {
            prefs.ReserveDockSpace=false;Position();await System.Threading.Tasks.Task.Delay(200);var baseline=DockReservation.WorkArea;
            prefs.ReserveDockSpace=true;
            foreach(var edge in Enum.GetValues<DockEdge>())foreach(var alignment in Enum.GetValues<DockAlignment>())
            {
                prefs.Edge=edge;prefs.Alignment=alignment;Refresh(true);UpdateLayout();Position();await System.Threading.Tasks.Task.Delay(200);
                startMenu!.OpenMenu();await System.Threading.Tasks.Task.Delay(150);
                var menu=new Rect(startMenu.Left,startMenu.Top,startMenu.ActualWidth,startMenu.ActualHeight);var anchor=StartAnchor;
                var area=DockReservation.WorkArea;
                bool reserve=edge switch{DockEdge.Top=>area.Top>baseline.Top,DockEdge.Left=>area.Left>baseline.Left,_=>area.Bottom<baseline.Bottom};
                bool side=edge switch{DockEdge.Top=>menu.Top>=anchor.Bottom,DockEdge.Left=>menu.Left>=anchor.Right,_=>menu.Bottom<=anchor.Top};
                bool centred=edge==DockEdge.Left||alignment!=DockAlignment.Centre||Math.Abs(menu.Left+menu.Width/2-(SystemParameters.WorkArea.Left+SystemParameters.WorkArea.Width/2))<1;
                if(edge==DockEdge.Left&&alignment==DockAlignment.Centre)centred=Math.Abs(menu.Top+menu.Height/2-(Top+ActualHeight/2))<1;
                checks[$"Live{edge}{alignment}"]=reserve&&side&&centred&&SystemParameters.WorkArea.Contains(menu);
                var dpi=VisualTreeHelper.GetDpi(this);
                double clearance=edge switch{DockEdge.Top=>area.Top/dpi.DpiScaleY-anchor.Bottom,DockEdge.Left=>area.Left/dpi.DpiScaleX-anchor.Right,_=>anchor.Top-area.Bottom/dpi.DpiScaleY};
                double pixel=1/(edge==DockEdge.Left?dpi.DpiScaleX:dpi.DpiScaleY);
                clearances[$"{edge}{alignment}"]=clearance;
                checks[$"Clearance{edge}{alignment}"]=clearance>=DockGeometry.WindowClearance-.01&&clearance<=DockGeometry.WindowClearance+pixel;
                var outer=reservation!.Bounds;
                double outside=edge switch{DockEdge.Top=>anchor.Top-outer.Top/dpi.DpiScaleY,DockEdge.Left=>anchor.Left-outer.Left/dpi.DpiScaleX,_=>outer.Bottom/dpi.DpiScaleY-anchor.Bottom};
                clearances[$"Screen{edge}{alignment}"]=outside;
                checks[$"ScreenClearance{edge}{alignment}"]=Math.Abs(outside-DockGeometry.ScreenClearance)<=pixel;
                startMenu.Dismiss();
            }
        }
        finally{prefs.Edge=originalEdge;prefs.Alignment=originalAlignment;prefs.ReserveDockSpace=originalReserve;Refresh(true);UpdateLayout();Position();}
        File.WriteAllText(System.IO.Path.Combine(Preferences.Folder,"location-verification.json"),System.Text.Json.JsonSerializer.Serialize(checks,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(System.IO.Path.Combine(Preferences.Folder,"location-clearance-verification.json"),System.Text.Json.JsonSerializer.Serialize(clearances));
        Close();
    }
    internal async System.Threading.Tasks.Task VerifyDockGlass()
    {
        var checks=new Dictionary<string,bool>{{"DefaultOff",!new Preferences().DockBackground}};
        var original=(prefs.DockBackground,prefs.Edge,prefs.Alignment,prefs.Theme,prefs.DockTransparency,prefs.DockBlur,prefs.ReserveDockSpace);
        int originalColourSource=prefs.DockColourSource;
        try
        {
            prefs.DockBackground=true;prefs.ReserveDockSpace=true;prefs.Alignment=DockAlignment.Centre;prefs.DockColourSource=0;
            foreach(var edge in Enum.GetValues<DockEdge>())foreach(int theme in new[]{1,2})
            {
                prefs.Edge=edge;prefs.Theme=theme;Refresh(true);UpdateLayout();Position();await System.Threading.Tasks.Task.Delay(150);UpdateLayout();Position();
                string key=$"{edge}{(theme==1?"Light":"Dark")}";
                var bounds=dockGlass.Bounds;var frame=new Rect(Left+bounds.Left,Top+bounds.Top,bounds.Width,bounds.Height);
                var area=DockReservation.WorkArea;var outer=reservation!.Bounds;var dpi=VisualTreeHelper.GetDpi(this);double pixel=1/dpi.DpiScaleX;
                double inner=edge switch{DockEdge.Top=>area.Top/dpi.DpiScaleY-frame.Bottom,DockEdge.Left=>area.Left/dpi.DpiScaleX-frame.Right,_=>frame.Top-area.Bottom/dpi.DpiScaleY};
                double screen=edge switch{DockEdge.Top=>frame.Top-outer.Top/dpi.DpiScaleY,DockEdge.Left=>frame.Left-outer.Left/dpi.DpiScaleX,_=>outer.Bottom/dpi.DpiScaleY-frame.Bottom};
                checks[key+"Spacing"]=Math.Abs(inner-8)<=pixel&&Math.Abs(screen-8)<=pixel;
                foreach(double transparency in new[]{0d,.75,1d})foreach(double blur in new[]{0d,20d})
                {
                    prefs.DockTransparency=transparency;prefs.DockBlur=blur;UpdateDockBackground();
                    bool effects=NativeBackdrop.TransparencyAllowed();
                    checks[$"{key}T{transparency}B{blur}"]=dockGlass.Surface.IsVisible&&Math.Abs(dockGlass.TintOpacity-(1-transparency))<.00001&&dockGlass.NativeVisible==(effects&&blur>0&&transparency>0&&transparency<1);
                    checks[$"{key}PureColourT{transparency}B{blur}"]=dockGlass.TintColour==(theme==1?Colors.White:Colors.Black);
                }
            }
            prefs.DockBackground=false;Position();checks["DisabledHasNoSurfaceOrBlur"]=dockGlass.Surface.Visibility==Visibility.Collapsed&&!dockGlass.NativeVisible;
        }
        finally
        {
            (prefs.DockBackground,prefs.Edge,prefs.Alignment,prefs.Theme,prefs.DockTransparency,prefs.DockBlur,prefs.ReserveDockSpace)=original;
            prefs.DockColourSource=originalColourSource;
            Refresh(true);UpdateLayout();Position();
        }
        File.WriteAllText(System.IO.Path.Combine(Preferences.Folder,"dock-glass-verification.json"),System.Text.Json.JsonSerializer.Serialize(checks,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Close();
    }
    void Separator(string name)
    {
        bool vertical=prefs.Edge==DockEdge.Left;
        var line=new Border{Width=vertical?prefs.IconSize*.8:1,Height=vertical?1:prefs.IconSize*.8,Background=new SolidColorBrush(Color.FromArgb(120,255,255,255)),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
        var space=new Grid{Width=vertical?prefs.IconSize*1.75+12:20,Height=vertical?20:prefs.IconSize*1.75+12,VerticalAlignment=VerticalAlignment.Bottom,Tag=name};space.Children.Add(line);System.Windows.Automation.AutomationProperties.SetName(space,name);row.Children.Add(space);
    }
    void Position()
    {
        if(!IsLoaded)return;
        // The icon is centred inside its hit target. Reserve to its normal visible top,
        // not the larger transparent window used for hover/bounce animation.
        double iconButtonHeight=prefs.IconSize*1.75+12;
        double padding=prefs.DockBackground?DockGeometry.BackgroundPadding:0;
        double reservedThickness=DockGeometry.ScreenClearance+prefs.IconSize+DockGeometry.WindowClearance+2*padding;
        bool vertical=prefs.Edge==DockEdge.Left;
        double hitThickness=prefs.IconSize+4;
        blankSurface.Height=vertical?double.NaN:hitThickness;blankSurface.Width=vertical?hitThickness:double.NaN;
        blankSurface.VerticalAlignment=prefs.Edge==DockEdge.Top?VerticalAlignment.Top:vertical?VerticalAlignment.Stretch:VerticalAlignment.Bottom;
        blankSurface.HorizontalAlignment=vertical?HorizontalAlignment.Left:HorizontalAlignment.Stretch;
        blankSurface.CornerRadius=new CornerRadius(0);
        double hitInset=(iconButtonHeight-prefs.IconSize)/2-2;
        blankSurface.Margin=prefs.Edge switch{DockEdge.Top=>new Thickness(0,hitInset,0,0),DockEdge.Left=>new Thickness(hitInset,0,0,0),_=>new Thickness(0,0,0,hitInset)};
        reservation?.Update(prefs.ReserveDockSpace,reservedThickness,prefs.Edge);
        Rect area;
        if(reservation is {Enabled:true} bar&&bar.Bounds.Bottom>bar.Bounds.Top)
        {
            var dpi=VisualTreeHelper.GetDpi(this);var bounds=bar.Bounds;
            area=new Rect(bounds.Left/dpi.DpiScaleX,bounds.Top/dpi.DpiScaleY,(bounds.Right-bounds.Left)/dpi.DpiScaleX,(bounds.Bottom-bounds.Top)/dpi.DpiScaleY);
        }
        else area=SystemParameters.WorkArea;
        var origin=DockGeometry.DockOrigin(area,new Size(ActualWidth,ActualHeight),prefs.Edge,prefs.Alignment,prefs.IconSize,padding);
        Left=origin.X;Top=origin.Y;
        UpdateDockBackground();
        if(startMenu?.IsVisible==true)startMenu.Reposition();
    }
    void UpdateDockBackground()
    {
        if(!IsLoaded)return;
        string? monitor=DockMonitorName();
        dockInk=DockGlass.Ink(prefs,monitor);var colour=((SolidColorBrush)dockInk).Color;
        if(lastInk!=colour){foreach(var item in row.Children.OfType<DependencyObject>())TintDockItem(item,dockInk);lastInk=colour;}
        Rect bounds=Rect.Empty;
        if(prefs.DockBackground&&row.Children.OfType<Button>().FirstOrDefault() is {IsLoaded:true} first&&row.Children.OfType<Button>().LastOrDefault() is Button last)
        {
            var firstPoint=first.TranslatePoint(new Point((first.ActualWidth-prefs.IconSize)/2,(first.ActualHeight-prefs.IconSize)/2),this);
            var anchor=new Rect(Left+firstPoint.X,Top+firstPoint.Y,prefs.IconSize,prefs.IconSize);var point=last.TranslatePoint(new Point((last.ActualWidth-prefs.IconSize)/2,(last.ActualHeight-prefs.IconSize)/2),this);
            double padding=DockGeometry.BackgroundPadding;
            bounds=new Rect(new Point(anchor.Left-Left-padding,anchor.Top-Top-padding),new Point(point.X+prefs.IconSize+padding,point.Y+prefs.IconSize+padding));
            blankSurface.HorizontalAlignment=HorizontalAlignment.Left;blankSurface.VerticalAlignment=VerticalAlignment.Top;
            blankSurface.Width=bounds.Width;blankSurface.Height=bounds.Height;blankSurface.Margin=new Thickness(bounds.Left,bounds.Top,0,0);blankSurface.CornerRadius=new CornerRadius(Math.Min(18,Math.Min(bounds.Width,bounds.Height)/2));
        }
        dockGlass.Update(this,prefs,bounds);
    }
    string? DockMonitorName()=>DesktopDisplays.All().FirstOrDefault(display=>display.Handle==DesktopDisplays.MonitorFromWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle,2))?.Name;
    static void TintDockItem(DependencyObject item,Brush ink)
    {
        if(item is Shape shape){if(shape.Stroke!=null)shape.Stroke=ink;if(shape.Fill!=null)shape.Fill=ink;}
        if(item is TextBlock text)text.Foreground=ink;
        foreach(var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())TintDockItem(child,ink);
    }
    internal Rect StartAnchor
    {
        get{if(startButton is not {IsLoaded:true})return new Rect(Left,Top,prefs.IconSize,prefs.IconSize);var point=startButton.TranslatePoint(new Point((startButton.ActualWidth-prefs.IconSize)/2,(startButton.ActualHeight-prefs.IconSize)/2),this);return new Rect(Left+point.X,Top+point.Y,prefs.IconSize,prefs.IconSize);}
    }
    Button MakeIcon(object content,string name,Action<Button> action)
    {
        var visual=new Grid{Width=prefs.IconSize,Height=prefs.IconSize,Background=new SolidColorBrush(Color.FromArgb(1,0,0,0)),RenderTransformOrigin=new Point(.5,.5)};
        var scale=new ScaleTransform(1,1); var translate=new TranslateTransform();
        visual.RenderTransform=new TransformGroup{Children=new TransformCollection{scale,translate}};
        if(content is UIElement element){TintDockItem(element,dockInk);visual.Children.Add(new Viewbox{Child=element,Stretch=Stretch.Uniform});}
        else visual.Children.Add(new Viewbox
        {
            Stretch=Stretch.Uniform,
            Child=new Grid
            {
                Width=36,Height=36,
                Children={new TextBlock{Text=content.ToString(),FontSize=25,Foreground=dockInk,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}}
            }
        });
        bool vertical=prefs.Edge==DockEdge.Left;
        var b=new Button{Content=visual,Width=prefs.IconSize*1.75+(vertical?12:8),Height=prefs.IconSize*1.75+(vertical?8:12),Background=Brushes.Transparent,BorderThickness=new Thickness(0),ToolTip=name,VerticalAlignment=VerticalAlignment.Bottom};
        System.Windows.Automation.AutomationProperties.SetName(b,name);
        // Layered HWNDs pass through pixels with zero alpha before WPF hit testing.
        // Alpha 1 keeps the full button interactive without a visible container.
        var hitSurface=new FrameworkElementFactory(typeof(Border));
        hitSurface.SetValue(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(1,0,0,0)));
        hitSurface.SetValue(vertical?FrameworkElement.WidthProperty:FrameworkElement.HeightProperty,prefs.IconSize+4);
        hitSurface.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
        hitSurface.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        var factory=new FrameworkElementFactory(typeof(ContentPresenter)); factory.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center); factory.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);
        hitSurface.AppendChild(factory);
        b.Template=new ControlTemplate(typeof(Button)){VisualTree=hitSurface};
        void Hover(double value){var a=new DoubleAnimation(value,TimeSpan.FromMilliseconds(prefs.HoverMilliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};scale.BeginAnimation(ScaleTransform.ScaleXProperty,a);scale.BeginAnimation(ScaleTransform.ScaleYProperty,a);}
        b.MouseEnter+=(_,_)=>Hover(prefs.Hover&&SystemParameters.ClientAreaAnimation?prefs.Magnification:1);
        b.MouseLeave+=(_,_)=>Hover(1);
        b.Tag=translate; b.Click+=(_,_)=>{if(DateTime.UtcNow>=suppressClickUntil)action(b);}; return b;
    }
    void Bounce(Button b)
    {
        if(!prefs.Bounce||!SystemParameters.ClientAreaAnimation)return;
        var a=new DoubleAnimationUsingKeyFrames{Duration=TimeSpan.FromMilliseconds(prefs.BounceMilliseconds)};
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0,KeyTime.FromPercent(0)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(-prefs.BounceHeight,KeyTime.FromPercent(.22),new CubicEase{EasingMode=EasingMode.EaseOut}));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(2,KeyTime.FromPercent(.48),new CubicEase{EasingMode=EasingMode.EaseIn}));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(-prefs.BounceHeight*.3,KeyTime.FromPercent(.7)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0,KeyTime.FromPercent(1)));
        if(prefs.Edge!=DockEdge.Bottom)foreach(var key in a.KeyFrames.OfType<EasingDoubleKeyFrame>())key.Value=-key.Value;
        ((TranslateTransform)b.Tag).BeginAnimation(prefs.Edge==DockEdge.Left?TranslateTransform.XProperty:TranslateTransform.YProperty,a);
    }
    void Refresh(bool force=false)
    {
        if(dragging||(!force&&(tray.IsOpen||row.Children.OfType<Button>().Any(b=>b.ContextMenu?.IsOpen==true))))return;
        windows=Native.Windows();
        dockInk=DockGlass.Ink(prefs,DockMonitorName());
        var effectivePins=dockPins.All();
        string next=string.Join("|",windows.Select(w=>$"{w.Handle}:{w.Path}:{w.Title}"))+string.Join("|",effectivePins);
        if(!force&&next==signature)return; signature=next; row.Children.Clear();pinButtons.Clear();
        bool vertical=prefs.Edge==DockEdge.Left;
        row.Orientation=vertical?Orientation.Vertical:Orientation.Horizontal;
        row.VerticalAlignment=prefs.Edge==DockEdge.Top?VerticalAlignment.Top:vertical?VerticalAlignment.Stretch:VerticalAlignment.Bottom;
        row.HorizontalAlignment=vertical?HorizontalAlignment.Left:HorizontalAlignment.Stretch;
        SizeToContent=SizeToContent.Manual;
        if(vertical){Height=double.NaN;Width=prefs.IconSize*1.75+36;SizeToContent=SizeToContent.Height;}
        else{Width=double.NaN;Height=prefs.IconSize*1.75+36;SizeToContent=SizeToContent.Width;}
        startButton=MakeIcon(new Ellipse{Width=25,Height=25,Stroke=Brushes.White,StrokeThickness=2},"Start",_=>{
            if(startMenu?.IsVisible==true){startMenu.Dismiss();return;}
            if((DateTime.UtcNow-startClosed).TotalMilliseconds<250)return;
            if(startMenu!=null)startMenu.AppBarPlacement=null;
            startMenu?.OpenMenu();
        });startButton.Loaded+=(_,_)=>QueuePosition();if(appBars==null||!appBars.Windows.Any())row.Children.Add(startButton);
        var taskViewIcon=new System.Windows.Shapes.Path
        {
            Data=Geometry.Parse("M 3,7 L 21,7 L 21,29 L 3,29 Z M 26,7 L 33,7 L 33,29 L 26,29 Z"),
            Width=36,Height=36,Stroke=Brushes.White,StrokeThickness=1.7,StrokeLineJoin=PenLineJoin.Round
        };
        if(prefs.ShowTaskView)row.Children.Add(MakeIcon(taskViewIcon,"Task View",_=>{
            startMenu?.Dismiss();
            if(!Native.TaskView())MessageBox.Show("Windows could not open Task View. You can use Windows + Tab.","New Start");
        }));
        var running=windows.Select(w=>w.Path).Where(p=>!effectivePins.Any(pin=>PinIdentity.Matches(pin,p))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool runningStarted=false;
        foreach(var path in effectivePins.Concat(running))
        {
            bool pinned=effectivePins.Contains(path,StringComparer.OrdinalIgnoreCase);
            if(!pinned&&!runningStarted){Separator("Pinned and running apps separator");runningStarted=true;}
            var tasks=windows.Where(w=>PinIdentity.Matches(path,w.Path)).ToList();
            var image=new Image{Source=StartCatalog.CachedIcon(path)};
            image.Loaded+=async(_,_)=>image.Source=await StartCatalog.Icon(path);
            var cell=new Grid{Width=36,Height=36}; cell.Children.Add(image);
            if(tasks.Count>0&&prefs.Indicators) cell.Children.Add(new Ellipse{Width=4,Height=4,Fill=dockInk,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,prefs.DockBackground?-2:-9)});
            string displayName=StartCatalog.Current?.Apps.FirstOrDefault(a=>string.Equals(a.Target,path,StringComparison.OrdinalIgnoreCase))?.Name??System.IO.Path.GetFileNameWithoutExtension(path);
            var button=MakeIcon(cell,displayName,b=>{
                if(tasks.Count==0){if(Launch(path)) Bounce(b);}
                else if(tasks.Count==1) Activate(tasks[0]);
                else {var menu=ThemedMenus.Create(prefs);foreach(var w in tasks)Add(menu,w.Title,()=>Activate(w)); menu.PlacementTarget=b;menu.IsOpen=true;}
            });
            var context=ThemedMenus.Create(prefs);
            Add(context,displayName,()=>{if(Launch(path))Bounce(button);});
            Add(context,pinned?"Unpin from taskbar":"Pin to taskbar",()=>{if(pinned)dockPins.Remove(path);else dockPins.Place(path,dockPins.All().Count);Save();Refresh(true);});
            if(tasks.Count>0)Add(context,tasks.Count==1?"Close window":"Close all windows",()=>{foreach(var task in Native.Windows().Where(w=>PinIdentity.Matches(path,w.Path)))Native.CloseWindow(task.Handle);});
            int menuGeneration=0;
            context.Closed+=(_,_)=>menuGeneration++;
            context.Opened+=async(_,_)=>{
                int generation=++menuGeneration;
                foreach(var old in context.Items.OfType<FrameworkElement>().Where(i=>Equals(i.Tag,"JumpList")).ToList())context.Items.Remove(old);
                var destinations=await JumpListReader.Read(path);
                if(!context.IsOpen||generation!=menuGeneration||destinations.Count==0)return;
                var heading=new MenuItem{Header="Recent",IsEnabled=false,Tag="JumpList"};context.Items.Insert(0,heading);int position=1;
                foreach(var destination in destinations){var item=new MenuItem{Header=destination.Name.Replace("_","__"),Tag="JumpList"};item.Click+=(_,_)=>Launch(destination.Target);context.Items.Insert(position++,item);}
                context.Items.Insert(position,new Separator{Tag="JumpList"});
            };
            if(pinned){Add(context,vertical?"Move up":"Move left",()=>MovePin(path,-1));Add(context,vertical?"Move down":"Move right",()=>MovePin(path,1));}
            foreach(var w in tasks)Add(context,w.Title,()=>Activate(w));
            AttachPinDrag(button,path,pinned);if(pinned)pinButtons.Add((path,button));
            button.ContextMenu=context; row.Children.Add(button);
        }
        Separator("Dock utilities separator");
        var controlsIcon=new System.Windows.Shapes.Path{Data=Geometry.Parse("M 4,8 L 32,8 M 4,18 L 32,18 M 4,28 L 32,28 M 12,4 L 12,12 M 24,14 L 24,22 M 15,24 L 15,32"),Stroke=Brushes.White,StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Width=36,Height=36};
        if(prefs.ShowSystemControls){var expand=MakeIcon(controlsIcon,"System controls",b=>{
            if(tray.IsOpen){tray.IsOpen=false;controlsDismissedByIcon=false;return;}
            // The menu can dismiss on mouse-down before the button receives Click.
            if(controlsDismissedByIcon){controlsDismissedByIcon=false;return;}
            BuildTray();tray.Placement=prefs.Edge switch{DockEdge.Top=>PlacementMode.Bottom,DockEdge.Left=>PlacementMode.Right,_=>PlacementMode.Top};tray.PlacementTarget=b;tray.IsOpen=true;
        });controlsButton=expand;row.Children.Add(expand);}
        if(prefs.Clock==2&&!(prefs.AppBarEnabled&&prefs.AppBarClock)){clock.Foreground=dockInk;clock.MaxWidth=vertical?prefs.IconSize*1.75+12:double.PositiveInfinity;clock.TextWrapping=vertical?TextWrapping.Wrap:TextWrapping.NoWrap;clock.Margin=vertical?new Thickness(2,8,2,8):new Thickness(10);row.Children.Add(clock);}
        if(prefs.ShowDesktop)
        {
            var desktop=new Grid{Width=36,Height=36};
            desktop.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M 3,5 L 30,5 L 30,19 M 3,5 L 3,29 L 14,29 M 3,10 L 30,10 M 3,25 L 14,25"),Stroke=Brushes.White,StrokeThickness=1.5,StrokeLineJoin=PenLineJoin.Round});
            desktop.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("M 7,14 L 10,14 L 10,17 L 7,17 Z M 13,14 L 16,14 L 16,17 L 13,17 Z M 7,20 L 10,20 L 10,23 L 7,23 Z"),Fill=Brushes.White});
            desktop.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse("F0 M 17,27 C 21,20 30,20 35,27 C 30,34 21,34 17,27 Z M 29,27 A 3,3 0 1 1 23,27 A 3,3 0 1 1 29,27 Z"),Fill=Brushes.White});
            row.Children.Add(MakeIcon(desktop,"Show Desktop",_=>{
                startMenu?.Dismiss();
                if(!Native.ShowDesktop())MessageBox.Show("Windows could not show the desktop. You can use Windows + D.","New Start");
            }));
        }
        // A fresh Image avoids assigning an existing visual to a second Viewbox on rebuild.
        recycleImage.Source=RecycleBinIcon.Current;
        if(recycleImage.Parent is Viewbox oldParent)oldParent.Child=null;
        var recycle=MakeIcon(recycleImage,"Recycle Bin",_=>Launch("shell:RecycleBinFolder"));
        var recycleMenu=ThemedMenus.Create(prefs);Add(recycleMenu,"Open Recycle Bin",()=>Launch("shell:RecycleBinFolder"));
        var empty=new MenuItem{Header="Empty Recycle Bin",Icon=new TextBlock{Text="\uE74D",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=15}};
        emptyRecycleItem=empty;
        empty.Click+=(_,_)=>EmptyRecycleBin();recycleMenu.Items.Add(empty);
        recycleMenu.Opened+=(_,_)=>{RecycleBinIcon.Refresh(true);empty.IsEnabled=!emptyingRecycleBin&&RecycleBinIcon.HasItems!=false;};
        recycle.ContextMenu=recycleMenu;row.Children.Add(recycle);
    }
    void EmptyRecycleBin()
    {
        if(emptyingRecycleBin)return;emptyingRecycleBin=true;
        try
        {
            int result=RecycleBinIcon.Empty(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            if(result<0&&result!=unchecked((int)0x800704C7)&&result!=unchecked((int)0x80004004))
                MessageBox.Show(this,Marshal.GetExceptionForHR(result)?.Message??"Windows could not empty the Recycle Bin.","New Start");
        }
        finally{emptyingRecycleBin=false;}
    }
    internal void ToggleStartFromAppBar(Native.Rect anchor,DesktopDisplay display)
    {
        if(startMenu==null)return;
        if(startMenu.IsVisible){startMenu.Dismiss();return;}
        if((DateTime.UtcNow-startClosed).TotalMilliseconds<250)return;
        startMenu.AppBarPlacement=(anchor,display);startMenu.OpenMenu();
    }
    internal void AppBarDisplaysChanged(){Refresh(true);QueuePosition();}
    void UpdateAppBars()
    {
        if(prefs.AppBarEnabled)
        {
            prefs.AppBarTransparency??=prefs.StartTransparency;prefs.AppBarBlur??=prefs.MenuBlur;
            try{if(appBars==null)appBars=new AdaptiveAppBars(this,prefs);else appBars.Refresh();}
            catch(Exception e){appBars?.Dispose();appBars=null;prefs.AppBarEnabled=false;MessageBox.Show("The AppBar could not start. The dock remains available.\n"+e.Message,"New Start");}
        }
        else{appBars?.Dispose();appBars=null;if(startMenu!=null){startMenu.Dismiss();startMenu.AppBarPlacement=null;}}
    }
    void EnableAppBar(bool enabled)
    {
        if(prefs.AppBarEnabled==enabled)return;
        if(enabled){prefs.AppBarPreviousEdge=prefs.Edge;prefs.AppBarPreviousAlignment=prefs.Alignment;if(prefs.Edge==DockEdge.Top){prefs.Edge=DockEdge.Bottom;prefs.Alignment=DockAlignment.Centre;}}
        else{prefs.Edge=prefs.AppBarPreviousEdge;prefs.Alignment=prefs.AppBarPreviousAlignment;}
        prefs.AppBarEnabled=enabled;UpdateAppBars();Refresh(true);Position();Save();
    }
    internal bool IsDockPinned(string path)=>dockPins.All().Any(pin=>string.Equals(PinIdentity.Resolve(pin),PinIdentity.Resolve(path),StringComparison.OrdinalIgnoreCase));
    internal void PinFromStart(string path){dockPins.Place(path,dockPins.All().Count);Save();Refresh(true);}
    void MovePin(string path,int delta){dockPins.Move(path,delta);Save();Refresh(true);}
    void AttachPinDrag(Button button,string path,bool pinned)
    {
        Point origin=default;bool pressed=false;
        button.PreviewMouseLeftButtonDown+=(_,e)=>{origin=e.GetPosition(button);pressed=true;};
        button.PreviewMouseLeftButtonUp+=(_,_)=>pressed=false;
        button.PreviewMouseMove+=(_,e)=>{
            if(!pressed||dragging||e.LeftButton!=MouseButtonState.Pressed)return;
            Point point=e.GetPosition(button);if(Math.Abs(point.X-origin.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-origin.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            pressed=false;e.Handled=true;bool cancelled=false,releasedOutside=false;
            QueryContinueDragEventHandler continuation=(_,args)=>{
                if(args.EscapePressed){cancelled=true;args.Action=DragAction.Cancel;return;}
                if((args.KeyStates&DragDropKeyStates.LeftMouseButton)==0&&GetCursorPos(out var cursor))
                {
                    var local=PointFromScreen(new Point(cursor.X,cursor.Y));
                    releasedOutside=local.X < -32||local.X>ActualWidth+32||local.Y < -32||local.Y>ActualHeight+32;
                }
            };
            button.QueryContinueDrag+=continuation;dragging=true;button.Opacity=.5;
            try{DragDrop.DoDragDrop(button,new DataObject(DockPins.DragFormat,path),DragDropEffects.Move|DragDropEffects.Copy);}
            finally{button.QueryContinueDrag-=continuation;dragging=false;button.Opacity=1;suppressClickUntil=DateTime.UtcNow.AddMilliseconds(250);}
            if(pinned&&!cancelled&&releasedOutside){dockPins.Remove(path);Save();}
            Refresh(true);
        };
    }
    static bool IsApplicationFile(string path)=>File.Exists(path)&&(System.IO.Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".lnk" or ".appref-ms");
    static List<string> DragApplications(IDataObject data)
    {
        if(data.GetDataPresent(DockPins.DragFormat)&&data.GetData(DockPins.DragFormat) is string app)return new(){app};
        if(data.GetDataPresent(DataFormats.FileDrop)&&data.GetData(DataFormats.FileDrop) is string[] files)return files.Where(IsApplicationFile).ToList();
        return new();
    }
    void DockDragOver(object sender,DragEventArgs e)
    {
        e.Effects=DragApplications(e.Data).Count==0?DragDropEffects.None:e.Data.GetDataPresent(DockPins.DragFormat)?DragDropEffects.Move:DragDropEffects.Copy;e.Handled=true;
    }
    void DockDrop(object sender,DragEventArgs e)
    {
        var apps=DragApplications(e.Data);if(apps.Count==0){e.Effects=DragDropEffects.None;e.Handled=true;return;}
        bool vertical=prefs.Edge==DockEdge.Left;var drop=e.GetPosition(this);double axis=vertical?drop.Y:drop.X;
        int index=pinButtons.Count(pin=>!apps.Any(app=>string.Equals(PinIdentity.Resolve(app),PinIdentity.Resolve(pin.Path),StringComparison.OrdinalIgnoreCase))&&PinAxis(pin.Button,vertical)<axis);
        bool external=!e.Data.GetDataPresent(DockPins.DragFormat);
        try{
            foreach(var app in apps)
            {
                string target=app;
                if(external&&System.IO.Path.GetExtension(app).ToLowerInvariant() is ".lnk" or ".appref-ms")
                {
                    var existing=dockPins.All().FirstOrDefault(pin=>string.Equals(PinIdentity.Resolve(pin),PinIdentity.Resolve(app),StringComparison.OrdinalIgnoreCase));
                    if(existing!=null)target=existing;
                    else{string folder=System.IO.Path.Combine(Preferences.Folder,"Pins",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);target=System.IO.Path.Combine(folder,System.IO.Path.GetFileName(app));File.Copy(app,target);}
                }
                dockPins.Place(target,index++);
            }
            Save();e.Effects=external?DragDropEffects.Copy:DragDropEffects.Move;
        }catch(Exception error){MessageBox.Show("Could not pin this application: "+error.Message,"New Start");e.Effects=DragDropEffects.None;}
        e.Handled=true;if(!dragging)Refresh(true);
    }
    double PinAxis(Button button,bool vertical){var point=button.TranslatePoint(new Point(button.ActualWidth/2,button.ActualHeight/2),this);return vertical?point.Y:point.X;}
    void Save(){if(suppressSettingsPersistence)return;try{prefs.Save();}catch(Exception e){MessageBox.Show("Could not save settings: "+e.Message);}}
    static void Add(ContextMenu menu,string title,Action action){var item=new MenuItem{Header=title};item.Click+=(_,_)=>action();menu.Items.Add(item);}
    static bool Launch(string target){try{Process.Start(new ProcessStartInfo(target){UseShellExecute=true});return true;}catch(Exception e){MessageBox.Show(e.Message,"Could not open");return false;}}
    static void Activate(Native.TaskWindow w){if(Native.GetForegroundWindow()==w.Handle){Native.ShowWindowAsync(w.Handle,6);return;}if(Native.IsIconic(w.Handle))Native.ShowWindowAsync(w.Handle,9);if(!Native.SetForegroundWindow(w.Handle))MessageBox.Show("Windows declined window activation. Use Alt+Tab to switch to this window.");}
    void BuildTray()
    {
        tray.Items.Clear();
        foreach(var entry in new[]{("Sound","ms-settings:sound","\uE767"),("Network","ms-settings:network-status","\uE701"),("Bluetooth","ms-settings:bluetooth","\uE702"),("Windows taskbar","ms-settings:taskbar","\uE713")})
        {
            var item=new MenuItem{Header=entry.Item1,Icon=new TextBlock{Text=entry.Item3,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=15,VerticalAlignment=VerticalAlignment.Center}};
            item.Click+=(_,_)=>Launch(entry.Item2);tray.Items.Add(item);
        }
        if(prefs.Clock==1)
        {
            tray.Items.Add(new Separator());
            tray.Items.Add(new MenuItem{Header=DateTime.Now.ToString("f"),IsEnabled=false,Opacity=1,Icon=new TextBlock{Text="\uE787",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=15}});
        }
    }
    internal FrameworkElement EmbeddedStartContents(double width,double height,Action dismiss,out Action release)
    {
        if(startMenu==null)throw new InvalidOperationException("Start is not ready.");return startMenu.Embed(width,height,dismiss,out release);
    }
    internal bool FocusEmbeddedStart()=>startMenu?.FocusSearch()==true;
    internal void Settings()
    {
        if(appBars?.OpenSettings()==true)return;
        var dialog=new Window{Title="New Start settings (Beta)",WindowStyle=WindowStyle.None,AllowsTransparency=true,Width=500,Height=Math.Min(740,SystemParameters.WorkArea.Height-32),MinWidth=420,MinHeight=400,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterScreen,ResizeMode=ResizeMode.NoResize};
        NativeBackdrop.Configure(dialog);dialog.Content=SettingsContents(dialog,dialog.Close,false,out var release);dialog.Closed+=(_,_)=>release();dialog.ShowDialog();
    }
    internal FrameworkElement SettingsContents(Window dialog,Action dismiss,bool embedded,out Action release)
    {
        var groups=new Dictionary<string,StackPanel>();var navButtons=new Dictionary<string,Button>();
        var pages=new Grid();var panel=new StackPanel();var navigation=new WrapPanel{Margin=new Thickness(20,0,20,8)};
        var scrollSettings=new ScrollViewer{Content=pages,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        void SelectCategory(string category){settingsCategory=category;foreach(var group in groups)group.Value.Visibility=group.Key==category?Visibility.Visible:Visibility.Collapsed;foreach(var button in navButtons){button.Value.FontWeight=button.Key==category?FontWeights.SemiBold:FontWeights.Normal;button.Value.Opacity=button.Key==category?1:.65;}scrollSettings.ScrollToTop();if(embedded&&pages.IsLoaded&&dialog is AdaptiveAppBarWindow bar)bar.RefitSettings();}
        var contents=new DockPanel();
        var settingsFrame=new Border{CornerRadius=new CornerRadius(embedded?0:8),BorderThickness=new Thickness(embedded?0:1),BorderBrush=new SolidColorBrush(Color.FromArgb(50,128,128,128)),Background=Brushes.Transparent,Child=contents};
        if(embedded){var monitor=DesktopDisplays.MonitorFromWindow(new System.Windows.Interop.WindowInteropHelper(dialog).Handle,2);var work=DesktopDisplays.Work(monitor);settingsFrame.MaxHeight=Math.Min(540,Math.Max(220,(work.Bottom-work.Top)/VisualTreeHelper.GetDpi(dialog).DpiScaleY-116));scrollSettings.MaxHeight=Math.Max(100,settingsFrame.MaxHeight-124);}
        var header=new Grid{Margin=new Thickness(32,20,20,12)};
        var title=new TextBlock{Text="Settings · Beta",FontSize=22,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};if(!embedded)title.MouseLeftButtonDown+=(_,_)=>dialog.DragMove();header.Children.Add(title);
        var close=new Button{Content=new TextBlock{Text="\uE8BB",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=14},Width=36,Height=36,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right,ToolTip="Close settings"};StartMenuWindow.Borderless(close);close.Click+=(_,_)=>dismiss();System.Windows.Automation.AutomationProperties.SetName(close,"Close settings");header.Children.Add(close);
        DockPanel.SetDock(header,Dock.Top);DockPanel.SetDock(navigation,Dock.Top);contents.Children.Add(header);contents.Children.Add(navigation);contents.Children.Add(scrollSettings);
        var themeTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        bool? lastDark=null;bool lastHighContrast=SystemParameters.HighContrast,lastTransparency=NativeBackdrop.TransparencyAllowed();
        void ApplyTheme(){bool dark=Appearance.IsDark(prefs.Theme);if(lastDark!=dark||lastHighContrast!=SystemParameters.HighContrast){lastDark=dark;lastHighContrast=SystemParameters.HighContrast;if(!embedded)Appearance.Apply(dialog,dark);}lastTransparency=NativeBackdrop.TransparencyAllowed();if(!embedded)Appearance.ApplySurface(dialog,settingsFrame,dark,prefs.StartTransparency,prefs.MenuBlur);UpdateDockBackground();appBars?.Refresh();}
        if(!embedded)dialog.SourceInitialized+=(_,_)=>{lastDark=null;ApplyTheme();};
        themeTimer.Tick+=(_,_)=>{if(lastDark!=Appearance.IsDark(prefs.Theme)||lastHighContrast!=SystemParameters.HighContrast||lastTransparency!=NativeBackdrop.TransparencyAllowed())ApplyTheme();};
        void Toggle(string label,bool value,Action<bool> set){var c=new CheckBox{Content=label,IsChecked=value,Margin=new Thickness(0,6,0,6)};c.Click+=(_,_)=>set(c.IsChecked==true);panel.Children.Add(c);}
        void Slider(string label,double value,double min,double max,Action<double> set){var t=new TextBlock();var s=new Slider{Minimum=min,Maximum=max,Value=Math.Round(value),TickFrequency=1,IsSnapToTickEnabled=true,SmallChange=1,LargeChange=5,Margin=new Thickness(0,5,0,12)};void Update(){t.Text=$"{label}: {s.Value:0}";set(Math.Round(s.Value));}s.ValueChanged+=(_,_)=>Update();Update();panel.Children.Add(t);panel.Children.Add(s);}
        void Section(string label)
        {
            string category=label switch{"Appearance & layout" or "Dock details"=>"Dock","Adaptive AppBar · Beta"=>"AppBar","Animations"=>"Motion","Applications & Windows"=>"Windows",_=>label};
            if(!groups.TryGetValue(category,out var group))
            {
                group=new StackPanel{Margin=new Thickness(32,4,32,24)};groups.Add(category,group);pages.Children.Add(group);
                group.Children.Add(new TextBlock{Text=category,FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,14)});
                var nav=new Button{Content=category,Padding=new Thickness(9,6,9,6),Margin=new Thickness(0,0,5,5)};nav.Click+=(_,_)=>SelectCategory(category);navigation.Children.Add(nav);navButtons.Add(category,nav);
            }
            panel=group;
        }
        Section("Appearance & layout");
        var edges=new ComboBox{ItemsSource=new[]{"Bottom","Top","Left"},SelectedIndex=(int)prefs.Edge,Margin=new Thickness(0,0,0,12)};
        var alignments=new ComboBox{Margin=new Thickness(0,0,0,16)};
        void UpdateAlignments(){alignments.ItemsSource=prefs.Edge==DockEdge.Left?new[]{"Top","Centre","Bottom"}:new[]{"Left","Centre","Right"};alignments.SelectedIndex=(int)prefs.Alignment;}
        UpdateAlignments();
        edges.SelectionChanged+=(_,_)=>{if(prefs.AppBarEnabled&&edges.SelectedIndex==(int)DockEdge.Top){edges.SelectedIndex=(int)DockEdge.Bottom;return;}prefs.Edge=(DockEdge)edges.SelectedIndex;UpdateAlignments();Refresh(true);Position();if(!embedded)dialog.Dispatcher.BeginInvoke(new Action(()=>{dialog.Activate();if(NativeBackdrop.TransparencyAllowed())CustomBackdrop.Apply(dialog,prefs.MenuBlur);}),DispatcherPriority.ContextIdle);};
        alignments.SelectionChanged+=(_,_)=>{if(alignments.SelectedIndex>=0){prefs.Alignment=(DockAlignment)alignments.SelectedIndex;Position();}};
        panel.Children.Add(new TextBlock{Text="Dock position",Margin=new Thickness(0,0,0,6)});panel.Children.Add(edges);
        panel.Children.Add(new TextBlock{Text="Alignment",Margin=new Thickness(0,0,0,6)});panel.Children.Add(alignments);
        Section("Appearance");
        panel.Children.Add(new TextBlock{Text="Shared colour theme",Margin=new Thickness(0,0,0,6)});
        var themes=new ComboBox{ItemsSource=new[]{"System","Light","Dark"},SelectedIndex=prefs.Theme,Margin=new Thickness(0,0,0,16)};
        themes.SelectionChanged+=(_,_)=>{prefs.Theme=themes.SelectedIndex;ApplyTheme();};panel.Children.Add(themes);
        Section("Dock");Slider("Icon size (px)",prefs.IconSize,16,64,v=>{prefs.IconSize=Math.Round(v);Refresh(true);});
        Toggle("macOS-style dock background",prefs.DockBackground,v=>{prefs.DockBackground=v;Refresh(true);Position();});
        panel.Children.Add(new TextBlock{Text="Background colour, transparency and blur are independent in Appearance.",Opacity=.65,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        Toggle("Reserve space for dock",prefs.ReserveDockSpace,v=>{prefs.ReserveDockSpace=v;Position();});
        Section("Appearance");
        string? AppearanceMonitor()=>DesktopDisplays.All().FirstOrDefault(display=>display.Handle==DesktopDisplays.MonitorFromWindow(new System.Windows.Interop.WindowInteropHelper(embedded?dialog:this).Handle,2))?.Name;
        void BackgroundChanged(){Save();ApplyTheme();}
        Toggle("Link Dock and AppBar blur and transparency",prefs.LinkBackgroundEffects,v=>{prefs.LinkBackgroundEffects=v;prefs.SynchroniseBackgrounds(true);BackgroundChanged();});
        Toggle("Link Dock and AppBar background colours",prefs.LinkBackgroundColours,v=>{prefs.LinkBackgroundColours=v;prefs.SynchroniseBackgrounds(true);BackgroundChanged();});
        panel.Children.Add(new TextBlock{Text="Enabling a link uses the AppBar’s current values. Then changes from either editor update both. Unlinking keeps the current values.",TextWrapping=TextWrapping.Wrap,Opacity=.7,Margin=new Thickness(0,0,0,10)});
        panel.Children.Add(BackgroundSettings.Create(prefs,false,AppearanceMonitor,BackgroundChanged));
        panel.Children.Add(BackgroundSettings.Create(prefs,true,AppearanceMonitor,BackgroundChanged));
        Slider("Start / standalone menu transparency (%)",prefs.StartTransparency*100,0,100,v=>{prefs.StartTransparency=v/100;startMenu?.UpdateAppearance();ApplyTheme();});
        Slider("Menu blur (%)",prefs.MenuBlur/60*100,0,100,v=>{prefs.MenuBlur=v*.6;startMenu?.UpdateAppearance();ApplyTheme();});
        Section("Dock");Toggle("Show system controls",prefs.ShowSystemControls,v=>{prefs.ShowSystemControls=v;tray.IsOpen=false;Refresh(true);});
        Toggle("Show Task View",prefs.ShowTaskView,v=>{prefs.ShowTaskView=v;Refresh(true);});
        Toggle("Show Desktop",prefs.ShowDesktop,v=>{prefs.ShowDesktop=v;Refresh(true);});
        Toggle("Use Windows taskbar pins",prefs.FollowWindowsTaskbarPins,v=>{prefs.FollowWindowsTaskbarPins=v;Refresh(true);});
        var restorePins=new Button{Content="Restore hidden Windows taskbar pins",Margin=new Thickness(0,4,0,8)};restorePins.Click+=(_,_)=>{prefs.HiddenTaskbarPins.Clear();Refresh(true);};panel.Children.Add(restorePins);
        Section("Adaptive AppBar · Beta");
        Toggle("Enable Adaptive AppBar",prefs.AppBarEnabled,v=>{EnableAppBar(v);edges.SelectedIndex=(int)prefs.Edge;UpdateAlignments();});
        panel.Children.Add(new TextBlock{Text="AppBar background colour, transparency and blur: Appearance.",TextWrapping=TextWrapping.Wrap,Opacity=.7,Margin=new Thickness(0,0,0,8)});
        Toggle("Show AppBar on every display",prefs.AppBarAllMonitors,v=>{prefs.AppBarAllMonitors=v;appBars?.Refresh(true);});
        Toggle("Expand media on hover",prefs.AppBarMediaHover,v=>prefs.AppBarMediaHover=v);
        Toggle("Animate AppBar expansion",prefs.AppBarAnimations,v=>prefs.AppBarAnimations=v);
        Slider("AppBar animation (ms)",prefs.AppBarAnimationMilliseconds,0,600,v=>prefs.AppBarAnimationMilliseconds=(int)v);
        Toggle("Show clock in AppBar",prefs.AppBarClock,v=>{prefs.AppBarClock=v;appBars?.Refresh();Refresh(true);});
        Toggle("Show date beside AppBar clock",prefs.AppBarDate,v=>{prefs.AppBarDate=v;appBars?.Refresh();});
        Toggle("Show battery percentage",prefs.AppBarBatteryPercentage,v=>{prefs.AppBarBatteryPercentage=v;appBars?.Refresh();});
        panel.Children.Add(new TextBlock{Text="The battery indicator appears only on devices where Windows reports a battery.",TextWrapping=TextWrapping.Wrap,FontSize=12,Opacity=.65,Margin=new Thickness(0,2,0,10)});
        var clockFormat=new ComboBox{ItemsSource=new[]{"Clock format: System","Clock format: 12-hour","Clock format: 24-hour"},SelectedIndex=prefs.AppBarClockFormat,Margin=new Thickness(0,0,0,12)};
        clockFormat.SelectionChanged+=(_,_)=>{prefs.AppBarClockFormat=clockFormat.SelectedIndex;appBars?.Refresh();};panel.Children.Add(clockFormat);
        Section("Windows");Toggle("Manage compatible maximised title bars (experimental)",prefs.BorderlessWindows,v=>{prefs.BorderlessWindows=v;appBars?.Borderless.Update();});
        panel.Children.Add(new TextBlock{Text="Standard Win32 frames only. Custom title bars, games, fullscreen and protected windows are left unchanged. Original caption styles are saved for recovery.",TextWrapping=TextWrapping.Wrap,FontSize=12,Opacity=.7,Margin=new Thickness(0,3,0,8)});
        panel.Children.Add(new TextBlock{Text="Borderless exclusions (executable names or paths, one per line)",TextWrapping=TextWrapping.Wrap});
        var exclusions=new TextBox{Text=string.Join(Environment.NewLine,prefs.BorderlessExclusions),AcceptsReturn=true,MinHeight=54,MaxHeight=100,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,6,0,8)};
        exclusions.TextChanged+=(_,_)=>{prefs.BorderlessExclusions=exclusions.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList();appBars?.Borderless.Update();};panel.Children.Add(exclusions);
        var recover=new Button{Content="Restore managed window frames now",Margin=new Thickness(0,3,0,8)};
        recover.Click+=(_,_)=>{prefs.BorderlessWindows=false;appBars?.Borderless.Update();MessageBox.Show(dialog,BorderlessManager.Recover(),"New Start recovery");};panel.Children.Add(recover);
        Section("Animations");
        Toggle("Hover magnification",prefs.Hover,v=>prefs.Hover=v);
        Slider("Hover magnification (%)",prefs.Magnification*100,100,175,v=>prefs.Magnification=v/100);
        Slider("Hover duration (ms)",prefs.HoverMilliseconds,0,1000,v=>prefs.HoverMilliseconds=v);
        Toggle("Spring launch bounce",prefs.Bounce,v=>prefs.Bounce=v);
        Slider("Bounce height",prefs.BounceHeight,0,30,v=>prefs.BounceHeight=v);
        Slider("Bounce duration (ms)",prefs.BounceMilliseconds,100,2000,v=>prefs.BounceMilliseconds=v);
        Section("Dock details");
        Toggle("White running indicators",prefs.Indicators,v=>{prefs.Indicators=v;Refresh(true);});
        var clocks=new ComboBox{ItemsSource=new[]{"Clock hidden","Clock in panel","Clock always visible"},SelectedIndex=prefs.Clock};clocks.SelectionChanged+=(_,_)=>{prefs.Clock=clocks.SelectedIndex;Refresh(true);};panel.Children.Add(clocks);
        Section("Applications & Windows");
        Toggle("Suppress Windows taskbar pointer reveal",prefs.BlockTaskbarEdgeReveal,v=>{prefs.BlockTaskbarEdgeReveal=v;taskbarSuppression?.Refresh();Save();if(taskbarSuppression?.Error is {} error)MessageBox.Show(dialog,error,"New Start");});
        panel.Children.Add(new TextBlock{Text="Keeps the Windows taskbar hidden until you press a Windows key. Official Start and taskbar access remain available; suppression resumes after you leave them.",TextWrapping=TextWrapping.Wrap,Opacity=.7,FontSize=12,Margin=new Thickness(0,3,0,8)});
        panel.Children.Add(new TextBlock{Text=$"Windows taskbar auto-hide: {(Native.AutoHide?"enabled":"disabled")}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        Section("Dock");var pin=new Button{Content="Pin an application…",Margin=new Thickness(0,4,0,4)};pin.Click+=(_,_)=>{var f=new OpenFileDialog{Filter="Applications and shortcuts|*.exe;*.lnk;*.appref-ms"};if(f.ShowDialog(dialog)==true){dockPins.Place(f.FileName,dockPins.All().Count);Save();Refresh(true);}};panel.Children.Add(pin);
        Section("Windows");
        var native=new Button{Content="Windows taskbar preferences",Margin=new Thickness(0,4,0,4)};native.Click+=(_,_)=>Launch("ms-settings:taskbar");panel.Children.Add(native);
        var exit=new Button{Content="Exit dock — keep Windows taskbar",Margin=new Thickness(0,4,0,4)};exit.Click+=(_,_)=>{dismiss();Close();};panel.Children.Add(exit);
        navigation.Children.Clear();foreach(string category in new[]{"Dock","AppBar","Appearance","Motion","Windows"})navigation.Children.Add(navButtons[category]);
        SelectCategory(groups.ContainsKey(settingsCategory)?settingsCategory:"Dock");ApplyTheme();if(!embedded)themeTimer.Start();
        release=()=>{themeTimer.Stop();Save();Refresh(true);};return settingsFrame;
    }
}


