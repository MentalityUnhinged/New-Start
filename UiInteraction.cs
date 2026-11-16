using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NewStart;
internal static class UiMetrics
{
    internal const double Corner=8,Gap=6,Row=28;
    internal static Thickness MenuPadding(Preferences prefs)=>prefs.MenuDensity==0?new(9,5,9,5):new(11,8,11,8);
    internal static void Fade(UIElement target,int milliseconds,bool enabled=true)
    {
        target.BeginAnimation(UIElement.OpacityProperty,null);target.Opacity=1;
        if(enabled&&SystemParameters.ClientAreaAnimation&&milliseconds>0)target.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.25,1,TimeSpan.FromMilliseconds(milliseconds)){FillBehavior=FillBehavior.Stop});
    }
}
internal sealed class WheelAccumulator
{
    double remainder;
    internal int Pages(int delta)
    {
        if(delta==0)return 0;
        if(remainder!=0&&Math.Sign(delta)!=Math.Sign(remainder))remainder=0;
        remainder+=delta;int pages=(int)(remainder/120);remainder-=pages*120;return -pages;
    }
}
internal static class ScrollPolicy
{
    sealed class Paging{internal Func<bool> Active=null!;internal Action<int> Move=null!;internal WheelAccumulator Wheel=new();}
    sealed class Remainder{internal double Value;}
    static readonly ConditionalWeakTable<ScrollViewer,Paging> paged=new();static readonly ConditionalWeakTable<ScrollViewer,Remainder> fractions=new();static bool installed;
    internal static Preferences Settings {get;set;}=new();
    internal static void Paged(ScrollViewer viewer,Func<bool> active,Action<int> move){paged.Remove(viewer);paged.Add(viewer,new(){Active=active,Move=move});}
    internal static double Units(int delta,int lines,double row,bool logical,double viewport,double multiplier)=>lines==0?0:-delta/120d*(lines<0?viewport:lines*(logical?1:row))*multiplier;
    internal static void Install()
    {
        if(installed)return;installed=true;
        EventManager.RegisterClassHandler(typeof(ScrollViewer),UIElement.PreviewMouseWheelEvent,new MouseWheelEventHandler(Wheel));
        EventManager.RegisterClassHandler(typeof(ScrollViewer),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>((ScrollViewer)sender).PanningMode=PanningMode.VerticalOnly));
    }
    static void Wheel(object sender,MouseWheelEventArgs args)
    {
        if(args.Handled||sender is not ScrollViewer viewer)return;
        DependencyObject? current=args.OriginalSource as DependencyObject;ScrollViewer? nearest=null;
        for(int depth=0;current!=null&&depth<80;depth++)
        {
            if(current is ScrollViewer found){nearest=found;break;}
            current=current is Visual?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current);
        }
        if(nearest!=null&&nearest!=viewer)return;
        args.Handled=true; // A wheel packet is handled once, not again by a parent/default WPF handler.
        if(SystemParameters.WheelScrollLines==0)return;
        if(paged.TryGetValue(viewer,out var page)&&page.Active()){int steps=page.Wheel.Pages(args.Delta);if(steps!=0)page.Move(steps);return;}
        double movement=Units(args.Delta,SystemParameters.WheelScrollLines,UiMetrics.Row,viewer.CanContentScroll,viewer.ViewportHeight,Settings.ScrollMultiplier);
        if(viewer.CanContentScroll){var fraction=fractions.GetOrCreateValue(viewer);if(fraction.Value*movement<0)fraction.Value=0;fraction.Value+=movement;movement=Math.Truncate(fraction.Value);fraction.Value-=movement;}
        viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset+movement,0,viewer.ScrollableHeight));
    }
}
