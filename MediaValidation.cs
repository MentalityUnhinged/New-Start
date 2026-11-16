using System;
using System.Collections.Generic;

namespace NewStart;
internal static class MediaValidation
{
    internal static void Run(Dictionary<string,bool> checks)
    {
        var timeline=new MediaTimeline(TimeSpan.Zero,TimeSpan.FromMinutes(5),TimeSpan.FromMinutes(1),TimeSpan.FromSeconds(10),TimeSpan.FromMinutes(4),DateTimeOffset.UtcNow);
        checks["MediaSeekUsesReportedMinimum"]=AppBarMedia.SeekTarget(timeline,TimeSpan.Zero)==TimeSpan.FromSeconds(10).Ticks;
        checks["MediaSeekUsesReportedMaximum"]=AppBarMedia.SeekTarget(timeline,TimeSpan.FromHours(1))==TimeSpan.FromMinutes(4).Ticks;
        checks["MediaSeekPreservesTickPrecision"]=AppBarMedia.SeekTarget(timeline,TimeSpan.FromTicks(TimeSpan.FromMinutes(2).Ticks+123))==TimeSpan.FromMinutes(2).Ticks+123;
        checks["MediaMissingTimelineNotSeekable"]=AppBarMedia.SeekTarget(null,TimeSpan.Zero)==null;
        checks["MediaUnknownSeekRangeNotInvented"]=AppBarMedia.SeekTarget(timeline with{MinSeekTime=TimeSpan.Zero,MaxSeekTime=TimeSpan.Zero},TimeSpan.FromMinutes(1))==null;
        checks["MediaInvalidDurationNotSeekable"]=AppBarMedia.SeekTarget(timeline with{EndTime=TimeSpan.Zero},TimeSpan.Zero)==null;
        checks["MediaSeekRangeIntersectsItemDuration"]=AppBarMedia.SeekTarget(timeline with{MinSeekTime=TimeSpan.FromSeconds(-10),MaxSeekTime=TimeSpan.FromHours(1)},TimeSpan.FromHours(1))==TimeSpan.FromMinutes(5).Ticks;
    }
}
