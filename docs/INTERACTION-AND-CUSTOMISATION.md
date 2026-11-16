# Interaction and customisation (beta)

New features extend the existing WPF Dock, AppBar material/expansion engine and preferences. They do not replace the shell framework. Existing settings migrate without resetting pin lists, appearance or placement.

## Everyday controls

- **Dock / AppBar:** independently show either Start button. Ctrl+Alt+S and the bars’ Open Start context action remain available when both buttons are hidden. The Windows key retains its official Windows behaviour.
- **AppBar:** enable contextual application menus. Only actual accessible MenuBar categories and verified enabled commands appear. Some applications expose no menus; the active application’s existing capability panel remains available. See [application menus](APPLICATION-MENUS.md).
- **Task tray:** each location independently exposes a labelled launcher for Explorer’s real notification area. It does not pretend to rehost third-party icons. Their clicks, status, tooltips and overflow remain under Explorer. See [tray boundaries](TRAY.md).
- **Extensions:** Plugins and Custom Icons buttons open dedicated management windows. AppBar right-click menus also provide direct access.

## Dock interaction

A running app’s active window minimises on a click. A minimised window restores and activates; an unfocused window comes forward. Live window enumeration precedes activation, so a stale running indicator does not alone trigger a new launch. Multiple windows share the application pin; the exact foreground window has priority, followed by a visible window, then a minimised one. Context menus retain explicit per-window actions. Windows may deny foreground activation across security boundaries; New Start reports that restriction rather than bypassing it.

Dragging uses a pointer-following icon, a shared insertion calculation for feedback and final placement, restrained neighbour shifts, cancellation and a visible removal state beyond the configured distance. Releasing outside removes only the local pin. It never closes, deletes or uninstalls the app. Returning inside cancels removal. Imported Windows pins are hidden locally rather than edited in Windows. Failed multi-file imports restore the previous in-memory order and clean up only copies created by that operation.

## Scrolling and density

The old Start paging handler used a fixed time throttle per wheel event; ordinary WPF wheel handling also discarded precision delta magnitude and could propagate through nested viewers. A single shared preview policy now handles each packet at its nearest viewer, normalises by the Windows wheel detent, honours the system line/page/disabled preference and accumulates precision fractions. Pinned Start still moves by complete sections with no scrollbar. The shared policy applies to context menus, dropdowns, settings and plugin content. Touch panning remains native WPF panning. No timer delay is added to wheel input.

## Advanced settings

Enable Advanced, then choose **Dock**, **AppBar** or **Menus**, followed by a detail page. Breadcrumbs provide a route back. These controls edit the same persisted preferences as Standard mode; hiding Advanced leaves their values effective.

Working options include icon spacing, drag enable/removal distance, hover/bounce timing, AppBar height/padding/expansion width/corners, animation timing, menu density and scroll speed. Values are range checked and presented as whole numbers. Right-click a detail control to restore its default, or reset its section. Reduced-motion preferences take precedence over animation settings. Unsupported speculative settings are not displayed.

## Custom icons

Select an installed app or supported component, choose the application-wide/Dock/AppBar scope, and import ICO, PNG, JPEG, BMP or GIF (maximum 10 MB). A bounded, frozen bitmap is cached; the managed asset is copied into local customisation storage. Overrides use resolved executable or packaged application identities, not window titles. A component override currently supports Start in both surfaces, and Task View, Show Desktop, System controls and Recycle Bin in the Dock. Transparent pixels do not reduce the button’s click target.

Location-specific overrides take precedence over application-wide overrides. Restoring a location reveals the application-wide choice, if one exists; restoring that choice returns the original system/app icon. The Start app list uses application-wide overrides. Assets and configuration remain local; the repository contains no user-selected icon files. SVG and icon packs are not supported in this version.

## Power, media and plugins

The battery panel uses actual Windows/firmware values and labels missing telemetry unavailable. Modern power-mode operations without a supported direct interface open the relevant working Windows page. Sound retains output/volume/mute and adds metadata, artwork, transport and a reported seek range for available media sessions. It remains compact without a session. See [power](POWER.md) and [media](MEDIA.md).

Plugin API 1 accepts data-only external manifests and explicitly trusted compiled providers. Unknown executable plugins cannot run through a manifest. Permissions are granted by the user, failures are contained, and disable/removal unloads contributions independently. The Codex provider delegates sign-in to the official CLI and reports genuine returned allowance windows; it does not infer general ChatGPT message limits. See [plugin authoring and limitations](PLUGINS.md).

## Validation boundaries

Build, pure regression, disposable-window interaction and native desktop checks are recorded separately in [testing](TESTING.md) and [the implementation checkpoint](../IMPLEMENTATION_PROGRESS.md). A successful build is not a claim of mixed-DPI, multi-monitor, graphics-driver, accessibility-provider or battery-firmware acceptance.
