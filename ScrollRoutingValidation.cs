using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NewStart;
internal static class ScrollRoutingValidation
{
    internal static async Task Run(Window owner,Dictionary<string,bool> checks)
    {
        var source=new TextBlock{Text="Scroll routing validation",Height=6000,Margin=new Thickness(8),Foreground=Brushes.Black};
        var inner=new ScrollViewer{Height=160,Content=source,CanContentScroll=false,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var content=new StackPanel();content.Children.Add(new Border{Height=60});content.Children.Add(inner);content.Children.Add(new Border{Height=1200});
        var outer=new ScrollViewer{Content=content,CanContentScroll=false,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var probe=new Window{Owner=owner,Title="New Start scroll routing fixture",Width=400,Height=330,Content=outer,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.White,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        async Task Settle(){await probe.Dispatcher.InvokeAsync(()=>probe.UpdateLayout(),DispatcherPriority.ContextIdle);await Task.Delay(25);}
        async Task<MouseWheelEventArgs> Packet(int delta)
        {
            var args=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta){RoutedEvent=UIElement.PreviewMouseWheelEvent};
            source.RaiseEvent(args);await Settle();return args;
        }
        static bool Near(double actual,double expected)=>Math.Abs(actual-expected)<.05;
        try
        {
            probe.Show();await Settle();outer.ScrollToVerticalOffset(0);inner.ScrollToVerticalOffset(0);await Settle();
            checks["ScrollRoutingNestedViewersScrollable"]=outer.ScrollableHeight>0&&inner.ScrollableHeight>0&&inner.ViewportHeight>0;
            double outerOffset=outer.VerticalOffset;int lines=SystemParameters.WheelScrollLines;double multiplier=ScrollPolicy.Settings.ScrollMultiplier;
            var first=await Packet(-120);
            checks["ScrollRoutingPacketHandledOnce"]=first.Handled&&ReferenceEquals(first.OriginalSource,source);
            if(lines==0)
            {
                checks["ScrollRoutingSystemPreferenceDisablesMovement"]=Near(inner.VerticalOffset,0)&&Near(outer.VerticalOffset,outerOffset);
                var disabled=await Packet(120);checks["ScrollRoutingDisabledReverseRemainsHandled"]=disabled.Handled&&Near(inner.VerticalOffset,0)&&Near(outer.VerticalOffset,outerOffset);
                checks["ScrollRoutingFixtureCompleted"]=true;return;
            }
            double movement=ScrollPolicy.Units(-120,lines,UiMetrics.Row,false,inner.ViewportHeight,multiplier);
            double expected=Math.Clamp(movement,0,inner.ScrollableHeight);
            checks["ScrollRoutingInnerUsesWindowsUnitsExactly"]=Near(inner.VerticalOffset,expected);
            checks["ScrollRoutingOuterDoesNotReceiveInnerPacket"]=Near(outer.VerticalOffset,outerOffset);
            var reverse=await Packet(120);
            checks["ScrollRoutingReverseReturnsToBoundedZero"]=reverse.Handled&&Near(inner.VerticalOffset,0)&&Near(outer.VerticalOffset,outerOffset);

            bool precisionHandled=true;for(int packet=0;packet<4;packet++)precisionHandled&=(await Packet(-30)).Handled;
            checks["ScrollRoutingPrecisionPacketsConserveDetent"]=precisionHandled&&Near(inner.VerticalOffset,expected);
            checks["ScrollRoutingPrecisionDoesNotMoveOuter"]=Near(outer.VerticalOffset,outerOffset);
            inner.ScrollToVerticalOffset(inner.ScrollableHeight);await Settle();double bottom=inner.ScrollableHeight;
            var atBottom=await Packet(-120);
            checks["ScrollRoutingBottomDoesNotOverscroll"]=atBottom.Handled&&Near(inner.VerticalOffset,bottom)&&inner.VerticalOffset<=inner.ScrollableHeight+.05;
            checks["ScrollRoutingBottomDoesNotPropagate"]=Near(outer.VerticalOffset,outerOffset);
            checks["ScrollRoutingFixtureCompleted"]=true;
        }
        catch{checks["ScrollRoutingFixtureCompleted"]=false;}
        finally{probe.Close();}
    }
}
