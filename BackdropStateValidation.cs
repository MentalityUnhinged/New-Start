using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NewStart;
internal static class BackdropStateValidation
{
    internal static async Task Run(Window owner,Dictionary<string,bool> checks)
    {
        // This fixture owns its HWND and material. It never changes a live app or saved preferences.
        var caption=new TextBlock{Text="Backdrop validation",Margin=new Thickness(16),VerticalAlignment=VerticalAlignment.Center};
        var choice=new{Name="Friendly choice",InternalData="should-not-show"};
        var selection=new ComboBox{ItemsSource=new[]{choice},DisplayMemberPath="Name",SelectedIndex=0,Margin=new Thickness(12,0,12,4)};
        var expander=new Expander{Header="Expandable choices",Content=new TextBlock{Text="Fixture only"},Margin=new Thickness(12,0,12,4)};
        var content=new StackPanel();content.Children.Add(caption);content.Children.Add(selection);content.Children.Add(expander);
        var frame=new Border{CornerRadius=new CornerRadius(8),Child=content,Background=Brushes.Transparent};
        var probe=new Window{Owner=owner,Title="New Start backdrop fixture",Width=260,Height=140,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.CenterScreen,Content=frame};
        bool nativeAvailable=false;
        try
        {
            probe.Show();await Task.Delay(100);
            nativeAvailable=NativeBackdrop.TransparencyAllowed()&&CustomBackdrop.Apply(probe,20);await Task.Delay(80);
            if(nativeAvailable)checks["BackdropPositiveRadiusInitiallyVisible"]=CustomBackdrop.Visible(probe);
            else checks["BackdropPositiveVisibilityUnavailableSkipped"]=true;

            CustomBackdrop.Hide(probe);await Task.Delay(50);checks["BackdropExplicitHideIsHidden"]=!CustomBackdrop.Visible(probe);
            probe.Width+=18;probe.Height+=12;await Task.Delay(80);checks["BackdropHideSurvivesResize"]=!CustomBackdrop.Visible(probe);
            probe.Left+=6;await Task.Delay(50);checks["BackdropHideSurvivesMove"]=!CustomBackdrop.Visible(probe);
            bool activated=probe.Activate();await Task.Delay(80);
            if(activated&&probe.IsActive)checks["BackdropHideSurvivesActivation"]=!CustomBackdrop.Visible(probe);
            else checks["BackdropActivationDeniedSkipped"]=true;
            checks["BackdropHideStillHiddenAfterActivationAttempt"]=!CustomBackdrop.Visible(probe);

            if(nativeAvailable)
            {
                bool reapplied=CustomBackdrop.Apply(probe,20);await Task.Delay(80);checks["BackdropPositiveRadiusCanBeReenabled"]=reapplied&&CustomBackdrop.Visible(probe);
            }
            bool zeroApplied=CustomBackdrop.Apply(probe,0);await Task.Delay(60);
            checks["BackdropZeroRadiusRemainsHidden"]=(!nativeAvailable||zeroApplied)&&!CustomBackdrop.Visible(probe);
            probe.Hide();await Task.Delay(40);probe.Show();probe.Width+=2;await Task.Delay(80);
            checks["BackdropZeroRadiusSurvivesHideShowResize"]=!CustomBackdrop.Visible(probe);
            probe.Activate();await Task.Delay(60);checks["BackdropZeroRadiusSurvivesActivationAttempt"]=!CustomBackdrop.Visible(probe);

            // Exercise the same opaque/clear-background states used by configuration surfaces.
            foreach(bool dark in new[]{false,true})
            {
                Appearance.Apply(probe,dark);probe.Background=Brushes.Transparent;
                selection.ApplyTemplate();expander.ApplyTemplate();probe.UpdateLayout();
                await probe.Dispatcher.InvokeAsync(()=>probe.UpdateLayout(),DispatcherPriority.Render);await Task.Delay(50);
                var rendered=VisualTree(probe).OfType<TextBlock>().Where(text=>text.IsVisible).ToArray();string theme=dark?"Dark":"Light";
                checks["BackdropComboShowsDisplayMember"+theme]=rendered.Any(text=>text.Text=="Friendly choice");
                checks["BackdropComboHidesRecordInternals"+theme]=rendered.All(text=>!text.Text.Contains("InternalData",StringComparison.Ordinal)&&!text.Text.Contains("should-not-show",StringComparison.Ordinal));
                checks["BackdropCaptionInheritsThemeForeground"+theme]=SameColour(caption.Foreground,probe.Foreground);
                checks["BackdropExpanderInheritsThemeForeground"+theme]=SameColour(expander.Foreground,probe.Foreground)&&rendered.Any(text=>text.Text=="Expandable choices"&&SameColour(text.Foreground,probe.Foreground));
                foreach(double transparency in new[]{0d,1d})
                {
                    if(nativeAvailable)CustomBackdrop.Apply(probe,20);
                    CustomBackdrop.Hide(probe);
                    Color colour=dark?Colors.Black:Colors.White;var tint=SurfaceAppearance.Material(colour,transparency).Tint;frame.Background=tint;
                    probe.Width+=2;probe.Activate();probe.Hide();probe.Show();await Task.Delay(80);
                    string suffix=$"{(dark?"Dark":"Light")}T{transparency}";
                    checks["BackdropEndpointMaterial"+suffix]=tint is SolidColorBrush brush&&brush.Color==colour&&Math.Abs(brush.Opacity-(1-transparency))<.001;
                    checks["BackdropEndpointStaysHidden"+suffix]=!CustomBackdrop.Visible(probe);
                    checks["BackdropEndpointKeepsForegroundOpacity"+suffix]=probe.Opacity==1&&caption.Opacity==1;
                }
            }
        }
        catch
        {
            checks["BackdropStateFixtureCompleted"]=false;
        }
        finally
        {
            try{CustomBackdrop.Hide(probe);}finally{probe.Close();}
            checks["BackdropClosedSurfaceReleased"]=!CustomBackdrop.Visible(probe);
        }
        if(!checks.ContainsKey("BackdropStateFixtureCompleted"))checks["BackdropStateFixtureCompleted"]=true;
    }
    static bool SameColour(Brush actual,Brush expected)=>ReferenceEquals(actual,expected)||actual is SolidColorBrush first&&expected is SolidColorBrush second&&first.Color==second.Color;
    static IEnumerable<DependencyObject> VisualTree(DependencyObject element)
    {
        yield return element;for(int child=0;child<VisualTreeHelper.GetChildrenCount(element);child++)foreach(var descendant in VisualTree(VisualTreeHelper.GetChild(element,child)))yield return descendant;
    }
}
