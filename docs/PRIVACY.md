# Local data and publication hygiene

New Start stores preferences, local pin identities/order, imported-pin exclusions, synced Start/app lists and icon caches under `%LOCALAPPDATA%/NewStart`. These can identify installed applications and local paths. Account display name and avatar are read from the current Windows account and displayed locally.

The application contains no telemetry, update service or account login. Launching an app, opening Windows settings or opening a user-selected recent destination delegates to Windows/the owning application, which may have its own network behaviour. New Start does not upload its settings or inventory.

This repository is a clean source export. It excludes live user settings, caches, icons extracted from installed apps, avatars, desktop screenshots, dumps, build intermediates, binaries and credentials. Only the original New Start branding assets are included. New installations use code defaults, including Bottom/Centre, with no developer pin list.

Diagnostic switches write reports locally. Inspect/redact any report before attaching it to an issue. Build artifacts should be produced from this repository or CI; do not commit local `bin`, `obj`, `native/build` or `artifacts` directories. Public commits use a GitHub no-reply identity.
