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
public sealed class DockWindow : Window
{
    readonly Preferences prefs=Preferences.Load();
    readonly StackPanel row=new(){Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Bottom};
    readonly Popup tray=new(){AllowsTransparency=true,StaysOpen=false,Placement=PlacementMode.Top};
    readonly TextBlock clock=new(){Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10)};
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    List<Native.TaskWindow> windows=new(); string signature="";
    StartMenuWindow? startMenu;
    DockReservation? reservation;
    readonly DockPins dockPins;
    readonly Grid dockSurface=new();
    readonly Border blankSurface=new(){Background=new SolidColorBrush(Color.FromArgb(1,0,0,0)),VerticalAlignment=VerticalAlignment.Bottom};
    Button? startButton;
    readonly List<(string Path,Button Button)> pinButtons=new();
    bool dragging;DateTime suppressClickUntil;
    bool positionQueued;
    [StructLayout(LayoutKind.Sequential)]struct CursorPoint{public int X,Y;}
    [DllImport("user32.dll")]static extern bool GetCursorPos(out CursorPoint point);
    DateTime startClosed;
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
        Title="New Start"; WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false; Topmost=true; ShowActivated=false;
        SizeToContent=SizeToContent.Width; Height=90;
        dockSurface.Children.Add(blankSurface);dockSurface.Children.Add(row);Content=dockSurface;
        var barMenu=ThemedMenus.Create(prefs);
        Add(barMenu,"Task Manager",()=>Launch("taskmgr.exe"));
        Add(barMenu,"Taskbar settings",()=>Launch("ms-settings:taskbar"));
        barMenu.Items.Add(new Separator());Add(barMenu,"New Start settings",Settings);
        dockSurface.ContextMenu=barMenu;
        AllowDrop=true;DragOver+=DockDragOver;Drop+=DockDrop;
        Loaded+=(_,_)=>{
            TaskbarPins.Refresh();
            RecycleBinIcon.Changed+=RecycleChanged;RecycleBinIcon.Refresh();
            Refresh(); Position();
            reservation=new DockReservation(this);reservation.Changed+=Position;
            Position();timer.Start();
            startMenu=new StartMenuWindow(prefs){Owner=this};
            startMenu.Dismissed+=()=>startClosed=DateTime.UtcNow;
            startMenu.Prepare();
        };
        SizeChanged+=(_,_)=>Position();
        // Windows can move a window after an AppBar work-area change. Restore
        // the edge-relative position after that notification has settled.
        LocationChanged+=(_,_)=>QueuePosition();
        timer.Tick+=(_,_)=>{ Refresh(); Position(); clock.Text=DateTime.Now.ToString("t"); _=StartCatalog.Refresh();RecycleBinIcon.Refresh();TaskbarPins.Refresh(); };
        Closed+=(_,_)=>{ timer.Stop(); tray.IsOpen=false; startMenu?.Close();RecycleBinIcon.Changed-=RecycleChanged;reservation?.Dispose(); };
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&!dragging){tray.IsOpen=false;startMenu?.Dismiss();e.Handled=true;}};
    }
    void RecycleChanged()=>Dispatcher.BeginInvoke(new Action(()=>recycleImage.Source=RecycleBinIcon.Current));
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
                startMenu.Dismiss();
            }
        }
        finally{prefs.Edge=originalEdge;prefs.Alignment=originalAlignment;prefs.ReserveDockSpace=originalReserve;Refresh(true);UpdateLayout();Position();}
        File.WriteAllText(System.IO.Path.Combine(Preferences.Folder,"location-verification.json"),System.Text.Json.JsonSerializer.Serialize(checks,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(System.IO.Path.Combine(Preferences.Folder,"location-clearance-verification.json"),System.Text.Json.JsonSerializer.Serialize(clearances));
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
        double dockThickness=4+iconButtonHeight/2+prefs.IconSize/2;
        double reservedThickness=dockThickness+DockGeometry.WindowClearance;
        bool vertical=prefs.Edge==DockEdge.Left;
        double hitThickness=dockThickness-4+2;
        blankSurface.Height=vertical?double.NaN:hitThickness;blankSurface.Width=vertical?hitThickness:double.NaN;
        blankSurface.VerticalAlignment=prefs.Edge==DockEdge.Top?VerticalAlignment.Top:vertical?VerticalAlignment.Stretch:VerticalAlignment.Bottom;
        blankSurface.HorizontalAlignment=vertical?HorizontalAlignment.Left:HorizontalAlignment.Stretch;
        reservation?.Update(prefs.ReserveDockSpace,reservedThickness,prefs.Edge);
        Rect area;
        if(reservation is {Enabled:true} bar&&bar.Bounds.Bottom>bar.Bounds.Top)
        {
            var dpi=VisualTreeHelper.GetDpi(this);var bounds=bar.Bounds;
            area=new Rect(bounds.Left/dpi.DpiScaleX,bounds.Top/dpi.DpiScaleY,(bounds.Right-bounds.Left)/dpi.DpiScaleX,(bounds.Bottom-bounds.Top)/dpi.DpiScaleY);
        }
        else area=SystemParameters.WorkArea;
        var origin=DockGeometry.DockOrigin(area,new Size(ActualWidth,ActualHeight),prefs.Edge,prefs.Alignment);
        Left=origin.X;Top=origin.Y;
        if(startMenu?.IsVisible==true)startMenu.Reposition();
    }
    internal Rect StartAnchor
    {
        get{if(startButton==null)return new Rect(Left,Top,prefs.IconSize,prefs.IconSize);var point=startButton.TranslatePoint(new Point((startButton.ActualWidth-prefs.IconSize)/2,(startButton.ActualHeight-prefs.IconSize)/2),this);return new Rect(Left+point.X,Top+point.Y,prefs.IconSize,prefs.IconSize);}
    }
    Button MakeIcon(object content,string name,Action<Button> action)
    {
        var visual=new Grid{Width=prefs.IconSize,Height=prefs.IconSize,Background=new SolidColorBrush(Color.FromArgb(1,0,0,0)),RenderTransformOrigin=new Point(.5,.5)};
        var scale=new ScaleTransform(1,1); var translate=new TranslateTransform();
        visual.RenderTransform=new TransformGroup{Children=new TransformCollection{scale,translate}};
        if(content is UIElement element) visual.Children.Add(new Viewbox{Child=element,Stretch=Stretch.Uniform});
        else visual.Children.Add(new Viewbox
        {
            Stretch=Stretch.Uniform,
            Child=new Grid
            {
                Width=36,Height=36,
                Children={new TextBlock{Text=content.ToString(),FontSize=25,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}}
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
        if(dragging||(!force&&row.Children.OfType<Button>().Any(b=>b.ContextMenu?.IsOpen==true)))return;
        windows=Native.Windows();
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
            startMenu?.OpenMenu();
        });row.Children.Add(startButton);
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
            if(tasks.Count>0&&prefs.Indicators) cell.Children.Add(new Ellipse{Width=4,Height=4,Fill=Brushes.White,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,-9)});
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
        if(prefs.ShowSystemControls){var expand=MakeIcon(controlsIcon,"System controls",b=>{BuildTray();tray.Placement=prefs.Edge switch{DockEdge.Top=>PlacementMode.Bottom,DockEdge.Left=>PlacementMode.Right,_=>PlacementMode.Top};tray.PlacementTarget=b;tray.IsOpen=!tray.IsOpen;});row.Children.Add(expand);}
        if(prefs.Clock==2){clock.MaxWidth=vertical?prefs.IconSize*1.75+12:double.PositiveInfinity;clock.TextWrapping=vertical?TextWrapping.Wrap:TextWrapping.NoWrap;clock.Margin=vertical?new Thickness(2,8,2,8):new Thickness(10);row.Children.Add(clock);}
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
        var recycleMenu=ThemedMenus.Create(prefs);Add(recycleMenu,"Open Recycle Bin",()=>Launch("shell:RecycleBinFolder"));recycle.ContextMenu=recycleMenu;row.Children.Add(recycle);
    }
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
    void Save(){try{prefs.Save();}catch(Exception e){MessageBox.Show("Could not save settings: "+e.Message);}}
    static void Add(ContextMenu menu,string title,Action action){var item=new MenuItem{Header=title};item.Click+=(_,_)=>action();menu.Items.Add(item);}
    static bool Launch(string target){try{Process.Start(new ProcessStartInfo(target){UseShellExecute=true});return true;}catch(Exception e){MessageBox.Show(e.Message,"Could not open");return false;}}
    static void Activate(Native.TaskWindow w){if(Native.GetForegroundWindow()==w.Handle){Native.ShowWindowAsync(w.Handle,6);return;}if(Native.IsIconic(w.Handle))Native.ShowWindowAsync(w.Handle,9);if(!Native.SetForegroundWindow(w.Handle))MessageBox.Show("Windows declined window activation. Use Alt+Tab to switch to this window.");}
    void BuildTray()
    {
        var panel=new StackPanel{Margin=new Thickness(14)};
        panel.Children.Add(new TextBlock{Text="System controls",FontSize=16,Foreground=Brushes.White});
        panel.Children.Add(new TextBlock{Text="App tray icons remain in Windows Explorer.",Foreground=Brushes.White,Margin=new Thickness(0,8,0,8)});
        foreach(var pair in new[]{("Sound","ms-settings:sound"),("Network","ms-settings:network-status"),("Bluetooth","ms-settings:bluetooth"),("Windows taskbar","ms-settings:taskbar")}) {var b=new Button{Content=pair.Item1,Margin=new Thickness(0,3,0,3)};b.Click+=(_,_)=>Launch(pair.Item2);panel.Children.Add(b);}
        if(prefs.Clock==1)panel.Children.Add(new TextBlock{Text=DateTime.Now.ToString("f"),Foreground=Brushes.White,Margin=new Thickness(0,10,0,0)});
        tray.Child=new Border{Background=new SolidColorBrush(Color.FromArgb(235,25,25,25)),CornerRadius=new CornerRadius(8),Child=panel};
    }
    internal void Settings()
    {
        var panel=new StackPanel{Margin=new Thickness(32,8,32,28)};
        var contents=new DockPanel();
        var settingsFrame=new Border{CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(50,128,128,128)),Child=contents};
        var dialog=new Window{Title="New Start settings (Beta)",WindowStyle=WindowStyle.None,AllowsTransparency=true,Width=500,Height=Math.Min(740,SystemParameters.WorkArea.Height-32),MinWidth=420,MinHeight=400,Content=settingsFrame,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterScreen,ResizeMode=ResizeMode.NoResize};
        NativeBackdrop.Configure(dialog);
        var header=new Grid{Margin=new Thickness(32,20,20,12)};
        var title=new TextBlock{Text="Settings · Beta",FontSize=22,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};title.MouseLeftButtonDown+=(_,_)=>dialog.DragMove();header.Children.Add(title);
        panel.Children.Add(new TextBlock{Text="New Start · 0.1.0-beta.1",Opacity=.65,Margin=new Thickness(0,0,0,8)});
        var close=new Button{Content=new TextBlock{Text="\uE8BB",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=14},Width=36,Height=36,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right,ToolTip="Close settings"};StartMenuWindow.Borderless(close);close.Click+=(_,_)=>dialog.Close();System.Windows.Automation.AutomationProperties.SetName(close,"Close settings");header.Children.Add(close);
        DockPanel.SetDock(header,Dock.Top);contents.Children.Add(header);contents.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var themeTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        bool? lastDark=null;bool lastHighContrast=SystemParameters.HighContrast,lastTransparency=NativeBackdrop.TransparencyAllowed();
        void ApplyTheme(){bool dark=Appearance.IsDark(prefs.Theme);if(lastDark!=dark||lastHighContrast!=SystemParameters.HighContrast){lastDark=dark;lastHighContrast=SystemParameters.HighContrast;Appearance.Apply(dialog,dark);}lastTransparency=NativeBackdrop.TransparencyAllowed();Appearance.ApplySurface(dialog,settingsFrame,dark,prefs.StartTransparency,prefs.MenuBlur);}
        dialog.SourceInitialized+=(_,_)=>{lastDark=null;ApplyTheme();};
        themeTimer.Tick+=(_,_)=>{if(lastDark!=Appearance.IsDark(prefs.Theme)||lastHighContrast!=SystemParameters.HighContrast||lastTransparency!=NativeBackdrop.TransparencyAllowed())ApplyTheme();};
        void Toggle(string label,bool value,Action<bool> set){var c=new CheckBox{Content=label,IsChecked=value,Margin=new Thickness(0,6,0,6)};c.Click+=(_,_)=>set(c.IsChecked==true);panel.Children.Add(c);}
        void Slider(string label,double value,double min,double max,Action<double> set){var t=new TextBlock();var s=new Slider{Minimum=min,Maximum=max,Value=Math.Round(value),TickFrequency=1,IsSnapToTickEnabled=true,SmallChange=1,LargeChange=5,Margin=new Thickness(0,5,0,12)};void Update(){t.Text=$"{label}: {s.Value:0}";set(Math.Round(s.Value));}s.ValueChanged+=(_,_)=>Update();Update();panel.Children.Add(t);panel.Children.Add(s);}
        void Section(string label){panel.Children.Add(new TextBlock{Text=label,FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,18,0,12)});}
        Section("Appearance & layout");
        var edges=new ComboBox{ItemsSource=new[]{"Bottom","Top","Left"},SelectedIndex=(int)prefs.Edge,Margin=new Thickness(0,0,0,12)};
        var alignments=new ComboBox{Margin=new Thickness(0,0,0,16)};
        void UpdateAlignments(){alignments.ItemsSource=prefs.Edge==DockEdge.Left?new[]{"Top","Centre","Bottom"}:new[]{"Left","Centre","Right"};alignments.SelectedIndex=(int)prefs.Alignment;}
        UpdateAlignments();
        edges.SelectionChanged+=(_,_)=>{prefs.Edge=(DockEdge)edges.SelectedIndex;UpdateAlignments();Refresh(true);Position();dialog.Dispatcher.BeginInvoke(new Action(()=>{dialog.Activate();if(NativeBackdrop.TransparencyAllowed())CustomBackdrop.Apply(dialog,prefs.MenuBlur);}),DispatcherPriority.ContextIdle);};
        alignments.SelectionChanged+=(_,_)=>{if(alignments.SelectedIndex>=0){prefs.Alignment=(DockAlignment)alignments.SelectedIndex;Position();}};
        panel.Children.Add(new TextBlock{Text="Dock position",Margin=new Thickness(0,0,0,6)});panel.Children.Add(edges);
        panel.Children.Add(new TextBlock{Text="Alignment",Margin=new Thickness(0,0,0,6)});panel.Children.Add(alignments);
        var themes=new ComboBox{ItemsSource=new[]{"System","Light","Dark"},SelectedIndex=prefs.Theme,Margin=new Thickness(0,0,0,16)};
        themes.SelectionChanged+=(_,_)=>{prefs.Theme=themes.SelectedIndex;ApplyTheme();};panel.Children.Add(themes);
        Slider("Icon size (px)",prefs.IconSize,16,64,v=>{prefs.IconSize=Math.Round(v);Refresh(true);});
        Toggle("Reserve space for dock",prefs.ReserveDockSpace,v=>{prefs.ReserveDockSpace=v;Position();});
        Slider("Menu transparency (%)",prefs.StartTransparency*100,0,100,v=>{prefs.StartTransparency=v/100;startMenu?.UpdateAppearance();ApplyTheme();});
        Slider("Menu blur (%)",prefs.MenuBlur/60*100,0,100,v=>{prefs.MenuBlur=v*.6;startMenu?.UpdateAppearance();ApplyTheme();});
        Toggle("Show system controls",prefs.ShowSystemControls,v=>{prefs.ShowSystemControls=v;tray.IsOpen=false;Refresh(true);});
        Toggle("Show Task View",prefs.ShowTaskView,v=>{prefs.ShowTaskView=v;Refresh(true);});
        Toggle("Show Desktop",prefs.ShowDesktop,v=>{prefs.ShowDesktop=v;Refresh(true);});
        Toggle("Use Windows taskbar pins",prefs.FollowWindowsTaskbarPins,v=>{prefs.FollowWindowsTaskbarPins=v;Refresh(true);});
        var restorePins=new Button{Content="Restore hidden Windows taskbar pins",Margin=new Thickness(0,4,0,8)};restorePins.Click+=(_,_)=>{prefs.HiddenTaskbarPins.Clear();Refresh(true);};panel.Children.Add(restorePins);
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
        panel.Children.Add(new TextBlock{Text=$"Windows taskbar auto-hide: {(Native.AutoHide?"enabled":"disabled")}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        var pin=new Button{Content="Pin an application…",Margin=new Thickness(0,4,0,4)};pin.Click+=(_,_)=>{var f=new OpenFileDialog{Filter="Applications and shortcuts|*.exe;*.lnk;*.appref-ms"};if(f.ShowDialog(dialog)==true){dockPins.Place(f.FileName,dockPins.All().Count);Save();Refresh(true);}};panel.Children.Add(pin);
        var native=new Button{Content="Windows taskbar preferences",Margin=new Thickness(0,4,0,4)};native.Click+=(_,_)=>Launch("ms-settings:taskbar");panel.Children.Add(native);
        var exit=new Button{Content="Exit dock — keep Windows taskbar",Margin=new Thickness(0,4,0,4)};exit.Click+=(_,_)=>{dialog.Close();Close();};panel.Children.Add(exit);
        ApplyTheme();themeTimer.Start();
        dialog.Closed+=(_,_)=>{themeTimer.Stop();Save();Refresh(true);};dialog.ShowDialog();
    }
}


