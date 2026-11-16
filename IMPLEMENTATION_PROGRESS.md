# New Start implementation progress

## Implementation checkpoint — 16 November 2026

Baseline: `966157d`. Working checkout: `NewStart-Development` in the current workspace. The original checkout remains in the earlier Codex task outputs; do not overwrite its settings or caches. The screenshot task was interrupted by this implementation request; no new gallery was published.

## Architecture inspected

.NET 8 Windows/WPF, native Composition DLL; DockWindow owns preferences, Start and AdaptiveAppBars. AppBar uses connected AppBarMaterial geometry and separate resting Shell reservation. ApplicationCapabilities uses bounded isolated UIA workers. Media/audio/network/Bluetooth services already exist. ThemedMenus and Appearance supply shared styling. TaskbarSuppression has an independent recovery helper.

## Workstreams

- Implemented and integrated: shared wheel-delta policy, precision accumulation, Windows scrolling preferences, compact menu density, independent Start visibility, Ctrl+Alt+S/context-menu fallback, advanced drill-down settings.
- Implemented and integrated: application/UIA menu categories and lazy verified command execution, AppBar context menus, custom icon scopes/caching/import/restore and dedicated window.
- Implemented and integrated: restricted API-v1 plugin framework, permissions/lifecycle/configuration/diagnostics, both placements, dedicated manager, documented Codex app-server usage provider (disabled until consent).
- Implemented and integrated: genuine battery/power telemetry/panel, native notification-area access in either location, Sound media metadata/transport/timeline/seeking.
- Implemented: Dock minimise/restore/focus, stable packaged identity matching, drag ghost/insertion/removal feedback and landing animation, connected expansion/layout refinements. Live activation and rearrangement passed; drag-away boundary feedback/removal has geometry/model coverage and still needs physical acceptance outside the automation tool's window bounds.
- Validation checkpoint: Release builds passed with zero warnings/errors; the combined development suite reached 131/131 passing checks (scrolling, migration/advanced UI, plugin permissions/lifecycle/parsing, power, media, custom icons including aspect ratio, drag geometry, pin order, spacing on all three edges and fresh-install Start independence). Live Dock activation passed 14/14; real application menu commands passed 9/9. Read-only power harness passed five native assertions. These do not establish full visual/hardware acceptance.
- Native AppBar: 134/134 checks passed in the normal Windows/Explorer desktop. Coverage includes independent Start/tray combinations and hidden-Start fallback, 28/44/56-DIP reservation, every connected expansion, Power/disabled-plugin content, context menus/tint/alpha, scrolling, material continuity, interruption and release/restoration. Four reservation checks failed only in the restricted tool session; the normal desktop run verified them. Reports omit personal media/network/application titles.
- Focused surfaces: 42/42 checks passed after visual review corrected ComboBox display templates and dark Expander text. Explicit backdrop hiding now survives resize/activation; zero blur and opaque/clear endpoints do not reappear as orphan surfaces. Nested viewer packets route once and precision deltas conserve displacement. Release build: zero warnings/errors.
- Visual acceptance: connected Settings and Extensions, dedicated Custom Icons and actual Dock rearrangement inspected at 100% scaling. No new screenshots were added to the repository. All disposable preview windows closed cleanly and temporary preferences were removed.

## Constraints and next step

Explorer provides no public supported enumeration/hosting API for arbitrary third-party tray icons. Implement an honest native tray access alternative, document limits, never manufacture icons. Unknown hardware telemetry must remain unavailable. No undocumented OpenAI endpoints, credential extraction or arbitrary untrusted plugin execution.

Desktop fixtures need normal Windows foreground permission via a Run button. User has agreed to Windows app access. Native executable fixtures now passed all 14 Dock activation and all 9 menu checks, including real Save and nested commands. Isolated workers receive explicit per-worker foreground delegation and observe asynchronous foreground acknowledgment (bounded200ms, cancel if another external window intervenes). All disposable fixture windows were closed. Source/report paths are the normal `artifacts` directory; use the native executable for previously approved desktop access.

Live Codex lookup succeeded using the existing authorised CLI: two reported allowance windows, reset times, durations and optional activity were returned. The local test report retains only availability/count flags, not usage values or account identity. No login, chat, credential copying or persisted plugin enable/grant occurred. The restricted tool environment initially blocked normal CLI profile access; the approved local read-only probe succeeded. Runtime does not require administrator elevation.

Source implementation and documented supported alternatives are complete for this pass and published to public `main`. Hosted Windows builds for the main implementation and spacing correction passed native/managed compilation, regression checks and artifact upload. Extra spacing now changes only the dock's length, preserving its reserved clearance. Missing/invalid preference files also receive the normal migration/default validation, preserving independent Start choices on a fresh install; all131 local checks pass. Commits use the existing GitHub no-reply identity. Runtime preferences, imported icons, usage responses, local reports and private desktop captures remain excluded.

The existing live installation was updated through its original launch path, with runtime-only backups retained locally and personal settings preserved. Only the verified app process was stopped; its separate Explorer recovery helper was left to finish. New Start remained running after restart and the runtime DLL matched the validated build. For the final small preference-default follow-up, verify the published head's CI and deploy the matching runtime before concluding the session.

Remaining acceptance: physical drag-away/pop feedback and Escape cancellation, mixed-DPI/multiple displays/hot-plug, different battery firmware/desktop hardware, active-media transport/seek across providers, native tray overflow/focus, Bluetooth/network connection actions and longer idle/driver testing. No functional claim is made for unavailable application menus, inline third-party tray hosting, arbitrary executable plugins, unreported battery telemetry or general ChatGPT message limits.

For a fresh session: inspect git status, read this checkpoint and docs/TESTING.md, then use `--development-preview` with its disposable profile for the remaining visual/hardware matrix. Existing normal preferences must remain intact. No automatic continuation has been scheduled or guaranteed.
