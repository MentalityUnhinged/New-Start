# Project outline and roadmap

## Delivered preview

Native dock and custom Start, original branding, top/left/bottom placement with edge-appropriate alignment, anchored menus, AppBar reservation, configurable sizing/animation, read-only pin import, local drag pinning/reordering/removal, themed settings/context menus, account/folder shortcuts and Explorer fallback.

## Delivered Beta: Adaptive AppBar

The initial Adaptive AppBar is implemented with resting-only reservation, per-display hosts, attached expanding controls, native media/audio/WLAN/Bluetooth integration, clock/calendar, active-window controls and optional conservative caption management with recovery. See [implementation, supported operations and pending acceptance coverage](ADAPTIVE-APPBAR.md). It shares the dock's colour theme and has separate blur/transparency settings.

## Further application-bar integration

The following earlier design notes include delivered foundation behaviour and remaining application-menu work. Universal application menu extraction and general Bluetooth connection adapters are not delivered by the current beta.

- Add an optional top application/menu bar using New Start's Light/Dark/System, transparency, blur and menu styling. It is off by default.
- When enabled, move the Start circle from the dock to the top bar, with only one Start entry point. Start opens beneath that control; preserve screen centring when the control/layout is centred.
- Restrict the dock to Bottom or Left while the bar is enabled. Retain left/centre/right alignment for Bottom and top/centre/bottom alignment for Left. If enabling from a Top dock, use Bottom/Centre. Remember the previous layout and restore it when the application bar is disabled.
- Include a clock and system access such as sound/volume, Wi-Fi/network and notification centre, with appropriate status and settings controls. The initial implementation should open Windows' existing panels where supported; custom panels and cross-app notification access need separate API/permission research.
- Reserve only the top bar's own occupied strip, alongside the dock's separate reservation. Maximised windows must respect both, with consistent clearance. Menus remain overlays. Keep fullscreen behaviour and Explorer fallback.
- Show the active application and, where reliably exposed, its application menu commands. Keep application title bars intact. Do not strip or reparent other apps' title bars or assume every app supplies exportable menus.
- Research standard Win32 menus, accessibility-exposed MenuBar/Menu/MenuItem elements and explicit app integrations. Exposing a menu through accessibility does not constitute a universal global-menu protocol: verify discovery, invocation, dynamic items, keyboard access and focus per application/framework. Unsupported apps should retain their normal menus and receive a clear fallback rather than invented commands.

Deliver in stages: bar/Start relocation and layout restrictions; clock and Windows system-panel access; then tested application-menu adapters. Validate enable/disable restoration, both dock edges, maximised/fullscreen windows, menu anchoring, focus/accessibility, DPI and multi-monitor behaviour before widening the beta.

