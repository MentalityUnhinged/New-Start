using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NewStart;
internal sealed record CodexUsageWindow(string Bucket,string Name,double UsedPercent,int? WindowMinutes,DateTimeOffset? ResetsAt)
{
    internal double RemainingPercent=>Math.Max(0,100-UsedPercent);
    internal string Duration=>WindowMinutes is int value?(value%1440==0?$"{value/1440} day":value%60==0?$"{value/60} hour":$"{value} minute"):"Reported window";
}
internal sealed record CodexUsageResult(IReadOnlyList<CodexUsageWindow> Windows,string? Plan,long? LifetimeTokens,long? PeakDailyTokens,long? CurrentStreakDays,int? ResetCredits,string? CreditBalance,string Notice);
internal static class CodexUsageClient
{
    internal static string? FindExecutable(string? configured=null)
    {
        if(!string.IsNullOrWhiteSpace(configured))return Path.IsPathFullyQualified(configured)&&Path.GetFileName(configured).Equals("codex.exe",StringComparison.OrdinalIgnoreCase)&&File.Exists(configured)?Path.GetFullPath(configured):null;
        foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries))
        {
            try{string candidate=Path.Combine(folder,"codex.exe");if(File.Exists(candidate))return Path.GetFullPath(candidate);}catch{}
        }
        // The official npm launcher can be .cmd/.ps1: resolve its installed native payload without invoking a shell.
        string npm=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"npm","node_modules","@openai","codex","node_modules","@openai");
        foreach(string arch in new[]{"codex-win32-x64","codex-win32-arm64"})
        {
            string target=arch.EndsWith("arm64",StringComparison.Ordinal)?"aarch64-pc-windows-msvc":"x86_64-pc-windows-msvc";
            string candidate=Path.Combine(npm,arch,"vendor",target,"bin","codex.exe");if(File.Exists(candidate))return candidate;
        }
        return null;
    }
    internal static async Task<CodexUsageResult> Read(string? configured,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        string executable=FindExecutable(configured)??throw new InvalidOperationException("Codex CLI was not found. Install the official CLI or select its codex.exe in plugin settings.");
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{"-s","read-only","-a","on-request","app-server"})info.ArgumentList.Add(arg);
        using var process=Process.Start(info)??throw new InvalidOperationException("Codex app-server could not start.");
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancellation);lifetime.CancelAfter(TimeSpan.FromSeconds(18));
        // Drain but never retain/log stderr: a configured Codex server can be chatty and may include private paths.
        var stderr=Drain(process.StandardError,lifetime.Token);
        try
        {
            await Request(process,1,"initialize",new{clientInfo=new{name="new_start_usage",title="New Start usage plugin",version="1.0.0"}},lifetime.Token);
            await Send(process,new{method="initialized",@params=new{}},lifetime.Token);
            var limits=await Request(process,2,"account/rateLimits/read",new{},lifetime.Token);
            JsonElement? usage=null;string notice="Only Codex service usage is reported. General ChatGPT message limits and OpenAI API billing are separate.";
            using(var optional=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                optional.CancelAfter(2200);
                try{usage=await Request(process,3,"account/usage/read",new{},optional.Token);}catch(OperationCanceledException) when(!lifetime.IsCancellationRequested){notice+=" Token-activity summaries are unavailable from this server.";}catch(InvalidOperationException){notice+=" Token-activity summaries are unavailable from this server.";}
            }
            return Parse(limits,usage,notice);
        }
        catch(OperationCanceledException) when(!cancellation.IsCancellationRequested){throw new InvalidOperationException("Codex usage lookup timed out. Existing authentication or the service may be unavailable.");}
        finally
        {
            lifetime.Cancel();try{process.StandardInput.Close();}catch{}try{if(!process.HasExited)process.Kill(true);}catch{}
            try{await stderr;}catch{}
        }
    }
    static async Task Drain(StreamReader reader,CancellationToken cancellation){var buffer=new char[1024];try{while(await reader.ReadAsync(buffer.AsMemory(),cancellation)>0){}}catch(OperationCanceledException){}catch(IOException){}}
    static async Task Send(Process process,object request,CancellationToken cancellation){await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(),cancellation);await process.StandardInput.FlushAsync(cancellation);}
    static async Task<JsonElement> Request(Process process,int id,string method,object parameters,CancellationToken cancellation)
    {
        await Send(process,new{id,method,@params=parameters},cancellation);
        for(int count=0;count<300;count++)
        {
            string? line=await process.StandardOutput.ReadLineAsync(cancellation);if(line==null)throw new InvalidOperationException("Codex app-server exited without a usage response.");if(line.Length>1024*1024)throw new InvalidOperationException("Codex returned an oversized response.");
            JsonDocument message;try{message=JsonDocument.Parse(line);}catch(JsonException){continue;}
            using(message)
            {
                var root=message.RootElement;
                if(root.ValueKind!=JsonValueKind.Object)continue;
                if(root.TryGetProperty("id",out var received)&&received.ValueKind==JsonValueKind.Number&&received.TryGetInt32(out int value)&&value==id)
                {
                    if(root.TryGetProperty("error",out var error))
                    {
                        string detail=Text(error,"message")??"";
                        if(detail.Contains("auth",StringComparison.OrdinalIgnoreCase)||detail.Contains("401",StringComparison.Ordinal))throw new InvalidOperationException("Codex authentication is unavailable. Sign in using the official Codex CLI, then refresh this plugin.");
                        if(detail.Contains("API key",StringComparison.OrdinalIgnoreCase)||detail.Contains("ChatGPT",StringComparison.OrdinalIgnoreCase)&&detail.Contains("only",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("This Codex sign-in does not expose ChatGPT-backed Codex allowance data. API billing is separate.");
                        throw new InvalidOperationException("The installed Codex server could not provide "+(method=="account/usage/read"?"token activity.":"usage limits. Update/sign in through Codex, then retry."));
                    }
                    if(root.TryGetProperty("result",out var result))return result.Clone();throw new InvalidOperationException("Codex returned no usage result.");
                }
                // Read-only probe must never execute or approve server-initiated operations.
                if(root.TryGetProperty("method",out _)&&root.TryGetProperty("id",out var serverId))await Send(process,new{id=serverId.Clone(),error=new{code=-32601,message="New Start usage client does not support interactive requests."}},cancellation);
            }
        }
        throw new InvalidOperationException("Codex returned too many unrelated notifications.");
    }
    internal static CodexUsageResult Parse(JsonElement limits,JsonElement? usage=null,string? notice=null)
    {
        var windows=new List<CodexUsageWindow>();string? plan=null,balance=null;var buckets=new List<(string Id,JsonElement Value)>();
        if(limits.ValueKind==JsonValueKind.Object&&limits.TryGetProperty("rateLimitsByLimitId",out var multiple)&&multiple.ValueKind==JsonValueKind.Object)buckets.AddRange(multiple.EnumerateObject().Where(p=>p.Value.ValueKind==JsonValueKind.Object).Select(p=>(p.Name,p.Value)));
        if(buckets.Count==0&&limits.ValueKind==JsonValueKind.Object&&limits.TryGetProperty("rateLimits",out var single)&&single.ValueKind==JsonValueKind.Object)buckets.Add((Text(single,"limitId")??"codex",single));
        foreach(var bucket in buckets.OrderBy(item=>item.Id=="codex"?0:1))
        {
            string label=Clean(Text(bucket.Value,"limitName")??bucket.Id,80);plan??=Text(bucket.Value,"planType");
            if(bucket.Value.TryGetProperty("credits",out var credits)&&credits.ValueKind==JsonValueKind.Object)balance??=Text(credits,"balance");
            foreach(string slot in new[]{"primary","secondary"})
            {
                if(!bucket.Value.TryGetProperty(slot,out var window)||window.ValueKind!=JsonValueKind.Object||!Number(window,"usedPercent",out double used)||used<0||used>10000)continue;
                int? duration=Number(window,"windowDurationMins",out double minutes)&&minutes>0&&minutes<=525600?(int)minutes:null;
                DateTimeOffset? reset=null;if(Number(window,"resetsAt",out double timestamp)){try{reset=DateTimeOffset.FromUnixTimeSeconds(checked((long)timestamp));}catch(ArgumentOutOfRangeException){}catch(OverflowException){}}
                windows.Add(new(bucket.Id,label,used,duration,reset));
            }
        }
        int? resets=null;if(limits.ValueKind==JsonValueKind.Object&&limits.TryGetProperty("rateLimitResetCredits",out var resetCredits)&&Integer(resetCredits,"availableCount") is long count&&count<=int.MaxValue)resets=(int)count;
        JsonElement summary=default;if(usage is {} activity&&activity.ValueKind==JsonValueKind.Object&&activity.TryGetProperty("summary",out var source))summary=source;
        return new(windows,CleanNullable(plan),Integer(summary,"lifetimeTokens"),Integer(summary,"peakDailyTokens"),Integer(summary,"currentStreakDays"),resets,CleanNullable(balance),notice??"Codex service data; ChatGPT chat allowances and OpenAI API billing are not reported.");
    }
    static long? Integer(JsonElement element,string name)=>element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out long number)&&number>=0?number:null;
    static bool Number(JsonElement element,string name,out double number){number=0;return element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out number)&&double.IsFinite(number);}
    static string? Text(JsonElement element,string name)=>element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    static string? CleanNullable(string? value)=>string.IsNullOrWhiteSpace(value)?null:Clean(value,80);
    static string Clean(string value,int length)=>new(value.Where(c=>!char.IsControl(c)).Take(length).ToArray());
}
