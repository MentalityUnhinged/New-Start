# Validation

Build on Windows with `./build.ps1`. Close an existing New Start instance before running a diagnostic; the single-instance mutex otherwise prevents a second launch.

| Switch | Coverage | Behaviour |
| --- | --- | --- |
| `--verify-audit` | Invalid preferences, concurrent icon/recent requests, shared styles | Writes a report and exits |
| `--verify-pin-model` | Synthetic pin insertion/order/hide/restore/serialization | Writes a report and exits; no Windows pin edits |
| `--verify-locations` | Nine edge/alignment combinations, menu bounds, native reservation, top-centre centring, default/fallback geometry | Temporarily moves dock, restores in-memory settings and exits |
| `--verify-start` | Prepared Start frame, icons, power bounds, task layout | Leaves preview running |
| `--verify-backdrop` | Repeated open/hide, no orphan surface, zero blur | Leaves preview running |
| `--verify-menu-performance` | Idle/menu CPU, zero blur comparison, recent duplication, handle counts over reopen cycles | Temporarily changes blur in memory, restores and exits |

Reports are local under `%LOCALAPPDATA%/NewStart`, not source artifacts. Rendering/desktop tests need an interactive Windows session. CI is a compilation gate; it does not validate native screen reservation or visual behaviour.

Placement validation passed all geometry/default/fallback checks and all nine live edge/alignment checks on the development display. Release compilation passed with no warnings/errors. Earlier audit checks covered pin persistence, shared caches/styles, native shutdown and backdrop dismissal. These do not establish full driver, display, accessibility or long-duration coverage.

Manual acceptance: each edge/alignment at several icon sizes, maximise a disposable app window, open/dismiss Start/context/settings menus, check top-centre menu centring, left-edge reorder and bounce direction, drag between Explorer/Start/dock, Escape cancellation, theme/transparency/high contrast, optional clock/controls, exit/restart and display/session changes. Do not use personal screenshots in public issues without redaction.

The placement diagnostic also measures the normal icon boundary against the native work area in all nine layouts. Expected clearance is 8 DIPs, with at most one physical pixel of rounding; click capture does not expand into this gap. Edge-change repositioning is checked after Windows has settled its work-area notification.
