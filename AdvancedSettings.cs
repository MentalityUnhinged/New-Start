using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NewStart;
// Each detail page edits the same Preferences instance as the everyday settings.
internal static class AdvancedSettings
{
    internal static FrameworkElement Create(Preferences prefs,Action apply,Action? configurePlugins=null)
    {
        var root=new StackPanel{Margin=new Thickness(0,4,0,12)};
        var enabled=new CheckBox{Content="Show advanced settings",IsChecked=prefs.AdvancedSettings,Margin=new Thickness(0,4,0,8)};
        AutomationProperties.SetName(enabled,"Show advanced settings");root.Children.Add(enabled);
        var navigation=new StackPanel{Visibility=prefs.AdvancedSettings?Visibility.Visible:Visibility.Collapsed};root.Children.Add(navigation);
        var breadcrumbs=new WrapPanel{Margin=new Thickness(0,0,0,8)};navigation.Children.Add(breadcrumbs);
        var body=new StackPanel();navigation.Children.Add(body);string category="",page="";
        void Commit(){prefs.Normalize();apply();}
        void ResetMenu(Control control,Action reset){var menu=ThemedMenus.Create(prefs,()=>SurfaceAppearance.MenuPreferences(prefs,prefs.AppBarEnabled));var item=new MenuItem{Header="Reset to default"};item.Click+=(_,_)=>reset();menu.Items.Add(item);control.ContextMenu=menu;}
        enabled.Click+=(_,_)=>{prefs.AdvancedSettings=enabled.IsChecked==true;navigation.Visibility=prefs.AdvancedSettings?Visibility.Visible:Visibility.Collapsed;Commit();};
        Button Button(string label,Action click)
        {
            var button=new Button{Content=label,Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,6,6),HorizontalContentAlignment=HorizontalAlignment.Left};
            AutomationProperties.SetName(button,label);button.Click+=(_,_)=>click();return button;
        }
        void Navigate(string nextCategory="",string nextPage=""){category=nextCategory;page=nextPage;Render();}
        void Description(string text)=>body.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Opacity=.8,Margin=new Thickness(0,0,0,10)});
        void Toggle(string label,bool value,Action<bool> set,bool fallback=true)
        {
            var check=new CheckBox{Content=label,IsChecked=value,Margin=new Thickness(0,4,0,8)};AutomationProperties.SetName(check,label);body.Children.Add(check);check.Click+=(_,_)=>{set(check.IsChecked==true);Commit();};ResetMenu(check,()=>{check.IsChecked=fallback;set(fallback);Commit();});
        }
        void Slider(string label,double value,int minimum,int maximum,Action<double> set,string suffix="")
        {
            var text=new TextBlock{Text=label+": "+Math.Round(value).ToString("0")+suffix,Margin=new Thickness(0,4,0,2)};body.Children.Add(text);
            var slider=new Slider{Minimum=minimum,Maximum=maximum,Value=Math.Clamp(Math.Round(value),minimum,maximum),TickFrequency=1,SmallChange=1,LargeChange=5,IsSnapToTickEnabled=true,Margin=new Thickness(0,0,0,10)};
            AutomationProperties.SetName(slider,label);body.Children.Add(slider);slider.ValueChanged+=(_,_)=>{double selected=Math.Round(slider.Value);text.Text=label+": "+selected.ToString("0")+suffix;set(selected);Commit();};
            double original=label switch{"Extra icon spacing"=>0,"Drag removal distance"=>48,"Hover magnification"=>125,"Hover duration"=>150,"Bounce height"=>10,"Bounce duration"=>650,"AppBar height"=>36,"AppBar horizontal padding"=>8,"AppBar corner radius"=>16,"AppBar expansion width"=>440,"AppBar animation duration"=>180,"Scroll speed"=>100,"Menu animation duration"=>120,_=>minimum};ResetMenu(slider,()=>slider.Value=original);
        }
        void Reset(Action action)=>body.Children.Add(Button("Reset this section",()=>{action();Commit();Render();}));
        void Render()
        {
            breadcrumbs.Children.Clear();body.Children.Clear();breadcrumbs.Children.Add(Button("Advanced",()=>Navigate()));
            if(category.Length>0){breadcrumbs.Children.Add(new TextBlock{Text="›",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,6,6)});breadcrumbs.Children.Add(Button(category,()=>Navigate(category)));}
            if(page.Length>0){breadcrumbs.Children.Add(new TextBlock{Text="› "+page,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,0,6)});}
            if(category.Length==0)
            {
                Description("Choose a category to adjust deeper options. Hiding Advanced keeps these settings active.");
                foreach(string item in new[]{"Dock","AppBar","Menus"}){string selected=item;body.Children.Add(Button(selected+" options",()=>Navigate(selected)));}if(configurePlugins!=null)body.Children.Add(Button("Plugin options",()=>Navigate("Plugins","Configuration")));return;
            }
            if(page.Length==0)
            {
                foreach(string item in category=="Dock"?new[]{"Layout and dragging","Animations","Components"}:category=="AppBar"?new[]{"Layout and expansion","Animations","Components"}:new[]{"Density and scrolling","Animations"})
                {string selected=item;body.Children.Add(Button(selected,()=>Navigate(category,selected)));}return;
            }
            if(category=="Plugins"){Description("Plugin permissions, refresh intervals, independent placements, configuration and diagnostics use the same dedicated manager as Standard settings.");if(configurePlugins!=null)body.Children.Add(Button("Manage plugins…",configurePlugins));return;}
            if(page=="Components")
            {
                if(category=="Dock")
                {
                    Toggle("Show Start button in Dock",prefs.ShowDockStart??!prefs.AppBarEnabled,v=>prefs.ShowDockStart=v);
                    Toggle("Show Task View",prefs.ShowTaskView,v=>prefs.ShowTaskView=v);Toggle("Show Desktop",prefs.ShowDesktop,v=>prefs.ShowDesktop=v);
                    Toggle("Show system controls",prefs.ShowSystemControls,v=>prefs.ShowSystemControls=v,false);Toggle("Show Windows notification area access in Dock",prefs.DockTaskTray,v=>prefs.DockTaskTray=v,false);
                    Reset(()=>{prefs.ShowDockStart=true;prefs.ShowTaskView=true;prefs.ShowDesktop=true;prefs.ShowSystemControls=false;prefs.DockTaskTray=false;});
                }
                else
                {
                    Toggle("Show Start button in AppBar",prefs.ShowAppBarStart,v=>prefs.ShowAppBarStart=v);Toggle("Show clock in AppBar",prefs.AppBarClock,v=>prefs.AppBarClock=v);
                    Toggle("Show date beside clock",prefs.AppBarDate,v=>prefs.AppBarDate=v,false);Toggle("Show battery percentage",prefs.AppBarBatteryPercentage,v=>prefs.AppBarBatteryPercentage=v,false);
                    Toggle("Show contextual application menus",prefs.AppBarApplicationMenu,v=>prefs.AppBarApplicationMenu=v,false);Toggle("Show Windows notification area access in AppBar",prefs.AppBarTaskTray,v=>prefs.AppBarTaskTray=v,false);
                    Reset(()=>{prefs.ShowAppBarStart=true;prefs.AppBarClock=true;prefs.AppBarDate=false;prefs.AppBarBatteryPercentage=false;prefs.AppBarApplicationMenu=false;prefs.AppBarTaskTray=false;});
                }
                return;
            }
            if(category=="Dock"&&page=="Layout and dragging")
            {
                Slider("Extra icon spacing",prefs.DockIconSpacing,0,20,v=>prefs.DockIconSpacing=(int)v," px");
                Toggle("Allow icon dragging",prefs.DockDragging,v=>prefs.DockDragging=v);
                Slider("Drag removal distance",prefs.DockRemovalThreshold,24,160,v=>prefs.DockRemovalThreshold=(int)v," px");
                Description("Dragging beyond the removal distance removes only the Dock pin.");
                Reset(()=>{prefs.DockIconSpacing=0;prefs.DockDragging=true;prefs.DockRemovalThreshold=48;});
            }
            else if(category=="Dock")
            {
                Toggle("Hover magnification",prefs.Hover,v=>prefs.Hover=v);
                Slider("Hover magnification",prefs.Magnification*100,100,175,v=>prefs.Magnification=v/100,"%");
                Slider("Hover duration",prefs.HoverMilliseconds,0,1000,v=>prefs.HoverMilliseconds=v," ms");
                Toggle("Spring launch bounce",prefs.Bounce,v=>prefs.Bounce=v);
                Slider("Bounce height",prefs.BounceHeight,0,30,v=>prefs.BounceHeight=v," px");
                Slider("Bounce duration",prefs.BounceMilliseconds,100,2000,v=>prefs.BounceMilliseconds=v," ms");
                Reset(()=>{prefs.Hover=true;prefs.Magnification=1.25;prefs.HoverMilliseconds=150;prefs.Bounce=true;prefs.BounceHeight=10;prefs.BounceMilliseconds=650;});
            }
            else if(category=="AppBar"&&page=="Layout and expansion")
            {
                Slider("AppBar height",prefs.AppBarHeight,28,56,v=>prefs.AppBarHeight=(int)v," px");
                Slider("AppBar horizontal padding",prefs.AppBarPadding,2,20,v=>prefs.AppBarPadding=(int)v," px");
                Slider("AppBar corner radius",prefs.AppBarCornerRadius,0,28,v=>prefs.AppBarCornerRadius=(int)v," px");
                Slider("AppBar expansion width",prefs.AppBarExpansionWidth,300,640,v=>prefs.AppBarExpansionWidth=(int)v," px");
                Reset(()=>{prefs.AppBarHeight=36;prefs.AppBarPadding=8;prefs.AppBarCornerRadius=16;prefs.AppBarExpansionWidth=440;});
            }
            else if(category=="AppBar")
            {
                Toggle("Animate AppBar expansion",prefs.AppBarAnimations,v=>prefs.AppBarAnimations=v);
                Slider("AppBar animation duration",prefs.AppBarAnimationMilliseconds,0,600,v=>prefs.AppBarAnimationMilliseconds=(int)v," ms");
                Description("Windows reduced-motion preferences take priority.");
                Reset(()=>{prefs.AppBarAnimations=true;prefs.AppBarAnimationMilliseconds=180;});
            }
            else if(page=="Density and scrolling")
            {
                body.Children.Add(new TextBlock{Text="Menu density",Margin=new Thickness(0,4,0,3)});
                var density=new ComboBox{ItemsSource=new[]{"Compact","Comfortable"},SelectedIndex=prefs.MenuDensity,Margin=new Thickness(0,0,0,10)};AutomationProperties.SetName(density,"Menu density");body.Children.Add(density);
                density.SelectionChanged+=(_,_)=>{if(density.SelectedIndex>=0){prefs.MenuDensity=density.SelectedIndex;Commit();}};
                ResetMenu(density,()=>density.SelectedIndex=0);
                Slider("Scroll speed",prefs.ScrollMultiplier*100,25,300,v=>prefs.ScrollMultiplier=v/100,"%");
                Description("100% follows the Windows wheel-scroll preference. Precision input retains partial wheel movement.");
                Reset(()=>{prefs.MenuDensity=0;prefs.ScrollMultiplier=1;});
            }
            else
            {
                Slider("Menu animation duration",prefs.MenuAnimationMilliseconds,0,400,v=>prefs.MenuAnimationMilliseconds=(int)v," ms");
                Reset(()=>prefs.MenuAnimationMilliseconds=120);
            }
        }
        Render();return root;
    }
}
