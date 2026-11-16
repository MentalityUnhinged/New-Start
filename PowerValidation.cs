using System.Collections.Generic;

namespace NewStart;
internal static class PowerValidation
{
    internal static void Run(Dictionary<string,bool> checks)
    {
        checks["PowerHealthFromReportedCapacity"]=PowerSnapshot.Health(50000,42000)==84;
        checks["PowerMissingCapacityNotInvented"]=PowerSnapshot.Health(null,42000)==null&&PowerSnapshot.Health(0,42000)==null&&PowerSnapshot.Health(50000,null)==null;
        checks["PowerAboveDesignCapacityNotClamped"]=PowerSnapshot.Health(50000,51000)==102;
        checks["PowerUnknownNativeCapacity"]=PowerBatteryDevices.ValidCapacity(uint.MaxValue)==null&&PowerBatteryDevices.ValidCapacity(0)==null;
        checks["PowerRelativeUnitsRemainExplicit"]=AppBarPower.Capacity(500,true).Contains("relative")&&!AppBarPower.Capacity(500,true).Contains("mWh");
        checks["PowerUnknownDurationNotZero"]=AppBarPower.Duration(null)=="Not reported";
        checks["PowerUnknownModeNotInvented"]=AppBarPower.ModeName(-1)=="Not reported"&&AppBarPower.ModeName(99)=="Not reported";
        checks["PowerDisabledSaverNotUnsupported"]=AppBarPower.SaverName(Windows.System.Power.EnergySaverStatus.Disabled).StartsWith("Off")&&AppBarPower.SaverName(Windows.System.Power.EnergySaverStatus.On)=="On";
    }
}
