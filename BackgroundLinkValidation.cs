using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NewStart;
internal static class BackgroundLinkValidation
{
    internal static void Run(Dictionary<string,bool> checks)
    {
        var prefs=new Preferences{Theme=2,DockTransparency=.2,AppBarTransparency=.7,DockBlur=6,AppBarBlur=24,DockColour="#112233",AppBarColour="#8844CC",AppBarColourSource=1};
        checks["AppearanceLinksDefaultOff"]=!prefs.LinkBackgroundEffects&&!prefs.LinkBackgroundColours;
        var dock=BackgroundSettings.Create(prefs,false,()=>null,()=>{});var appBar=BackgroundSettings.Create(prefs,true,()=>null,()=>{});
        static IEnumerable<DependencyObject> Tree(DependencyObject root){yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var node in Tree(child))yield return node;}
        static T Named<T>(FrameworkElement root,string name) where T:DependencyObject=>Tree(root).OfType<T>().Single(node=>AutomationProperties.GetName(node)==name);
        dock.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));appBar.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        try
        {
            prefs.LinkBackgroundEffects=true;prefs.SynchroniseBackgrounds(true);
            checks["LinkEffectsUsesAppBarValues"]=prefs.DockTransparency==.7&&prefs.DockBlur==24&&prefs.DockColour=="#112233";
            Named<Slider>(dock,"Dock background transparency").Value=35;
            checks["DockEffectEditUpdatesBothEditors"]=prefs.AppBarTransparency==.35&&Named<Slider>(appBar,"AppBar background transparency").Value==35;
            Named<Slider>(appBar,"AppBar background blur").Value=50;
            checks["AppBarEffectEditUpdatesBothEditors"]=prefs.DockBlur==30&&Named<Slider>(dock,"Dock background blur").Value==50;
            prefs.LinkBackgroundColours=true;prefs.SynchroniseBackgrounds(true);
            checks["LinkColourUsesAppBarValues"]=prefs.DockColour=="#8844CC"&&prefs.DockColourSource==1;
            Named<TextBox>(dock,"Dock colour HEX").Text="#24A060";
            checks["DockColourEditUpdatesBothEditors"]=prefs.AppBarColour=="#24A060"&&Named<TextBox>(appBar,"AppBar colour HEX").Text=="#24A060";
            prefs.AppBarColourSource=2;prefs.AppBarWallpaperStrategy=1;prefs.SynchroniseBackgrounds(true);
            checks["WallpaperSourceAndStrategyLinked"]=prefs.DockColourSource==2&&prefs.DockWallpaperStrategy==1;
            prefs.LinkBackgroundEffects=false;prefs.SynchroniseBackgrounds(true);Named<Slider>(dock,"Dock background transparency").Value=10;
            checks["UnlinkedEffectsRemainIndependent"]=prefs.DockTransparency==.1&&prefs.AppBarTransparency==.35&&prefs.LinkBackgroundColours;
            var saved=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(prefs))!;saved.Normalize();
            checks["LinkPreferencesPersist"]=saved.LinkBackgroundColours&&!saved.LinkBackgroundEffects&&saved.DockWallpaperStrategy==1&&saved.DockTransparency==.1&&saved.AppBarTransparency==.35;
            prefs.LinkBackgroundColours=false;prefs.SynchroniseBackgrounds(true);Named<TextBox>(appBar,"AppBar colour HEX").Text="#FF8040";
            checks["UnlinkedColoursRemainIndependent"]=prefs.DockColour=="#24A060"&&prefs.AppBarColour=="#FF8040";
        }
        finally{dock.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));appBar.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));}
    }
}
