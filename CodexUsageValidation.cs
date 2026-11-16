using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace NewStart;
internal static class CodexUsageValidation
{
    // Explicit read-only diagnostic; does not enable the plugin or persist permission grants.
    internal static async Task Run()
    {
        object report;bool success=false;
        try
        {
            using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(22));var result=await CodexUsageClient.Read(null,cancellation.Token);
            success=result.Windows.Count>0;report=new{CliAvailable=true,ReportedWindows=result.Windows.Count,ReportedBucketCount=result.Windows.Select(window=>window.Bucket).Distinct().Count(),HasResetTimes=result.Windows.Any(window=>window.ResetsAt!=null),HasWindowDurations=result.Windows.Any(window=>window.WindowMinutes!=null),HasOptionalActivity=result.LifetimeTokens!=null,NoCredentialsCopied=true,success};
        }
        catch(InvalidOperationException error){report=new{success,Status=error.Message};}
        catch{report=new{success,Status="The authorised Codex service could not be queried in this environment."};}
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"codex-usage-verification.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        if(!success)throw new InvalidOperationException("Live Codex usage availability was not verified.");
    }
}
