using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NewStart;
internal static class InteractionValidation
{
    // No live preferences, application activation, Explorer or shell configuration is changed.
    internal static void Run(Dictionary<string,bool> checks)
    {
        static bool Near(double first,double second)=>Math.Abs(first-second)<.000001;
        checks["WheelRespectsWindowsLineCount"]=Near(ScrollPolicy.Units(120,3,28,false,600,1),-84);
        checks["WheelDirectionReverses"]=Near(ScrollPolicy.Units(-120,3,28,false,600,1),84);
        checks["WheelDisabledPreferenceStopsScroll"]=ScrollPolicy.Units(120,0,28,false,600,1)==0;
        checks["WheelPagePreferenceUsesViewport"]=Near(ScrollPolicy.Units(120,-1,28,false,600,1),-600);
        checks["WheelLogicalContentUsesItemUnits"]=Near(ScrollPolicy.Units(120,3,28,true,600,1),-3);
        checks["WheelCustomSpeedAppliedOnce"]=Near(ScrollPolicy.Units(120,3,28,false,600,1.5),-126);
        checks["WheelPrecisionPacketsConserveDistance"]=Near(new[]{15,30,45,30}.Sum(delta=>ScrollPolicy.Units(delta,3,28,false,600,1)),ScrollPolicy.Units(120,3,28,false,600,1));
        checks["WheelZeroPacketDoesNotMove"]=ScrollPolicy.Units(0,3,28,false,600,1)==0;
        var wheel=new WheelAccumulator();checks["PagedWheelAccumulatesPrecisionInput"]=new[]{30,30,30,30}.Select(wheel.Pages).SequenceEqual(new[]{0,0,0,-1});
        wheel=new();checks["PagedWheelConservesRemainder"]=new[]{90,90,60}.Select(wheel.Pages).SequenceEqual(new[]{0,-1,-1});
        wheel=new();checks["PagedWheelMultipleDetents"]=wheel.Pages(240)==-2&&wheel.Pages(-360)==3;
        wheel=new();checks["PagedWheelReversalDiscardsOldDirection"]=new[]{60,-90,-30}.Select(wheel.Pages).SequenceEqual(new[]{0,0,1});
        wheel=new();checks["PagedWheelZeroPacketPreservesRemainder"]=new[]{60,0,60}.Select(wheel.Pages).SequenceEqual(new[]{0,0,-1});
        checks["ActivationEmptySelectionSafe"]=ApplicationActivation.Choose(Array.Empty<Native.TaskWindow>(),IntPtr.Zero)==null;
        // Exact-foreground precedence needs no native window state query or focus change.
        var first=new Native.TaskWindow(new IntPtr(101),"First",@"C:\Fixture\one.exe");var second=new Native.TaskWindow(new IntPtr(202),"Second",@"C:\Fixture\one.exe");
        checks["ActivationSelectsExactForegroundWindow"]=ReferenceEquals(ApplicationActivation.Choose(new[]{first,second},second.Handle),second);
        checks["ActivationPreservesFirstExactMatch"]=ReferenceEquals(ApplicationActivation.Choose(new[]{first,second},first.Handle),first);
        var legacy=JsonSerializer.Deserialize<Preferences>("""{"AppBarEnabled":true,"Pins":["C:\\Fixture\\App.exe"],"IconSize":24}""")!;legacy.Normalize();
        checks["LegacyStartPlacementMigratesOnce"]=legacy.ShowDockStart==false&&legacy.ShowAppBarStart&&legacy.IconSize==24&&legacy.Pins.SequenceEqual(new[]{@"C:\Fixture\App.exe"});
        legacy.AppBarEnabled=false;legacy.Normalize();checks["MigratedStartChoiceStaysIndependent"]=legacy.ShowDockStart==false;
        var dockOnly=JsonSerializer.Deserialize<Preferences>("{}");dockOnly!.Normalize();checks["LegacyDockStartRemainsVisible"]=dockOnly.ShowDockStart==true;
        var hidden=new Preferences{ShowDockStart=false,ShowAppBarStart=false,AppBarEnabled=false};hidden.Normalize();checks["IndependentStartVisibilityPreserved"]=hidden.ShowDockStart==false&&!hidden.ShowAppBarStart;
        var malformed=new Preferences{MenuDensity=9,ScrollMultiplier=double.NaN,MenuAnimationMilliseconds=-1,AppBarHeight=900,AppBarPadding=0,AppBarCornerRadius=-1,AppBarExpansionWidth=1,DockIconSpacing=99,DockRemovalThreshold=1,HoverMilliseconds=double.PositiveInfinity,BounceMilliseconds=1};malformed.Normalize();
        checks["AdvancedRangesClampSafely"]=malformed.MenuDensity==1&&malformed.ScrollMultiplier==1&&malformed.MenuAnimationMilliseconds==0&&malformed.AppBarHeight==56&&malformed.AppBarPadding==2&&malformed.AppBarCornerRadius==0&&malformed.AppBarExpansionWidth==300&&malformed.DockIconSpacing==20&&malformed.DockRemovalThreshold==24&&malformed.HoverMilliseconds==150&&malformed.BounceMilliseconds==100;
        var upper=new Preferences{ScrollMultiplier=9,AppBarAnimationMilliseconds=900,AppBarExpansionWidth=900,DockRemovalThreshold=900};upper.Normalize();
        checks["AdvancedUpperRangesClamp"]=upper.ScrollMultiplier==3&&upper.AppBarAnimationMilliseconds==600&&upper.AppBarExpansionWidth==640&&upper.DockRemovalThreshold==160;
        var saved=new Preferences{AdvancedSettings=true,AppBarEnabled=true,ShowDockStart=true,ShowAppBarStart=false,DockDragging=false,DockIconSpacing=7,ScrollMultiplier=1.35,AppBarHeight=42,AppBarExpansionWidth=510};
        var restored=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(saved))!;restored.Normalize();restored.AdvancedSettings=false;restored.Normalize();
        checks["AdvancedHiddenValuesPersist"]=restored.DockIconSpacing==7&&!restored.DockDragging&&restored.ScrollMultiplier==1.35&&restored.AppBarHeight==42&&restored.AppBarExpansionWidth==510&&restored.ShowDockStart==true&&!restored.ShowAppBarStart;
        ValidateEditor(checks);
    }
    static void ValidateEditor(Dictionary<string,bool> checks)
    {
        static IEnumerable<DependencyObject> Tree(DependencyObject root){yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var item in Tree(child))yield return item;}
        static T Named<T>(DependencyObject root,string name) where T:DependencyObject=>Tree(root).OfType<T>().Single(item=>AutomationProperties.GetName(item)==name);
        static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var prefs=new Preferences{DockIconSpacing=7};int commits=0;var editor=AdvancedSettings.Create(prefs,()=>commits++);
        checks["AdvancedEditorConstructionIsPassive"]=commits==0&&!prefs.AdvancedSettings;
        var enabled=Named<CheckBox>(editor,"Show advanced settings");enabled.IsChecked=true;enabled.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        Click(Named<Button>(editor,"Dock options"));Click(Named<Button>(editor,"Layout and dragging"));Named<Slider>(editor,"Extra icon spacing").Value=12;
        checks["AdvancedEditorAppliesWorkingFields"]=prefs.DockIconSpacing==12&&prefs.AdvancedSettings&&commits==2;
        Click(Named<Button>(editor,"Reset this section"));checks["AdvancedEditorSectionReset"]=prefs.DockIconSpacing==0&&prefs.DockDragging&&prefs.DockRemovalThreshold==48;
        Click(Named<Button>(editor,"Advanced"));Click(Named<Button>(editor,"AppBar options"));Click(Named<Button>(editor,"Layout and expansion"));Named<Slider>(editor,"AppBar height").Value=44;
        enabled.IsChecked=false;enabled.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));checks["AdvancedEditorHidingRetainsChanges"]=!prefs.AdvancedSettings&&prefs.AppBarHeight==44;
    }
    internal static void Run()
    {
        var checks=new Dictionary<string,bool>();Run(checks);Directory.CreateDirectory(Preferences.Folder);File.WriteAllText(Path.Combine(Preferences.Folder,"interaction-verification.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
        if(checks.Values.Any(value=>!value))throw new InvalidOperationException("Interaction regression checks failed.");
    }
}
