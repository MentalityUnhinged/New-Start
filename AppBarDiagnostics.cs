using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NewStart;
// Diagnostic access stays in this partial, without exposing implementation controls to the product UI.
internal sealed partial class AdaptiveAppBarWindow
{
    internal bool DiagnosticStartVisible=>buttons.TryGetValue(AppBarSection.Start,out var start)&&start.IsVisible;
    internal bool DiagnosticTrayVisible=>trayButton?.IsVisible==true;
    internal bool DiagnosticApplicationMenuVisible=>categoryButtons.Visibility==Visibility.Visible;
    internal bool DiagnosticHasContextEntry(string label)=>root.ContextMenu?.Items.OfType<MenuItem>().Any(item=>item.Header?.ToString()==label)==true;
    internal bool DiagnosticComponentContextMenus=>buttons.Values.Distinct().All(button=>button.ContextMenu?.Items.OfType<MenuItem>().Any(item=>item.Header?.ToString()=="AppBar settings")==true);
    static IEnumerable<DependencyObject> DiagnosticTree(DependencyObject element)
    {
        yield return element;foreach(var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())foreach(var descendant in DiagnosticTree(child))yield return descendant;
    }
    internal string DiagnosticBodyText=>string.Join("\n",DiagnosticTree(body).OfType<TextBlock>().Select(text=>text.Text));
    internal void DiagnosticExpandPlugin(string id){selectedPlugin=id;Expand(AppBarSection.Plugin);}
    internal async Task DiagnosticCheckContextAppearance(Dictionary<string,bool> checks,string key)
    {
        var menu=root.ContextMenu;if(menu==null){checks[key+"ContextMenuExists"]=false;return;}
        var saved=Prefs.AppBarTransparency;
        try
        {
            foreach(double transparency in new[]{0d,.5,1d})
            {
                Prefs.AppBarTransparency=transparency;UpdateAppearance();menu.PlacementTarget=root;menu.IsOpen=true;await Task.Delay(100);menu.ApplyTemplate();
                var expected=SurfaceAppearance.Background(Prefs,true,Display.Name);byte alpha=(byte)Math.Round(255*(1-transparency));
                checks[key+"ContextTintT"+transparency]=SystemParameters.HighContrast||menu.Resources["MenuSurface"] is SolidColorBrush brush&&brush.Color==Color.FromArgb(alpha,expected.R,expected.G,expected.B);
                checks[key+"ContextCompactTemplateT"+transparency]=menu.Template!=null&&menu.Resources["MenuPadding"] is Thickness padding&&padding==UiMetrics.MenuPadding(Prefs);
                checks[key+"ContextKeepsControlOpacityT"+transparency]=menu.Items.OfType<MenuItem>().Where(item=>item.IsEnabled).All(item=>item.Opacity==1);
                menu.IsOpen=false;await Task.Delay(40);checks[key+"ContextDismissesT"+transparency]=!menu.IsOpen;
            }
        }
        finally{menu.IsOpen=false;Prefs.AppBarTransparency=saved;UpdateAppearance();}
    }
}
