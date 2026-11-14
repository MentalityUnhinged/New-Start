# Architecture

New Start is a single-instance, per-user .NET 8 WPF desktop process. A small C++ DLL supplies Windows Composition backdrop surfaces. The dock and menus remain native desktop windows; Explorer is never replaced or injected into.

The dock enumerates eligible top-level app windows on a one-second timer and rebuilds when its task/pin signature changes. Application menus retain their current controls while open. Shell identities merge local/read-only imported pins with running apps. Grouping is approximate for some packaged/hosted applications.

`DockGeometry` contains edge/alignment and anchored-menu calculations. `DockReservation` registers a hidden Shell AppBar on the selected primary-monitor edge and negotiates the occupied strip. Changing edges unregisters the old reservation. The visible dock is independently aligned inside the negotiated bounds, excluding its transparent hover/bounce room from reserved thickness. Menus overlay the work area. Top-centre Start is horizontally screen-centred; other menus follow the button and clamp to the work area.

Start prepares a cached app snapshot and warms pinned icons before interaction. Shell enumeration, Start-layout export and icon extraction run on background STA workers. Frozen image sources are cached in memory/on disk; concurrent icon requests share a task. Automatic recent destinations use a separate worker, short cache and generation checks to reject stale menu results.

Menus and settings share cached resource dictionaries and the same theme/transparency/blur values. Native backdrop companion windows sit behind transparent WPF foreground windows, with a safety timer hiding orphaned surfaces. Context menus reuse one companion and update it on position changes rather than on every frame. One compositor/dispatcher-queue service has process lifetime to avoid WinRT destruction after managed apartment teardown; individual window targets are released.

Preferences and caches are created under `%LOCALAPPDATA%/NewStart`. They are not bundled in source or build artifacts. Defaults are independent from the developer's live settings. Pin removal changes only New Start's local state; imported Windows pins remain unchanged.
