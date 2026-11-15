# Validation

Build on Windows with `./build.ps1`. Close an existing New Start instance before running a diagnostic; the single-instance mutex otherwise prevents a second launch.

| Switch | Coverage | Behaviour |
| --- | --- | --- |
| `--verify-audit` | Invalid preferences, concurrent icon/recent requests, shared styles | Writes a report and exits |
| `--verify-pin-model` | Synthetic pin insertion/order/hide/restore/serialization | Writes a report and exits; no Windows pin edits |
| `--verify-locations` | Nine edge/alignment combinations, menu bounds, native reservation, top-centre centring, default/fallback geometry | Temporarily moves dock, restores in-memory settings and exits |
| `--verify-dock-glass` | Default off, light/dark on three edges, tray clearance, transparency/blur combinations, disabled surface cleanup | Temporarily changes in-memory settings, restores and exits |
| `--verify-appbar` | Per-connected-display hosts, every expansion, fixed reservation, interruptible animation, appearance isolation, Start placement, disable/release and idle samples | Restores in-memory preferences and exits; native discovery may require location access |
| `--verify-borderless` | Disposable WinForms window, floating/maximised/restored styles, exclusion/disable/exit restoration, forced-process-exit guardian and journal recovery | Never modifies personal application windows; closes its disposable process and exits |
| `--appbar-preview` | Interactive AppBar acceptance | Uses temporary material values, exposes the host for UI automation and suppresses deactivation dismissal; settings changes can persist if settings is opened and closed |
| `--start-actions-preview` | Interactive Start pin/unpin acceptance | Keeps Start open despite automation activation; real local preference changes persist |
| `--verify-start` | Prepared Start frame, icons, power bounds, task layout | Leaves preview running |
| `--verify-backdrop` | Repeated open/hide, no orphan surface, zero blur | Leaves preview running |
| `--verify-menu-performance` | Idle/menu CPU, zero blur comparison, recent duplication, handle counts over reopen cycles | Temporarily changes blur in memory, restores and exits |

Reports are local under `%LOCALAPPDATA%/NewStart`, not source artifacts. Rendering/desktop tests need an interactive Windows session. CI is a compilation gate; it does not validate native screen reservation or visual behaviour.

The AppBar foundation, theme/material isolation and recovery fixtures passed on one connected 100% display. UI acceptance checked attached media artwork/metadata, real native Wi-Fi state/list, Bluetooth radio state, and a master-volume change restored from 40 to 41 to 40. Playback and network connections were not interrupted. The full remaining hardware/driver/application matrix is tracked in [Adaptive AppBar validation](ADAPTIVE-APPBAR.md); physical multi-monitor/DPI/hot-plug and device connection operations are not asserted as passed.

Start context actions were checked by pinning Microsoft Store to the dock, verifying duplicate pin prevention, unpinning it from New Start, restarting, and pinning it back through search. The test dock pin was removed afterward. Windows Start pin storage was not modified. Start actions use the shared themed context menus and local preferences; All Apps/search remain available after unpinning.

Placement validation passed all geometry/default/fallback checks and all nine live edge/alignment checks on the development display. Release compilation passed with no warnings/errors. Earlier audit checks covered pin persistence, shared caches/styles, native shutdown and backdrop dismissal. These do not establish full driver, display, accessibility or long-duration coverage.

Manual acceptance: each edge/alignment at several icon sizes, maximise a disposable app window, open/dismiss Start/context/settings menus, check top-centre menu centring, left-edge reorder and bounce direction, drag between Explorer/Start/dock, Escape cancellation, theme/transparency/high contrast, optional clock/controls, exit/restart and display/session changes. Do not use personal screenshots in public issues without redaction.

The placement diagnostic also measures the normal icon boundary against the native work area in all nine layouts. Expected clearance is 8 DIPs on both the screen-facing and maximised-window-facing sides, with at most one physical pixel of rounding; measurements exclude invisible button and animation padding. Click capture stays around the visible dock. Edge-change repositioning is checked after Windows has settled its work-area notification.

System Controls uses the shared themed menu without a heading. Repeated icon clicks were checked for open/close/reopen behaviour; the dismissal guard resets after the physical mouse button is released, even when the popup consumes MouseUp. Empty Recycle Bin uses SHEmptyRecycleBinW with zero flags, preserving Windows confirmation/progress. The menu entry and disabled-empty state were checked without executing deletion; cancellation, populated bins and drive failures require separate disposable-data acceptance tests.

Recycle Bin state updates use Shell change notifications and a one-second fallback. A disposable file was sent to the bin, the full icon and enabled Empty action were checked, then that file was restored and the empty icon/disabled action were checked. No bin-empty operation or permanent deletion was performed. Forced refreshes are coalesced without dropping an update requested during a running query, and query failures retain the last known state. Native notification registration is released when the dock closes.

AppBar expansion checks also compare the single WPF outline against the native window region across every section. The shared native helper prepares its geometry before visibility, suppresses background erasure and uses composition-only rendering. Visual acceptance checked seamless curved joins, switching between clock/audio, folding back to the strip, and the dark themed calendar with month navigation. Native backdrop dismissal passed ten reopen cycles with zero-blur cleanup.
