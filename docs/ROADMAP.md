# Project outline and roadmap

## Delivered preview

Native dock and custom Start, original branding, top/left/bottom placement with edge-appropriate alignment, anchored menus, AppBar reservation, configurable sizing/animation, read-only pin import, local drag pinning/reordering/removal, themed settings/context menus, account/folder shortcuts and Explorer fallback.

## Planned: optional macOS-style application bar

This is a future feature specification, not functionality included in the current beta.

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

## Task model and accessibility

Improve AppUserModelID/hosted-app grouping, keyboard navigation, screen-reader labels, focus handling and cross-app drag acceptance. Add event-driven window updates and dock overflow handling. Measure cold/warm startup separately and profile native blur on multiple GPUs.

## Display and session robustness

Multi-monitor selection, mixed-DPI transitions, display reconnection, sleep/resume, remote-session and fullscreen tests. Improve auto-hide/edge reveal without losing Explorer access.

## Shell integration research

Assess supported alternatives for tray hosting, badges, task progress and previews. Do not promise parity where public Windows APIs cannot supply it. Keep adapters read-only and isolate Windows-version compatibility changes.

## Possible future visible replacement

Taskbar suppression remains disabled. A separately validated recovery process, original-state journal, process identity/heartbeat checks and crash/hang/session/Explorer-restart restoration would be required before considering an opt-in visible replacement.
