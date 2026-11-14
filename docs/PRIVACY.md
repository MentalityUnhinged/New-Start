# Local data and publication hygiene

New Start stores preferences, local pin identities/order, imported-pin exclusions, synced Start/app lists and icon caches under `%LOCALAPPDATA%/NewStart`. These can identify installed applications and local paths. Account display name and avatar are read from the current Windows account and displayed locally.

The application contains no telemetry, update service or account login. Launching an app, opening Windows settings or opening a user-selected recent destination delegates to Windows/the owning application, which may have its own network behaviour. New Start does not upload its settings or inventory.

This repository excludes live user settings, caches, reusable icons extracted from installed apps, standalone account images, dumps, build intermediates, binaries and credentials. It includes original New Start branding and five example screenshots explicitly supplied for public publication. Those screenshots show the contributor's chosen desktop/app appearance, including account and recent-location details visible in the images; they are documentation, not bundled settings or runtime data. New installations use code defaults, including Bottom/Centre, with no developer pin list.

Diagnostic switches write reports locally. Inspect/redact any report before attaching it to an issue. Build artifacts should be produced from this repository or CI; do not commit local `bin`, `obj`, `native/build` or `artifacts` directories. Public commits use a GitHub no-reply identity.
