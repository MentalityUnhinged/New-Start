using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NewStart;
public sealed partial class DockWindow
{
    Border? insertionMarker;DockDragGhost? dragGhost;string? dragIdentity;bool dragPinned,dragRemoval;
    Rect PinBounds(Button button){var offset=VisualTreeHelper.GetOffset(button);var origin=row.TranslatePoint(new Point(offset.X,offset.Y),this);return new Rect(origin,new Size(button.ActualWidth,button.ActualHeight));}
    bool RemovalAt(Point local)
    {
        var origin=blankSurface.TranslatePoint(new Point(),this);return DockDragGeometry.IsRemoval(local,new Rect(origin,blankSurface.RenderSize),prefs.DockRemovalThreshold);
    }
    void BeginDragFeedback(Button button,string path,bool pinned)
    {
        dragIdentity=path;dragPinned=pinned;dragRemoval=false;
        dragGhost=new DockDragGhost(button,()=>{
            if(!GetCursorPos(out var cursor))return false;var point=PointFromScreen(new Point(cursor.X,cursor.Y));dragRemoval=dragPinned&&RemovalAt(point);UpdateDragFeedback(point,dragIdentity);return dragRemoval;
        });dragGhost.Show();
    }
    void UpdateDragFeedback(Point point,string? excluded=null)
    {
        bool vertical=prefs.Edge==DockEdge.Left;double axis=vertical?point.Y:point.X;
        if(dragging&&dragRemoval){if(insertionMarker!=null)insertionMarker.Visibility=Visibility.Collapsed;ResetDragNeighbours();return;}
        var pins=pinButtons.Where(pin=>excluded==null||!string.Equals(PinIdentity.Resolve(pin.Path),PinIdentity.Resolve(excluded),StringComparison.OrdinalIgnoreCase)).ToList();
        var bounds=pins.Select(pin=>PinBounds(pin.Button)).ToArray();int index=DockDragGeometry.InsertionIndex(bounds,point,vertical);
        var divider=row.Children.OfType<Grid>().FirstOrDefault(element=>element.Tag?.ToString()=="Pinned and running apps separator")??row.Children.OfType<Grid>().FirstOrDefault(element=>element.Tag?.ToString()=="Dock utilities separator");
        Rect boundary=pins.Count==0?new Rect(divider?.TranslatePoint(new Point(),this)??row.TranslatePoint(new Point(),this),new Size(divider?.ActualWidth??8,divider?.ActualHeight??Math.Max(24,prefs.IconSize))):PinBounds(pins[Math.Min(index,pins.Count-1)].Button);
        double position=pins.Count==0?(vertical?boundary.Top:boundary.Left):DockDragGeometry.InsertionPosition(bounds,index,vertical);
        insertionMarker??=new Border{CornerRadius=new CornerRadius(2),Background=SystemColors.HighlightBrush,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false,RenderTransform=new TranslateTransform()};
        if(!dockSurface.Children.Contains(insertionMarker))dockSurface.Children.Add(insertionMarker);
        insertionMarker.Width=vertical?Math.Max(16,prefs.IconSize):3;insertionMarker.Height=vertical?3:Math.Max(16,prefs.IconSize);insertionMarker.Visibility=Visibility.Visible;
        var marker=(TranslateTransform)insertionMarker.RenderTransform;double x=vertical?boundary.Left+(boundary.Width-insertionMarker.Width)/2:position-1.5;double y=vertical?position-1.5:boundary.Top+(boundary.Height-insertionMarker.Height)/2;
        Move(marker,x,y);
        foreach(var pin in pinButtons)
        {
            pin.Button.RenderTransform??=new TranslateTransform();if(pin.Button.RenderTransform is not TranslateTransform shift){shift=new();pin.Button.RenderTransform=shift;}
            var rect=PinBounds(pin.Button);double centre=vertical?rect.Top+rect.Height/2:rect.Left+rect.Width/2;double amount=centre<position?-4:4;Move(shift,vertical?0:amount,vertical?amount:0);
        }
    }
    void Move(TranslateTransform transform,double x,double y)
    {
        void Set(DependencyProperty property,double value){if(Math.Abs((double)transform.GetValue(property)-value)<.1)return;if(SystemParameters.ClientAreaAnimation)transform.BeginAnimation(property,new DoubleAnimation(value,TimeSpan.FromMilliseconds(100)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}},HandoffBehavior.SnapshotAndReplace);else{transform.BeginAnimation(property,null);transform.SetValue(property,value);}}
        Set(TranslateTransform.XProperty,x);Set(TranslateTransform.YProperty,y);
    }
    void ResetDragNeighbours(){foreach(var pin in pinButtons)if(pin.Button.RenderTransform is TranslateTransform transform)Move(transform,0,0);}
    void EndDragFeedback(bool removed)
    {
        if(insertionMarker!=null)insertionMarker.Visibility=Visibility.Collapsed;ResetDragNeighbours();dragGhost?.Finish(removed);dragGhost=null;dragIdentity=null;dragRemoval=false;
    }
    void AnimateDroppedPin(string path)
    {
        if(!SystemParameters.ClientAreaAnimation)return;
        Dispatcher.BeginInvoke(new Action(()=>{var pin=pinButtons.FirstOrDefault(pin=>string.Equals(PinIdentity.Resolve(pin.Path),PinIdentity.Resolve(path),StringComparison.OrdinalIgnoreCase));if(pin.Button==null||!GetCursorPos(out var cursor))return;var local=PointFromScreen(new Point(cursor.X,cursor.Y));var bounds=PinBounds(pin.Button);var transform=new TranslateTransform();pin.Button.RenderTransform=transform;double x=Math.Clamp(local.X-bounds.Left-bounds.Width/2,-80,80),y=Math.Clamp(local.Y-bounds.Top-bounds.Height/2,-80,80);var easing=new CubicEase{EasingMode=EasingMode.EaseOut};transform.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(x,0,TimeSpan.FromMilliseconds(150)){EasingFunction=easing});transform.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(y,0,TimeSpan.FromMilliseconds(150)){EasingFunction=easing});}),DispatcherPriority.ContextIdle);
    }
}
internal sealed class DockDragGhost:Window
{
    readonly DispatcherTimer timer=new(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(16)};readonly Func<bool> update;readonly TextBlock badge=new(){Text="Release to remove pin",FontSize=10,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center};readonly Grid surface=new();readonly Border badgeFrame;readonly double pointerY;IntPtr handle;bool finishing;
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetStyle(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetStyle(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")]static extern bool GetCursorPos(out PointNative point);
    [StructLayout(LayoutKind.Sequential)]struct PointNative{public int X,Y;}
    internal DockDragGhost(Button source,Func<bool> update)
    {
        this.update=update;Width=128;Height=92;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;IsHitTestVisible=false;
        var visual=(source.Content as Grid)?.Children.OfType<Viewbox>().FirstOrDefault() as FrameworkElement??source.Content as FrameworkElement;ImageSource? image=null;
        if(visual!=null&&visual.ActualWidth>0&&visual.ActualHeight>0){var dpi=VisualTreeHelper.GetDpi(source);var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(visual.ActualWidth*dpi.DpiScaleX)),Math.Max(1,(int)Math.Ceiling(visual.ActualHeight*dpi.DpiScaleY)),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();image=bitmap;}
        double iconHeight=Math.Max(16,visual?.ActualHeight??36);pointerY=6+iconHeight/2;
        surface.Children.Add(new Image{Source=image,Width=Math.Max(16,visual?.ActualWidth??36),Height=iconHeight,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,6,0,0),Opacity=.88});badge.VerticalAlignment=VerticalAlignment.Bottom;
        badgeFrame=new Border{Child=badge,Background=new SolidColorBrush(Color.FromArgb(210,30,30,30)),CornerRadius=new CornerRadius(5),VerticalAlignment=VerticalAlignment.Bottom,Padding=new Thickness(4,3,4,3),Margin=new Thickness(1),IsHitTestVisible=false,Visibility=Visibility.Collapsed};surface.Children.Add(badgeFrame);Content=surface;
        SourceInitialized+=(_,_)=>{handle=new WindowInteropHelper(this).Handle;SetStyle(handle,-20,new IntPtr(GetStyle(handle,-20).ToInt64()|0x080000A0));Position();};
        timer.Tick+=(_,_)=>Position();Loaded+=(_,_)=>timer.Start();Closed+=(_,_)=>timer.Stop();
    }
    void Position(){if(finishing||handle==IntPtr.Zero)return;if(GetCursorPos(out var cursor)){double scale=Math.Max(96,DesktopDisplays.GetDpiForWindow(handle))/96d;DesktopDisplays.SetWindowPos(handle,new IntPtr(-1),cursor.X-(int)(Width*scale/2),cursor.Y-(int)(pointerY*scale),0,0,0x0011);}bool removing=update();badgeFrame.Visibility=removing?Visibility.Visible:Visibility.Collapsed;}
    internal void Finish(bool removed)
    {
        if(finishing)return;finishing=true;timer.Stop();if(!removed||!SystemParameters.ClientAreaAnimation){Close();return;}
        surface.RenderTransformOrigin=new Point(.5,.35);var scale=new ScaleTransform(1,1);surface.RenderTransform=scale;var duration=TimeSpan.FromMilliseconds(150);scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1.2,duration));scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1.2,duration));var fade=new DoubleAnimation(0,duration);fade.Completed+=(_,_)=>Close();BeginAnimation(OpacityProperty,fade);
    }
}
