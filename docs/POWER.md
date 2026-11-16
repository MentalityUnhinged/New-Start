# Power and battery panel

The AppBar power panel uses real Windows telemetry. The battery indicator still appears only when `GetSystemPowerStatus` reports a battery; the expanded panel can also be opened on a desktop to access system power settings and uptime.

`AppBarPower` is a shared service with a dispatcher, a `Current` snapshot, `Changed`, `RefreshAsync` and `BuildContent`. The content builder returns ordinary WPF controls for the existing connected AppBar expansion. It subscribes/unsubscribes its live rendering when mounted/unmounted. The root host should create one service per AppBar manager, forward existing native power broadcasts to `RefreshAsync`, and dispose it with the host.

## Sources and truthful unavailable states

- Percentage, mains/charging state and estimated remaining battery time use `GetSystemPowerStatus`; remaining time also uses Windows PowerManager when reported. Charging or mains-powered systems do not display an invented discharge estimate.
- Design/full-charge capacities use `Windows.Devices.Power.Battery.AggregateBattery.GetReport`. Battery health is the reported full-charge capacity divided by reported design capacity. Missing/zero values produce **Not reported**, not a zero-percent health score. Reported capacity above design is preserved rather than silently capped.
- Per-pack capacity and cycle count use the documented battery miniport interface (`GUID_DEVINTERFACE_BATTERY`, query tag and query information). Cycle count zero means unsupported under the Windows contract, so it is displayed as **Not reported**. Device serial numbers/unique identifiers are neither displayed nor persisted.
- Relative battery capacity units are explicitly labelled; they are never summed with absolute mWh values. Multiple packs retain separate cycle counts rather than fabricating a combined cycle count. Aggregated capacities are used only when every native pack supplies complete, compatible absolute telemetry.
- System uptime uses `GetTickCount64`. It is refreshed when the panel or power state refreshes, not by a permanent uptime timer.
- Effective power mode uses `PowerRegisterForEffectivePowerModeNotifications`; energy saver uses Windows PowerManager. Its `Disabled` state means saver is disabled or the device is on mains, not that the hardware is unsupported. Unavailable APIs remain labelled unavailable.
- Available power plans are enumerated through PowrProf. Selecting a plan calls the supported `PowerSetActiveScheme` API and verifies the active GUID afterward. This is labelled **power plan**, distinct from the modern Windows power-mode slider. Energy-saver and modern power-mode modification use working Windows Settings shortcuts because this implementation does not rely on an undocumented overlay setter.

## Events and resources

Battery reports and PowerManager events coalesce into asynchronous reads; effective-power-mode notifications also trigger reads. Native `WM_POWERBROADCAST` notifications are supported through the existing AppBar host registrations. At most one background read runs at once. Static device capacities/cycle counts are cached for five minutes, and an explicit refresh invalidates that cache. Battery I/O uses overlapped operations with cancellation; buffers remain alive until cancellation completes. A broken driver can delay its background operation, but it cannot block the WPF UI thread or create an accumulating polling loop.

No WMI service, external utility, administrator rights, new package dependency, power-policy registry mutation or telemetry fabrication is required. Unsupported batteries/firmware and restricted access produce partial reports plus working Settings links. Some firmware supplies incomplete or inaccurate design capacity.

## Validation

`PowerValidation.Run` checks health arithmetic, unavailable capacities, above-design reports, native unknown values, relative-unit labelling, unknown mode and unknown time. Live charging/discharging transitions, multiple physical batteries, cycle-capable hardware and power-plan changes require device acceptance; do not change a user's active plan merely to test the UI.

Primary references: [BatteryReport](https://learn.microsoft.com/en-us/uwp/api/windows.devices.power.batteryreport), [BATTERY_INFORMATION](https://learn.microsoft.com/en-us/windows/win32/power/battery-information-str), [query information IOCTL](https://learn.microsoft.com/en-us/windows/win32/power/ioctl-battery-query-information), [effective mode notifications](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powerregisterforeffectivepowermodenotifications), [power scheme management](https://learn.microsoft.com/en-us/windows/win32/power/managing-power-schemes).
