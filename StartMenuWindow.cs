using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Shapes;
using System.Collections.Generic;

namespace NewStart;
internal sealed class StartMenuWindow : Window
{
    readonly Preferences prefs;
    readonly Border frame;
    readonly Grid layout=new();
    readonly StackPanel body=new();
    readonly TextBox search=new(){FontSize=14,Padding=new Thickness(14,10,14,10),Margin=new Thickness(32,28,32,18)};
    readonly TextBlock heading=new(){Text="Pinned",FontSize=15,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};
    readonly Button all=new(){Content="All apps  ›",Padding=new Thickness(12,4,12,4),HorizontalAlignment=HorizontalAlignment.Right};
    readonly TextBlock status=new(){Text="Loading apps…",Margin=new Thickness(32,6,32,10),TextWrapping=TextWrapping.Wrap,FontSize=12};
    readonly ScrollViewer scroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    readonly StackPanel sectionPips=new(){VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,5,0)};
    readonly List<UniformGrid> pinnedSections=new();
    readonly List<Ellipse> pipDots=new();
    bool pinnedView;double sectionHeight=420;DateTime lastSectionWheel;
    StartData? data;bool allApps;int page;bool modalOpen;
    bool? lastDark;
    bool lastHighContrast;
    bool draggingApp;bool appContextOpen;DateTime suppressLaunchUntil;
    Action? embeddedDismiss;
    readonly Button powerButton;
    readonly FrameworkElement powerVisual;
    internal bool PowerIconFits
    {
        get
        {
            var bounds=powerVisual.TransformToAncestor(powerButton).TransformBounds(new Rect(0,0,powerVisual.ActualWidth,powerVisual.ActualHeight));
            return new Rect(8,8,powerButton.ActualWidth-16,powerButton.ActualHeight-16).Contains(bounds);
        }
    }
    internal event Action? Dismissed;
    internal DockWindow? DockHost {get;set;}
    internal bool KeepOpenForTesting {get;set;}
    internal void Prepare()
    {
        new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        Measure(new Size(Width,Height));Arrange(new Rect(0,0,Width,Height));UpdateLayout();
    }
    internal void OpenMenu()
    {
        Reposition();
        UpdateAppearance();Show();Activate();search.Focus();_=StartCatalog.Refresh();
        if(AppBarPlacement!=null)Dispatcher.BeginInvoke(new Action(Reposition));
    }
    internal (Native.Rect Anchor,DesktopDisplay Display)? AppBarPlacement {get;set;}
    internal void Reposition()
    {
        if(AppBarPlacement is {} placement)
        {
            var handle=new System.Windows.Interop.WindowInteropHelper(this).Handle;
            DesktopDisplays.SetWindowPos(handle,IntPtr.Zero,placement.Anchor.Left,placement.Anchor.Bottom,0,0,0x0015);
            double scale=Math.Max(1,DesktopDisplays.GetDpiForWindow(handle))/96d;
            var area=DesktopDisplays.Work(placement.Display.Handle);var anchor=placement.Anchor;
            var menu=DockGeometry.MenuBounds(new Rect(area.Left/scale,area.Top/scale,(area.Right-area.Left)/scale,(area.Bottom-area.Top)/scale),new Rect(anchor.Left/scale,anchor.Top/scale,(anchor.Right-anchor.Left)/scale,(anchor.Bottom-anchor.Top)/scale),DockEdge.Top,DockAlignment.Start);
            Width=menu.Width;Height=menu.Height;
            DesktopDisplays.SetWindowPos(handle,IntPtr.Zero,(int)Math.Round(menu.X*scale),(int)Math.Round(menu.Y*scale),(int)Math.Ceiling(menu.Width*scale),(int)Math.Ceiling(menu.Height*scale),0x0014);return;
        }
        var work=SystemParameters.WorkArea;
        Rect bounds=(DockHost??Owner) is DockWindow dock?DockGeometry.MenuBounds(work,dock.StartAnchor,prefs.Edge,prefs.Alignment,new Rect(dock.Left,dock.Top,dock.ActualWidth,dock.ActualHeight)):new Rect(work.Left+(work.Width-Math.Min(640,work.Width-24))/2,work.Top+12,Math.Min(640,work.Width-24),Math.Min(660,work.Height-24));
        Width=bounds.Width;Height=bounds.Height;Left=bounds.Left;Top=bounds.Top;
    }
    internal void Dismiss()
    {
        if(embeddedDismiss!=null){embeddedDismiss();Dismissed?.Invoke();return;}
        if(!IsVisible)return;
        if(Owner==null&&DockHost==null){Close();return;}
        CustomBackdrop.Hide(this);Hide();Dismissed?.Invoke();allApps=false;page=0;search.Clear();data=StartCatalog.Current;Render();
    }
    internal FrameworkElement Embed(double width,double height,Action dismiss,out Action release)
    {
        if(IsVisible)Dismiss();
        var saved=(frame.Width,frame.Height,frame.BorderThickness,frame.CornerRadius,frame.BorderBrush);
        Content=null;embeddedDismiss=dismiss;frame.Width=width;frame.Height=height;frame.BorderThickness=new Thickness(0);frame.CornerRadius=new CornerRadius(0);frame.BorderBrush=Brushes.Transparent;frame.Background=Brushes.Transparent;
        data=StartCatalog.Current;Render();UpdateAppearance();_=StartCatalog.Refresh();
        release=()=>{
            embeddedDismiss=null;(frame.Width,frame.Height,frame.BorderThickness,frame.CornerRadius,frame.BorderBrush)=saved;
            allApps=false;page=0;search.Clear();data=StartCatalog.Current;Render();Content=frame;
        };
        return frame;
    }
    internal bool FocusSearch()=>search.Focus();
    Window ModalOwner=>Window.GetWindow(frame)??this;
    internal StartMenuWindow(Preferences preferences,bool sectionPreview=false)
    {
        prefs=preferences;Title="New Start (Beta)";Width=640;Height=Math.Min(660,SystemParameters.WorkArea.Height-30);
        WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;Topmost=true;Background=Brushes.Transparent;
        NativeBackdrop.Configure(this);
        frame=new Border{CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(50,128,128,128)),Child=layout};
        Content=frame;
        for(int i=0;i<5;i++)layout.RowDefinitions.Add(new RowDefinition{Height=i==2?new GridLength(1,GridUnitType.Star):GridLength.Auto});
        var searchBox=new Grid();searchBox.Children.Add(search);
        var placeholder=new TextBlock{Text="⌕   Search for apps",IsHitTestVisible=false,Foreground=Brushes.Gray,Margin=new Thickness(48,40,48,0),VerticalAlignment=VerticalAlignment.Top};
        searchBox.Children.Add(placeholder);layout.Children.Add(searchBox);
        System.Windows.Automation.AutomationProperties.SetName(search,"Search apps");search.ToolTip="Search apps";
        var editMenu=ThemedMenus.Create(prefs);
        foreach(var edit in new[]{("Cut",ApplicationCommands.Cut,"Ctrl+X"),("Copy",ApplicationCommands.Copy,"Ctrl+C"),("Paste",ApplicationCommands.Paste,"Ctrl+V"),("Select all",ApplicationCommands.SelectAll,"Ctrl+A")})
            editMenu.Items.Add(new MenuItem{Header=edit.Item1,Command=edit.Item2,CommandTarget=search,InputGestureText=edit.Item3});
        search.ContextMenu=editMenu;
        var header=new Grid{Margin=new Thickness(48,0,48,14)};header.Children.Add(heading);header.Children.Add(all);Put(header,1);
        scroll.Content=body;var appRegion=new Grid();appRegion.Children.Add(scroll);appRegion.Children.Add(sectionPips);Put(appRegion,2);Put(status,3);
        scroll.ScrollChanged+=(_,e)=>{
            if(!pinnedView)return;
            if(e.ViewportHeightChange!=0)SizeSections();
            UpdatePips((int)Math.Round(scroll.VerticalOffset/sectionHeight));
        };
        scroll.PreviewMouseWheel+=(_,e)=>{
            if(!pinnedView)return;e.Handled=true;
            if(e.Delta==0||(DateTime.UtcNow-lastSectionWheel).TotalMilliseconds<180)return;
            lastSectionWheel=DateTime.UtcNow;GoSection(page+(e.Delta<0?1:-1));
        };
        scroll.RequestBringIntoView+=(_,e)=>{
            if(!pinnedView)return;e.Handled=true;
            if(e.TargetObject is DependencyObject target)
                for(int i=0;i<pinnedSections.Count;i++)if(pinnedSections[i].IsAncestorOf(target)){GoSection(i);break;}
        };
        var footer=new Grid{Margin=new Thickness(32,15,32,20)};
        footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        footer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});footer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var accountPanel=new StackPanel{Orientation=Orientation.Horizontal};
        var avatar=new Image{Width=32,Height=32,Stretch=Stretch.UniformToFill,Clip=new EllipseGeometry(new Point(16,16),16,16),Margin=new Thickness(0,0,12,0)};
        var genericAvatar=new TextBlock{Text="\uE77B",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=26,Width=32,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};
        var accountName=new TextBlock{FontSize=12,VerticalAlignment=VerticalAlignment.Center,MaxWidth=120,TextTrimming=TextTrimming.CharacterEllipsis};
        accountPanel.Children.Add(avatar);accountPanel.Children.Add(genericAvatar);accountPanel.Children.Add(accountName);
        var user=new Button{Content=accountPanel,HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(8),MinHeight=44};user.Click+=(_,_)=>Launch("ms-settings:yourinfo");
        void ApplyAccount()
        {
            var profile=AccountIdentity.Current;accountName.Text=profile.DisplayName;avatar.Source=profile.Avatar;
            avatar.Visibility=profile.Avatar!=null?Visibility.Visible:Visibility.Collapsed;genericAvatar.Visibility=profile.Avatar==null?Visibility.Visible:Visibility.Collapsed;
            user.ToolTip=profile.DisplayName;System.Windows.Automation.AutomationProperties.SetName(user,profile.DisplayName+" — account");
        }
        ApplyAccount();
        void AccountUpdated()=>Dispatcher.BeginInvoke(new Action(ApplyAccount));
        AccountIdentity.Updated+=AccountUpdated;Closed+=(_,_)=>AccountIdentity.Updated-=AccountUpdated;
        var powerIcon=new System.Windows.Shapes.Path{Data=Geometry.Parse("M 12,2 L 12,12 M 5,5 A 10,10 0 1 0 19,5"),Width=20,Height=20,Stretch=Stretch.Uniform,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        powerIcon.SetBinding(Shape.StrokeProperty,new Binding("Foreground"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Button),1)});
        var powerFrame=new Grid{Width=24,Height=24,ClipToBounds=true};powerFrame.Children.Add(powerIcon);
        // Override the shared button style's padding: the icon has its own fixed inset.
        var power=new Button{Content=powerFrame,Width=44,Height=44,Padding=new Thickness(0),ClipToBounds=true,ToolTip="Power",HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
        powerButton=power;powerVisual=powerFrame;
        Borderless(user);Borderless(power);
        System.Windows.Automation.AutomationProperties.SetName(power,"Power");
        power.Click+=(_,_)=>{var menu=ThemedMenus.Create(prefs);Add(menu,"Lock",()=>{Process.Start(new ProcessStartInfo("rundll32.exe","user32.dll,LockWorkStation"){UseShellExecute=true});Dismiss();});Add(menu,"Restart",()=>Power("restart","/r /t 0"));Add(menu,"Shut down",()=>Power("shut down","/s /t 0"));menu.PlacementTarget=power;menu.IsOpen=true;};
        var shortcuts=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        foreach(var shortcut in StartShortcuts.Items)
        {
            // Centre the visible glyph geometry, rather than the font's line box/baseline.
            var outline=new FormattedText(shortcut.Glyph,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe Fluent Icons"),16,Brushes.White,1).BuildGeometry(new Point());
            var glyph=new System.Windows.Shapes.Path{Data=outline,Stretch=Stretch.Uniform,Width=16,Height=16,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            glyph.SetBinding(Shape.FillProperty,new Binding("Foreground"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Button),1)});
            var iconFrame=new Grid{Width=24,Height=24,ClipToBounds=true};iconFrame.Children.Add(glyph);
            var button=new Button{Content=iconFrame,Width=36,Height=44,Padding=new Thickness(0),ClipToBounds=true,VerticalAlignment=VerticalAlignment.Center,ToolTip=shortcut.Name};
            Borderless(button);System.Windows.Automation.AutomationProperties.SetName(button,shortcut.Name);
            button.Click+=(_,_)=>{try{Launch(shortcut.Target());}catch(Exception e){modalOpen=true;try{MessageBox.Show(ModalOwner,e.Message,"Could not open "+shortcut.Name);}finally{modalOpen=false;}}};
            shortcuts.Children.Add(button);
        }
        shortcuts.Children.Add(power);Grid.SetColumn(shortcuts,1);footer.Children.Add(shortcuts);footer.Children.Add(user);Put(footer,4);
        SizeChanged+=(_,_)=>{
            bool compact=ActualWidth<600;Grid.SetRow(shortcuts,compact?1:0);Grid.SetColumn(shortcuts,compact?0:1);Grid.SetColumnSpan(shortcuts,compact?2:1);
            shortcuts.HorizontalAlignment=compact?HorizontalAlignment.Center:HorizontalAlignment.Right;
        };
        all.Click+=(_,_)=>{allApps=!allApps;search.Text="";Render();};
        search.TextChanged+=(_,_)=>{placeholder.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;Render();};
        search.KeyDown+=(_,e)=>{if(e.Key==Key.Enter&&data!=null&&!string.IsNullOrWhiteSpace(search.Text)){var match=data.Apps.FirstOrDefault(a=>a.Name.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase));if(match!=null)Launch(match.Target);e.Handled=true;}};
        PreviewKeyDown+=(_,e)=>{
            if(pinnedView&&e.Key is Key.PageDown or Key.PageUp){GoSection(page+(e.Key==Key.PageDown?1:-1));e.Handled=true;return;}
            if(e.Key==Key.Escape){if(search.Text.Length>0)search.Clear();else if(allApps){allApps=false;Render();}else Dismiss();e.Handled=true;}
        };
        Deactivated+=(_,_)=>{if(IsVisible&&!modalOpen&&!appContextOpen&&!sectionPreview&&!KeepOpenForTesting&&!draggingApp)Dismiss();};
        SourceInitialized+=(_,_)=>UpdateAppearance();
        data=StartCatalog.Current;if(sectionPreview&&data!=null)data=data with{Pins=data.Apps.Take(73).ToList()};if(data!=null)Render();
        void CatalogUpdated()=>Dispatcher.BeginInvoke(new Action(()=>{
            if(sectionPreview||draggingApp)return;
            var latest=StartCatalog.Current;
            bool changed=data==null||latest==null||!data.Pins.Select(a=>(a.Name,a.Target)).SequenceEqual(latest.Pins.Select(a=>(a.Name,a.Target)))||!data.Apps.Select(a=>(a.Name,a.Target)).SequenceEqual(latest.Apps.Select(a=>(a.Name,a.Target)))||data.Warning!=latest.Warning;
            data=latest;if(changed)Render();
        }));
        StartCatalog.Updated+=CatalogUpdated;Closed+=(_,_)=>StartCatalog.Updated-=CatalogUpdated;
        Loaded+=(_,_)=>{
            Reposition();
            UpdateAppearance();
            search.Focus();
            // Paint the existing snapshot now. Windows enumeration/export happens off the opening path.
            _=StartCatalog.Refresh();
        };
    }
    internal void UpdateAppearance()
    {
            bool dark=Appearance.IsDark(prefs.Theme);
            if(lastDark!=dark||lastHighContrast!=SystemParameters.HighContrast){Appearance.Apply(this,dark);lastDark=dark;lastHighContrast=SystemParameters.HighContrast;}
        if(embeddedDismiss==null)Appearance.ApplySurface(this,frame,dark,prefs.StartTransparency,prefs.MenuBlur);else frame.Background=Brushes.Transparent;
        search.Background=new SolidColorBrush(dark?Color.FromRgb(46,46,46):Colors.White);search.Foreground=Foreground;
    }
    void Put(UIElement element,int row){Grid.SetRow(element,row);layout.Children.Add(element);}
    internal static void Borderless(Button button)
    {
        button.Background=Brushes.Transparent;button.BorderThickness=new Thickness(0);button.FocusVisualStyle=null;
        var surface=new FrameworkElementFactory(typeof(Border));surface.SetValue(Border.BackgroundProperty,Brushes.Transparent);surface.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);surface.AppendChild(content);
        var template=new ControlTemplate(typeof(Button)){VisualTree=surface};
        foreach(var property in new[]{Button.IsMouseOverProperty,Button.IsKeyboardFocusedProperty}){var trigger=new Trigger{Property=property,Value=true};trigger.Setters.Add(new Setter(UIElement.OpacityProperty,.7));template.Triggers.Add(trigger);}
        button.Template=template;
    }
    static void Add(ContextMenu menu,string name,Action action){var i=new MenuItem{Header=name};i.Click+=(_,_)=>action();menu.Items.Add(i);}
    void Power(string verb,string arguments){modalOpen=true;try{if(MessageBox.Show(ModalOwner,"Do you want to "+verb+" this PC?","Power",MessageBoxButton.YesNo)==MessageBoxResult.Yes){Process.Start(new ProcessStartInfo("shutdown.exe",arguments){UseShellExecute=false,CreateNoWindow=true});Dismiss();}}finally{modalOpen=false;}}
    void Launch(string target){try{Process.Start(new ProcessStartInfo(target){UseShellExecute=true});Dismiss();}catch(Exception e){modalOpen=true;try{MessageBox.Show(ModalOwner,e.Message,"Could not open app");}finally{modalOpen=false;}}}
    static bool SameApp(string left,string right)=>string.Equals(PinIdentity.Resolve(left),PinIdentity.Resolve(right),StringComparison.OrdinalIgnoreCase);
    List<StartApp> VisiblePins()
    {
        if(data==null)return new();
        return data.Pins.Concat(prefs.StartPins.Select(target=>data.Apps.FirstOrDefault(app=>SameApp(app.Target,target))).OfType<StartApp>())
            .Where(app=>!prefs.HiddenStartPins.Any(target=>SameApp(target,app.Target)))
            .DistinctBy(app=>PinIdentity.Resolve(app.Target),StringComparer.OrdinalIgnoreCase).ToList();
    }
    void SetStartPin(StartApp app,bool pinned)
    {
        prefs.StartPins.RemoveAll(target=>SameApp(target,app.Target));
        prefs.HiddenStartPins.RemoveAll(target=>SameApp(target,app.Target));
        if(pinned){if(data?.Pins.Any(pin=>SameApp(pin.Target,app.Target))!=true)prefs.StartPins.Add(app.Target);}
        else prefs.HiddenStartPins.Add(app.Target);
        prefs.Save();Dispatcher.BeginInvoke(new Action(Render));
    }
    Button AppButton(StartApp app,bool tile)
    {
        var panel=new StackPanel{Orientation=tile?Orientation.Vertical:Orientation.Horizontal};
        var image=new Image{Source=app.Icon??StartCatalog.CachedIcon(app.Target),Width=tile?32:28,Height=tile?32:28,Margin=tile?new Thickness(0,2,0,7):new Thickness(0,0,16,0)};
        panel.Children.Add(image);
        image.Loaded+=async(_,_)=>{image.Source=await StartCatalog.Icon(app.Target);};
        panel.Children.Add(new TextBlock{Text=app.Name,FontSize=12,TextAlignment=tile?TextAlignment.Center:TextAlignment.Left,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,MaxWidth=tile?82:450});
        var b=new Button{Content=panel,ToolTip=app.Name,Height=tile?double.NaN:44,Padding=new Thickness(5),Margin=new Thickness(2),HorizontalContentAlignment=tile?HorizontalAlignment.Center:HorizontalAlignment.Left};
        // Tiles have no permanent border or fill, as in the original Start pinned grid.
        var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.BackgroundProperty,Brushes.Transparent);border.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));border.SetValue(Border.PaddingProperty,new Thickness(5));border.Name="Tile";
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,tile?HorizontalAlignment.Center:HorizontalAlignment.Left);presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(presenter);
        var template=new ControlTemplate(typeof(Button)){VisualTree=border};
        foreach(var property in new[]{Button.IsMouseOverProperty,Button.IsKeyboardFocusedProperty}){var trigger=new Trigger{Property=property,Value=true};trigger.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(28,128,128,128)),"Tile"));template.Triggers.Add(trigger);}b.Template=template;
        System.Windows.Automation.AutomationProperties.SetName(b,app.Name);b.Click+=(_,_)=>{if(DateTime.UtcNow>=suppressLaunchUntil)Launch(app.Target);};
        var context=ThemedMenus.Create(prefs);
        b.ContextMenu=context;
        context.Opened+=(_,_)=>appContextOpen=true;
        context.Closed+=(_,_)=>appContextOpen=false;
        b.ContextMenuOpening+=(_,_)=>{
            context.Items.Clear();
            if((DockHost??Owner) is DockWindow dock)
            {
                bool dockPinned=dock.IsDockPinned(app.Target);
                var pin=new MenuItem{Header=dockPinned?"Already pinned to dock":"Pin to dock",IsEnabled=!dockPinned};
                pin.Click+=(_,_)=>dock.PinFromStart(app.Target);context.Items.Add(pin);
            }
            bool startPinned=VisiblePins().Any(pin=>SameApp(pin.Target,app.Target));
            Add(context,startPinned?"Unpin from Start":"Pin to Start",()=>SetStartPin(app,!startPinned));
        };
        Point origin=default;bool pressed=false;
        b.PreviewMouseLeftButtonDown+=(_,e)=>{origin=e.GetPosition(b);pressed=true;};b.PreviewMouseLeftButtonUp+=(_,_)=>pressed=false;
        b.PreviewMouseMove+=(_,e)=>{
            if(!pressed||draggingApp||e.LeftButton!=MouseButtonState.Pressed)return;
            var point=e.GetPosition(b);if(Math.Abs(point.X-origin.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-origin.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            pressed=false;e.Handled=true;draggingApp=true;DragDropEffects result=DragDropEffects.None;
            try{result=DragDrop.DoDragDrop(b,new DataObject(DockPins.DragFormat,app.Target),DragDropEffects.Copy|DragDropEffects.Move);}
            finally{draggingApp=false;suppressLaunchUntil=DateTime.UtcNow.AddMilliseconds(250);}
            if(result!=DragDropEffects.None)Dismiss();
        };
        return b;
    }
    void Render()
    {
        pinnedView=false;body.Children.Clear();pinnedSections.Clear();pipDots.Clear();sectionPips.Children.Clear();scroll.ScrollToTop();if(data==null)return;
        bool filtering=!string.IsNullOrWhiteSpace(search.Text);
        pinnedView=!allApps&&!filtering;
        scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;
        sectionPips.Visibility=pinnedView?Visibility.Visible:Visibility.Collapsed;
        heading.Text=filtering?"Search results":allApps?"All apps":"Pinned";all.Content=allApps?"‹  Back":"All apps  ›";
        status.Text=data.Warning??"";status.Visibility=status.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
        if(allApps||filtering)
        {
            body.Margin=new Thickness(40,0,40,0);
            var apps=data.Apps.Where(a=>!filtering||a.Name.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase));
            foreach(var group in apps.GroupBy(a=>char.IsLetter(a.Name[0])?a.Name[..1].ToUpper(System.Globalization.CultureInfo.CurrentCulture):"#"))
            {
                var letter=new Button{Content=group.Key,FontSize=15,Width=36,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(4,8,0,4),Padding=new Thickness(4)};
                var anchor=letter;letter.Click+=(_,_)=>JumpLetters();body.Children.Add(anchor);
                foreach(var app in group)body.Children.Add(AppButton(app,false));
            }
            if(body.Children.Count==0)body.Children.Add(new TextBlock{Text="No apps found",Margin=new Thickness(12)});
        }
        else
        {
            const int columns=6,rows=6,pageSize=columns*rows;
            var pins=VisiblePins();
            body.Margin=new Thickness(30,0,30,0);int pages=Math.Max(1,(pins.Count+pageSize-1)/pageSize);page=0;
            sectionPips.Visibility=pages>1?Visibility.Visible:Visibility.Collapsed;
            for(int section=0;section<pages;section++)
            {
                var grid=new UniformGrid{Columns=columns,Rows=rows,Height=sectionHeight};
                foreach(var app in pins.Skip(section*pageSize).Take(pageSize))grid.Children.Add(AppButton(app,true));
                pinnedSections.Add(grid);body.Children.Add(grid);
                var dot=new Ellipse{Width=section==0?8:4,Height=section==0?8:4,Opacity=section==0?1:.55,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};dot.SetResourceReference(Shape.FillProperty,"Text");pipDots.Add(dot);
                int selected=section;var button=new Button{Content=dot,Width=26,Height=28,Padding=new Thickness(0),ToolTip=$"Pinned section {section+1}"};Borderless(button);System.Windows.Automation.AutomationProperties.SetName(button,$"Pinned section {section+1}");button.Click+=(_,_)=>GoSection(selected);sectionPips.Children.Add(button);
            }
            if(pins.Count==0)body.Children.Add(new TextBlock{Text="No pinned apps. Right-click an app in All apps to pin it here.",Margin=new Thickness(18)});
            SizeSections();
        }
    }
    void SizeSections()
    {
        if(!pinnedView)return;
        sectionHeight=Math.Max(1,scroll.ViewportHeight>1?scroll.ViewportHeight:420);
        foreach(var section in pinnedSections)section.Height=sectionHeight;
        GoSection(page);
    }
    void GoSection(int index)
    {
        if(!pinnedView||pinnedSections.Count==0)return;
        page=Math.Clamp(index,0,pinnedSections.Count-1);scroll.ScrollToVerticalOffset(page*sectionHeight);UpdatePips(page);
    }
    void UpdatePips(int index)
    {
        if(pipDots.Count==0)return;page=Math.Clamp(index,0,pipDots.Count-1);
        for(int i=0;i<pipDots.Count;i++){double size=i==page?8:4;pipDots[i].Width=size;pipDots[i].Height=size;pipDots[i].Opacity=i==page?1:.55;}
    }
    void JumpLetters()
    {
        if(data==null)return;
        pinnedView=false;sectionPips.Visibility=Visibility.Collapsed;
        body.Children.Clear();
        var grid=new UniformGrid{Columns=6};
        foreach(var letter in new[]{"#"}.Concat(Enumerable.Range('A',26).Select(c=>((char)c).ToString())))
        {
            var b=new Button{Content=letter,Height=52,Margin=new Thickness(4),IsEnabled=data.Apps.Any(a=>(char.IsLetter(a.Name[0])?a.Name[..1].ToUpper(System.Globalization.CultureInfo.CurrentCulture):"#")==letter)};
            b.Click+=(_,_)=>{Render();var anchor=body.Children.OfType<Button>().FirstOrDefault(a=>a.Content?.ToString()==letter);anchor?.BringIntoView();};grid.Children.Add(b);
        }
        body.Children.Add(grid);
    }
}