Research references: [Microsoft MenuBar control type](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supportmenubarcontroltype), [Menu control type](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-supportmenucontroltype), and [notification area APIs](https://learn.microsoft.com/en-us/windows/win32/shell/notification-area). Accessibility menu support and notification-area APIs are separate capabilities; neither alone supplies a full macOS-style global menu or Windows notification-centre replacement.

## Planned: dock and application-bar plugins

An optional extension system will let users add their own dock items, application-bar indicators and menus. This is a future feature, not part of the current beta.

- Support both dock and application-bar placements through one plugin API. Users choose where each plugin appears, its order, and whether it is enabled. A plugin must declare which surfaces it supports; dock-only use must not require enabling the application bar.
- Initial examples include AI-usage menus showing provider-reported usage, remaining allowance and reset times, plus user-created status indicators, shortcuts and tools. Label unavailable, estimated or stale values clearly; do not imply that every AI provider exposes usage/quota data.
- Supply a documented SDK, versioned manifest/API and a small working example. Manifests identify the plugin, author, version, compatible API version, supported placements and requested capabilities.
- Let plugins provide icons, compact status text, menu entries and actions. New Start owns menu rendering so extensions inherit theme, blur, transparency, icon sizing, keyboard access and edge-aware positioning.
- Provide a Plugins settings page for explicit installation, enable/disable, placement, ordering, configuration and removal. No third-party plugins are enabled or downloaded by default.
- Design a separate plugin host process with bounded refresh rates, cancellation/timeouts and failure reporting, so a slow or crashed extension cannot freeze the dock or application bar. Process separation is fault isolation, not by itself a security sandbox; assess actual capability enforcement before publishing an SDK.
- Make requested network, application-data and credential access visible during installation. Keep provider credentials in protected local storage and out of manifests, logs, exports and source control. Do not share account data with plugins automatically.

Deliver in stages: define the extension contract and hosting boundaries; build a local example; add plugin management and both placements; then add an AI-usage example using a supported provider interface. Document compatibility, authoring, packaging and failure behaviour before community distribution. The plugin API must coexist with the application's non-commercial licence; document third-party plugin licensing separately rather than assuming every extension has the same terms.

## Planned: HoverOver window previews

Add an optional window-preview pop-out when the pointer rests over a running application's dock icon. This is a future feature, not included in the current beta.

- Show a window thumbnail and title for a single window, or a group of previews when an app has multiple windows. Pinned apps with no running windows keep their normal hover behaviour without an empty preview.
- Clicking a preview activates/restores that specific window. Provide an accessible close action that requests a normal window close and respects the application's save prompts.
- Position the pop-out above a bottom dock, below a top dock or to the right of a left dock, clamped to the available display area. Match New Start's theme, blur, transparency and rounded menu styling.
- Include an enable/disable setting and adjustable hover delay. Keep the preview open while moving from the icon into the pop-out, dismiss it after leaving or pressing Escape, and avoid stealing focus merely on hover. Coordinate with magnification, dragging and context menus.
- Research native live-thumbnail support and its hosting requirements before selecting an implementation. Handle minimised, closed, protected and otherwise unavailable windows with an icon/title fallback; do not promise a live image in every case or persist window captures.
- Validate window grouping, rapid window changes, activation, close/save prompts, keyboard access, all dock edges, display scaling and graphics/session performance. Only update previews while needed.

## Planned: custom icons, including Start

Allow users to replace icons in New Start, including the Start circle. This is a future feature, not included in the current beta.

- Provide icon overrides for individual pinned applications and built-in dock/application-bar controls, including Start. Overrides change New Start's presentation only, not Windows shortcuts or the application's own files.
- Use the same custom Start icon wherever Start is currently placed: the dock or the optional application bar. Keep its full click target, accessible name, sizing and hover behaviour independent of transparent pixels in the image.
- Add an icon picker with preview, per-item reset and restore-all-defaults actions. Keep the hollow circle and current system/application icons as defaults.
- Research supported image formats, including Windows ICO, transparent PNG and vector SVG, with consistent scaling and aspect-ratio handling. Validate imported files, limit dimensions/file size and handle missing or invalid assets gracefully; no format is promised until its loading path is implemented and tested.
- Copy chosen assets into New Start's local customization storage so moving the source file does not break an override. Associate application overrides with stable identities so reordering or moving the dock does not lose them.
- Keep personal icon choices/assets out of source control. Any future theme/icon-pack import or export should be explicit, portable and documented, with licensing/attribution retained where required.

## Task model and accessibility

Improve AppUserModelID/hosted-app grouping, keyboard navigation, screen-reader labels, focus handling and cross-app drag acceptance. Add event-driven window updates and dock overflow handling. Measure cold/warm startup separately and profile native blur on multiple GPUs.

## Display and session robustness

Multi-monitor selection, mixed-DPI transitions, display reconnection, sleep/resume, remote-session and fullscreen tests. Improve auto-hide/edge reveal without losing Explorer access.

## Shell integration research

Assess supported alternatives for tray hosting, badges, task progress and previews. Do not promise parity where public Windows APIs cannot supply it. Keep adapters read-only and isolate Windows-version compatibility changes.

## Planned: optional Windows taskbar and Start suppression

Add separate, explicit settings to **hide the Windows taskbar while New Start runs** and **use New Start instead of Windows Start**. Both remain off by default and are not implemented in the current beta. They should work with the dock alone or with the optional application bar.

- Research reversible taskbar suppression across primary/secondary displays and Explorer restarts. Hiding a window and releasing its reserved work area are separate requirements; verify both rather than assuming one implies the other.
- Investigate redirecting standalone Windows-key and Ctrl+Esc activation to New Start, while preserving unrelated Windows-key shortcuts. Suppress normal Windows Start entry points only where reliably supported; do not promise a universal OS-level disable switch.
- Keep Explorer, desktop/file management and Windows Start components installed and running. Do not terminate StartMenuExperienceHost, uninstall shell components or change the system shell as an ordinary preference.
- Restore the original taskbar visibility, work-area behaviour and input handling when the option is turned off, New Start exits, or its recovery process detects a crash/hang. Preserve preference changes the user makes during the session instead of blindly overwriting them with an old snapshot.
- Require a separate tested recovery executable, durable original-state journal, PID/start-time validation and heartbeat before suppression can be enabled. If recovery cannot start or remain healthy, leave Windows UI available. Provide an independent emergency restore command and prevent repeated crash/re-enable loops.
- Validate startup, exit, crash, hang, recovery failure, Explorer restart, lock/unlock, display/DPI changes and sleep/resume. Keep secure-system shortcuts and a way to launch Task Manager available. No keyboard-content logging is needed for Start-key redirection.

Research boundaries: [low-level keyboard hooks](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc) can be removed by Windows after a timeout, so interception needs failure/fallback testing. [Shell Launcher](https://learn.microsoft.com/en-us/windows/configuration/shell-launcher/) is a separate managed-device shell-replacement feature with edition/deployment requirements, not a drop-in preference for this desktop beta. Isolate any Explorer compatibility adapter and document tested Windows versions.

Application context and independent colour sources now have a beta implementation. See [supported commands, wallpaper matching and remaining acceptance coverage](CONTEXT-AND-COLOUR.md). Broad third-party menu export, richer extension-backed browser integrations and user-defined action layouts remain future work.
