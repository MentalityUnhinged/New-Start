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

## Task model and accessibility

Improve AppUserModelID/hosted-app grouping, keyboard navigation, screen-reader labels, focus handling and cross-app drag acceptance. Add event-driven window updates and dock overflow handling. Measure cold/warm startup separately and profile native blur on multiple GPUs.

## Display and session robustness

Multi-monitor selection, mixed-DPI transitions, display reconnection, sleep/resume, remote-session and fullscreen tests. Improve auto-hide/edge reveal without losing Explorer access.

## Shell integration research

Assess supported alternatives for tray hosting, badges, task progress and previews. Do not promise parity where public Windows APIs cannot supply it. Keep adapters read-only and isolate Windows-version compatibility changes.

## Possible future visible replacement

Taskbar suppression remains disabled. A separately validated recovery process, original-state journal, process identity/heartbeat checks and crash/hang/session/Explorer-restart restoration would be required before considering an opt-in visible replacement.

