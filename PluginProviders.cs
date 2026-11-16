using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NewStart;
// This contract is a trusted host extension point. Manifest files cannot register executable providers.
internal interface IPluginProvider:IDisposable
{
    Task<PluginSnapshot> ReadAsync(PluginConfiguration configuration,CancellationToken cancellation);
}
internal sealed record PluginProviderRegistration(string Id,string[] Permissions,bool AllowExternalManifests,Func<PluginManifest,IPluginProvider> Create);
internal static class PluginProviderRegistry
{
    static readonly Dictionary<string,PluginProviderRegistration> registrations=new(StringComparer.Ordinal)
    {
        ["links"]=new("links",new[]{"open-web-links","open-system-settings"},true,manifest=>new PluginLinksProvider(manifest.Name)),
        ["codex-usage"]=new("codex-usage",new[]{"codex-account-usage","launch-codex-app-server"},false,_=>new CodexUsagePluginProvider())
    };
    internal static bool TryGet(string? id,out PluginProviderRegistration registration)=>registrations.TryGetValue(id??"",out registration!);
    // Future audited, compiled provider modules can register before constructing PluginManager.
    internal static void RegisterTrusted(PluginProviderRegistration registration)
    {
        if(string.IsNullOrWhiteSpace(registration.Id)||registrations.ContainsKey(registration.Id))throw new ArgumentException("Duplicate or invalid plugin provider identifier.");registrations.Add(registration.Id,registration);
    }
}
internal sealed class PluginLinksProvider:IPluginProvider
{
    readonly string name;internal PluginLinksProvider(string name){this.name=name;}
    public Task<PluginSnapshot> ReadAsync(PluginConfiguration configuration,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();return Task.FromResult(new PluginSnapshot(name,"Available plugin actions",Array.Empty<PluginRow>(),DateTimeOffset.Now));
    }
    public void Dispose(){}
}
internal sealed class CodexUsagePluginProvider:IPluginProvider
{
    public async Task<PluginSnapshot> ReadAsync(PluginConfiguration configuration,CancellationToken cancellation)
    {
        var usage=await CodexUsageClient.Read(configuration.CodexExecutable,cancellation);var rows=new List<PluginRow>();if(usage.Plan!=null)rows.Add(new("Reported plan",usage.Plan));
        foreach(var window in usage.Windows)
        {
            string reset=window.ResetsAt is {} date?" · resets "+date.LocalDateTime.ToString("g"):" · reset unavailable";
            rows.Add(new(window.Name+" · "+window.Duration,$"{window.RemainingPercent:0}% remaining ({window.UsedPercent:0}% used)"+reset,Math.Clamp(window.UsedPercent,0,100)));
        }
        if(usage.LifetimeTokens is long tokens)rows.Add(new("Reported lifetime tokens",tokens.ToString("N0")));if(usage.PeakDailyTokens is long peak)rows.Add(new("Reported peak daily tokens",peak.ToString("N0")));if(usage.CurrentStreakDays is long streak)rows.Add(new("Reported activity streak",streak+" days"));if(usage.ResetCredits is int credits)rows.Add(new("Available rate-limit resets",credits.ToString()));if(usage.CreditBalance!=null)rows.Add(new("Reported workspace credit balance",usage.CreditBalance));
        string label=usage.Windows.FirstOrDefault() is {} first?$"Codex {first.RemainingPercent:0}%":"Codex —";return new(label,usage.Windows.Count>0?usage.Notice:"No allowance windows were reported. "+usage.Notice,rows,DateTimeOffset.Now);
    }
    public void Dispose(){}
}
