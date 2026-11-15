using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace NewStart;
public sealed partial class DockWindow
{
    internal async Task VerifyAppBar()
    {
        suppressSettingsPersistence=true;
        var saved=(prefs.AppBarEnabled,prefs.AppBarAllMonitors,prefs.BorderlessWindows,prefs.Edge,prefs.Alignment,prefs.AppBarAnimations,prefs.AppBarTransparency,prefs.AppBarBlur,prefs.Theme,prefs.AppBarMediaHover);
        var savedColour=(prefs.AppBarColourSource,prefs.AppBarColour);
        var checks=new Dictionary<string,bool>();var details=new List<object>();
        var baseline=DesktopDisplays.All().ToDictionary(display=>display.Name,display=>DesktopDisplays.Work(display.Handle));
        try
        {
            prefs.AppBarEnabled=true;prefs.AppBarAllMonitors=true;prefs.BorderlessWindows=false;prefs.Edge=DockEdge.Bottom;prefs.AppBarAnimations=true;prefs.AppBarMediaHover=false;UpdateAppBars();Refresh(true);UpdateLayout();Position();
            await Task.Delay(600);
            checks["DefaultOff"]=!new Preferences().AppBarEnabled;
            checks["HostCreated"]=appBars!=null;
            checks["OneHostPerDisplay"]=appBars?.Windows.Count()==DesktopDisplays.All().Count;
            checks["StartMovedOutOfDock"]=!row.Children.OfType<System.Windows.Controls.Button>().Any(button=>button.ToolTip?.ToString()=="Start");
            if(appBars!=null)foreach(var bar in appBars.Windows.ToArray())
            {
                var resting=DesktopDisplays.Work(bar.Display.Handle);var reserved=bar.ReservedBounds;string key=bar.Display.Name;
                checks[key+"ReservationFixed"]=reserved.Bottom-reserved.Top==(int)Math.Ceiling(AdaptiveAppBarWindow.RestHeight*System.Windows.Media.VisualTreeHelper.GetDpi(bar).DpiScaleY);
                checks[key+"WorkAreaBelowBar"]=resting.Top>=reserved.Bottom;
                checks[key+"ClockCenteredOnDisplay"]=bar.ClockIsCentered;
                bar.Expand(AppBarSection.Audio);bar.RefreshContent();await Task.Delay(700);
                checks[key+"AudioSurvivesMetadataRefresh"]=bar.Section==AppBarSection.Audio;bar.Collapse(false);
                bar.Toggle(AppBarSection.Audio);await Task.Delay(450);checks[key+"AudioClickStaysOpen"]=bar.Section==AppBarSection.Audio;
                bar.Toggle(AppBarSection.Audio);await Task.Delay(300);checks[key+"AudioSecondClickCloses"]=bar.Section==AppBarSection.None;
                foreach(var section in Enum.GetValues<AppBarSection>().Where(section=>section!=AppBarSection.None))
                {
                    bar.Expand(section);await Task.Delay(350);
                    var expanded=DesktopDisplays.Work(bar.Display.Handle);
                    checks[key+section+"OverlayOnly"]=Same(resting,expanded)&&bar.ActualHeight>AdaptiveAppBarWindow.RestHeight;
                    checks[key+section+"ContinuousMaterial"]=bar.HasContinuousMaterial;
                    if(section==AppBarSection.Settings)checks[key+"SettingsHasNoSeparateWindow"]=!Application.Current.Windows.Cast<Window>().Any(window=>window.IsVisible&&window.Title=="New Start settings (Beta)");
                    bar.Collapse();await Task.Delay(300);
                    checks[key+section+"Collapses"]=bar.Section==AppBarSection.None&&Math.Abs(bar.ActualHeight-AdaptiveAppBarWindow.RestHeight)<1;
                    if(!checks[key+section+"Collapses"])details.Add(new{Failure="Collapse",Section=bar.Section.ToString(),Requested=section.ToString(),bar.Height,bar.ActualHeight,bar.SurfaceUpdates,bar.Repositions});
                }
                bar.Expand(AppBarSection.Media);bar.Expand(AppBarSection.Audio);bar.Collapse();bar.Expand(AppBarSection.Clock);await Task.Delay(300);
                checks[key+"InterruptibleTransition"]=bar.Section==AppBarSection.Clock&&Same(resting,DesktopDisplays.Work(bar.Display.Handle));bar.Collapse(false);
                double menuTransparency=prefs.StartTransparency,menuBlur=prefs.MenuBlur;
                foreach(int theme in new[]{1,2})foreach(double transparency in new[]{0d,1d})foreach(double blur in new[]{0d,20d})
                {
                    prefs.AppBarColourSource=0;prefs.Theme=theme;prefs.AppBarTransparency=transparency;prefs.AppBarBlur=blur;bar.UpdateAppearance();
                    bool effects=NativeBackdrop.TransparencyAllowed();checks[key+$"Theme{theme}T{transparency}B{blur}"]=bar.BlurVisible==(effects&&blur>0&&transparency>0&&transparency<1)&&Math.Abs(bar.MaterialOpacity-(1-transparency))<.001;
                    checks[key+$"PureSystemColour{theme}T{transparency}B{blur}"]=bar.MaterialColour==(theme==1?System.Windows.Media.Colors.White:System.Windows.Media.Colors.Black);
                }
                prefs.AppBarColourSource=1;bar.Expand(AppBarSection.Clock);await Task.Delay(300);
                foreach(string colour in new[]{"#7A36CE","#FFFFFF","#000000"})foreach(double transparency in new[]{0d,.5,1d})
                {
                    prefs.AppBarColour=colour;prefs.AppBarTransparency=transparency;bar.UpdateAppearance();SurfaceAppearance.TryHex(colour,out var expected);
                    checks[key+$"Custom{colour}T{transparency}"]=bar.MaterialColour==expected&&Math.Abs(bar.MaterialOpacity-(1-transparency))<.001&&bar.HasContinuousMaterial;
                }
                bar.Collapse(false);(prefs.AppBarColourSource,prefs.AppBarColour)=savedColour;
                checks[key+"IndependentAppearance"]=prefs.StartTransparency==menuTransparency&&prefs.MenuBlur==menuBlur;
                bar.Expand(AppBarSection.Start);await Task.Delay(350);
                checks[key+"StartSharesAppBarSurface"]=bar.Section==AppBarSection.Start&&bar.HasContinuousMaterial&&startMenu?.IsVisible==false;bar.Collapse(false);
                details.Add(new{Display=key,Dpi=System.Windows.Media.VisualTreeHelper.GetDpi(bar).DpiScaleX,Bounds=reserved,Audio=appBars.Audio.Output,AudioError=appBars.Audio.Error,Media=appBars.Media.Title,MediaError=appBars.Media.Error,Network=appBars.Network.Status,Bluetooth=appBars.Bluetooth.Status});
            }
            if(appBars!=null)
            {
                foreach(double blur in new[]{0d,20d})
                {
                    prefs.AppBarBlur=blur;foreach(var bar in appBars.Windows){bar.Collapse(false);bar.UpdateAppearance();}
                    using var settling=System.Diagnostics.Process.GetCurrentProcess();
                    int quietSamples=0,settleSeconds=0;
                    while(quietSamples<3&&settleSeconds<20){settling.Refresh();double start=settling.TotalProcessorTime.TotalSeconds;await Task.Delay(1000);settling.Refresh();quietSamples=settling.TotalProcessorTime.TotalSeconds-start<.1?quietSamples+1:0;settleSeconds++;}
                    using var process=System.Diagnostics.Process.GetCurrentProcess();await Task.Delay(300);process.Refresh();double cpu=process.TotalProcessorTime.TotalSeconds;
                    long changes=appBars.Applications.Notifications,refreshes=appBars.Applications.Refreshes,wifi=appBars.Network.Refreshes,notifications=appBars.Network.Notifications,surfaces=appBars.Windows.Sum(bar=>bar.SurfaceUpdates),positions=appBars.Windows.Sum(bar=>bar.Repositions);
                    long audioReads=appBars.Audio.Refreshes,mediaReads=appBars.Media.Refreshes,bluetooth=appBars.Bluetooth.Notifications;
                    var threads=process.Threads.Cast<System.Diagnostics.ProcessThread>().ToDictionary(thread=>thread.Id,thread=>thread.TotalProcessorTime.TotalSeconds);
                    await Task.Delay(2000);process.Refresh();
                    var threadCpu=process.Threads.Cast<System.Diagnostics.ProcessThread>().Where(thread=>threads.ContainsKey(thread.Id)).Select(thread=>new{thread.Id,Cpu=thread.TotalProcessorTime.TotalSeconds-threads[thread.Id]}).OrderByDescending(thread=>thread.Cpu).Take(3).ToArray();
                    details.Add(new{Blur=blur,SettleSeconds=settleSeconds,ReachedQuietState=quietSamples==3,IdleSampleSeconds=2,IdleCpuSeconds=process.TotalProcessorTime.TotalSeconds-cpu,WorkingSetMB=Math.Round(process.WorkingSet64/1048576d,1),ActiveNotifications=appBars.Applications.Notifications-changes,ActiveRefreshes=appBars.Applications.Refreshes-refreshes,WifiRefreshes=appBars.Network.Refreshes-wifi,WifiNotifications=appBars.Network.Notifications-notifications,AudioReads=appBars.Audio.Refreshes-audioReads,MediaReads=appBars.Media.Refreshes-mediaReads,BluetoothNotifications=appBars.Bluetooth.Notifications-bluetooth,SurfaceUpdates=appBars.Windows.Sum(bar=>bar.SurfaceUpdates)-surfaces,Repositions=appBars.Windows.Sum(bar=>bar.Repositions)-positions,Threads=threadCpu});
                }
            }
            appBars?.Dispose();appBars=null;await Task.Delay(300);
            checks["DisableReleasesReservation"]=DesktopDisplays.All().All(display=>baseline.TryGetValue(display.Name,out var rect)&&DesktopDisplays.Work(display.Handle).Top==rect.Top);
        }
        catch(Exception e){checks["UnhandledFailure"]=false;details.Add(new{Error=e.ToString()});}
        finally
        {
            appBars?.Dispose();appBars=null;(prefs.AppBarEnabled,prefs.AppBarAllMonitors,prefs.BorderlessWindows,prefs.Edge,prefs.Alignment,prefs.AppBarAnimations,prefs.AppBarTransparency,prefs.AppBarBlur,prefs.Theme,prefs.AppBarMediaHover)=saved;
            (prefs.AppBarColourSource,prefs.AppBarColour)=savedColour;
            UpdateAppBars();Refresh(true);UpdateLayout();Position();
            File.WriteAllText(Path.Combine(Preferences.Folder,"appbar-verification.json"),JsonSerializer.Serialize(new{Checks=checks,Displays=details},new JsonSerializerOptions{WriteIndented=true}));Close();suppressSettingsPersistence=false;
        }
    }
    static bool Same(Native.Rect a,Native.Rect b)=>a.Left==b.Left&&a.Top==b.Top&&a.Right==b.Right&&a.Bottom==b.Bottom;
    static int DisplayLeft(AdaptiveAppBarWindow bar)=>bar.Display.Bounds.Left;
    internal void PreviewAppBar(){prefs.AppBarEnabled=true;prefs.BorderlessWindows=false;if(prefs.Edge==DockEdge.Top)prefs.Edge=DockEdge.Bottom;UpdateAppBars();if(appBars!=null)foreach(var bar in appBars.Windows){bar.ShowInTaskbar=true;bar.Testing=false;bar.RefreshContent();}Refresh(true);Position();}
}
