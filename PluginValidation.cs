using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;

namespace NewStart;
internal static class PluginValidation
{
    internal static void Run(Dictionary<string,bool> checks)
    {
        PluginManifest Link(string target="https://example.com/",string permission="open-web-links")=>new(){Id="example.links",Name="Example links",Provider="links",Permissions=new[]{permission},Actions=new(){new("open","Open example",target)}};
        checks["PluginManifestVersionAccepted"]=PluginManager.Validate(Link(),false)==null;
        var version=Link();version.ApiVersion=PluginManager.ApiVersion+1;checks["PluginFutureApiRejected"]=PluginManager.Validate(version,false)!=null;
        var executable=Link();executable.Provider="arbitrary-dll";checks["PluginExecutableProviderRejected"]=PluginManager.Validate(executable,false)!=null;
        var spoof=Link();spoof.Provider="codex-usage";checks["PluginExternalCannotClaimBundledProvider"]=PluginManager.Validate(spoof,false)!=null;
        checks["PluginWindowsSettingsAllowed"]=PluginManager.Validate(Link("ms-settings:display","open-system-settings"),false)==null;
        checks["PluginExecutableUriRejected"]=PluginManager.Validate(Link("file:///C:/Windows/System32/cmd.exe"),false)!=null;
        checks["PluginScriptUriRejected"]=!PluginManager.SafeTarget("javascript:alert(1)",out _);
        checks["PluginCredentialUriRejected"]=!PluginManager.SafeTarget("https://user:secret@example.com/",out _);
        checks["PluginMissingPermissionRejected"]=PluginManager.Validate(Link("ms-settings:display"),false)!=null;
        var duplicates=Link();duplicates.Actions.Add(new("open","Duplicate action","https://example.com/another"));checks["PluginDuplicateCapabilityRejected"]=PluginManager.Validate(duplicates,false)!=null;
        var malformed=Link();malformed.Actions[0]=new("bad:scope","Invalid scope","https://example.com/");checks["PluginCapabilityScopeValidated"]=PluginManager.Validate(malformed,false)!=null;
        using var source=JsonDocument.Parse("""
        {"rateLimits":{"primary":{"usedPercent":99,"windowDurationMins":60}},"rateLimitsByLimitId":{"codex":{"limitName":"Codex","planType":"test","primary":{"usedPercent":25,"windowDurationMins":300,"resetsAt":2000000000},"secondary":{"usedPercent":40,"windowDurationMins":10080}},"review":{"limitName":"Review","primary":{"usedPercent":10,"windowDurationMins":1440}}},"rateLimitResetCredits":{"availableCount":2}}
        """);
        using var activity=JsonDocument.Parse("""{"summary":{"lifetimeTokens":1234,"peakDailyTokens":500,"currentStreakDays":3}}""");var parsed=CodexUsageClient.Parse(source.RootElement,activity.RootElement);
        checks["CodexMultipleBucketsPreferredWithoutLegacyDuplication"]=parsed.Windows.Count==3&&parsed.Windows.All(window=>window.UsedPercent!=99);
        checks["CodexRemainingFromReportedValues"]=parsed.Windows[0].RemainingPercent==75&&parsed.Windows[0].WindowMinutes==300&&parsed.Windows[0].ResetsAt?.ToUnixTimeSeconds()==2000000000;
        checks["CodexOptionalSummaryReported"]=parsed.LifetimeTokens==1234&&parsed.PeakDailyTokens==500&&parsed.CurrentStreakDays==3&&parsed.ResetCredits==2;
        using var legacy=JsonDocument.Parse("""{"rateLimits":{"primary":{"usedPercent":120,"windowDurationMins":60},"secondary":{"usedPercent":-1}}}""");var legacyResult=CodexUsageClient.Parse(legacy.RootElement);
        checks["CodexLegacyAndOveragePreserved"]=legacyResult.Windows.Count==1&&legacyResult.Windows[0].UsedPercent==120&&legacyResult.Windows[0].RemainingPercent==0;
        checks["CodexMissingMetricsNotFabricated"]=legacyResult.LifetimeTokens==null&&legacyResult.ResetCredits==null&&legacyResult.Plan==null;
        using var missing=JsonDocument.Parse("null");checks["CodexMissingPayloadGraceful"]=CodexUsageClient.Parse(missing.RootElement).Windows.Count==0;
        using var invalid=JsonDocument.Parse("""{"rateLimits":{"primary":{"usedPercent":1e300},"secondary":{"usedPercent":"unknown"}}}""");checks["CodexInvalidMetricsExcluded"]=CodexUsageClient.Parse(invalid.RootElement).Windows.Count==0;
        string temporary=Path.Combine(Preferences.Folder,"plugin-validation-"+Guid.NewGuid().ToString("N"));
        try
        {
            string pluginFolder=Path.Combine(temporary,"Plugins","example");Directory.CreateDirectory(pluginFolder);string manifest=Path.Combine(pluginFolder,"plugin.json");File.WriteAllText(manifest,JsonSerializer.Serialize(Link()));
            using(var manager=new PluginManager(Dispatcher.CurrentDispatcher,temporary))
            {
                checks["PluginBundledStartsDisabled"]=manager.Entries.Single(entry=>entry.Manifest.Id==PluginManager.CodexId).Configuration.Enabled==false;
                checks["PluginPermissionConsentEnforced"]=!manager.SetEnabled("example.links",true)&&manager.ComponentsFor("AppBar").Count==0;
                checks["PluginExplicitPermissionLoadsCapabilities"]=manager.SetEnabled("example.links",true,true)&&manager.Capabilities.Count==1&&manager.ComponentsFor("AppBar").Count==1;
                manager.Configure("example.links",configuration=>{configuration.Dock=true;configuration.AppBar=false;configuration.RefreshMinutes=999;});checks["PluginIndependentPlacementAndRangeValidation"]=manager.ComponentsFor("AppBar").Count==0&&manager.ComponentsFor("Dock").Count==1&&manager.Entries.Single(entry=>entry.Manifest.Id=="example.links").Configuration.RefreshMinutes==60;
                manager.SetEnabled("example.links",false);checks["PluginDisableUnloadsComponentsAndCapabilities"]=manager.ComponentsFor("Dock").Count==0&&manager.Capabilities.Count==0;
            }
            using(var restored=new PluginManager(Dispatcher.CurrentDispatcher,temporary))
            {
                var saved=restored.Entries.Single(entry=>entry.Manifest.Id=="example.links");checks["PluginSettingsPersist"]=saved.Configuration.Dock&&!saved.Configuration.AppBar&&!saved.Configuration.Enabled&&saved.Configuration.GrantedPermissions.Contains("open-web-links");
                checks["PluginRemovalReversible"]=restored.Remove("example.links")&&!File.Exists(manifest)&&File.Exists(manifest+".removed");restored.Discover();checks["PluginRemovalSurvivesDiscovery"]=restored.Entries.All(entry=>entry.Manifest.Id!="example.links");
                restored.Remove(PluginManager.CodexId);
            }
            using var removed=new PluginManager(Dispatcher.CurrentDispatcher,temporary);checks["PluginBundledRemovalPersistsAndRevokesConsent"]=removed.Entries.Single(entry=>entry.Manifest.Id==PluginManager.CodexId).Configuration is {Removed:true,Enabled:false,GrantedPermissions.Count:0};
        }
        finally
        {
            string expectedPrefix=Path.Combine(Preferences.Folder,"plugin-validation-");if(Path.GetFullPath(temporary).StartsWith(Path.GetFullPath(expectedPrefix),StringComparison.OrdinalIgnoreCase)&&Directory.Exists(temporary))Directory.Delete(temporary,true);
        }
    }
}
