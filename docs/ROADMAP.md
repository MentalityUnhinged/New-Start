# Project outline and roadmap

## Delivered preview

Native dock and custom Start, original branding, top/left/bottom placement with edge-appropriate alignment, anchored menus, AppBar reservation, configurable sizing/animation, read-only pin import, local drag pinning/reordering/removal, themed settings/context menus, account/folder shortcuts and Explorer fallback.

## Next: task model and accessibility

Improve AppUserModelID/hosted-app grouping, keyboard navigation, screen-reader labels, focus handling and cross-app drag acceptance. Add event-driven window updates and dock overflow handling. Measure cold/warm startup separately and profile native blur on multiple GPUs.

## Display and session robustness

Multi-monitor selection, mixed-DPI transitions, display reconnection, sleep/resume, remote-session and fullscreen tests. Improve auto-hide/edge reveal without losing Explorer access.

## Shell integration research

Assess supported alternatives for tray hosting, badges, task progress and previews. Do not promise parity where public Windows APIs cannot supply it. Keep adapters read-only and isolate Windows-version compatibility changes.

## Possible future visible replacement

Taskbar suppression remains disabled. A separately validated recovery process, original-state journal, process identity/heartbeat checks and crash/hang/session/Explorer-restart restoration would be required before considering an opt-in visible replacement.
