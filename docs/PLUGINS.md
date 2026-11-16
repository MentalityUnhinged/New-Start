# New Start plugins (API 1, beta)

New Start provides a versioned plugin host with per-plugin permission grants, configuration, AppBar/Dock placement, live compact status, expanded content, capability actions, diagnostics, and cancellation on disable or shutdown. Manage these through **Settings → Plugins** or the AppBar's **Plugins** context action. The management window is independent of the settings panel.

## Trust model

API 1 deliberately supports **data-only external manifests** and **audited, compiled providers**. It does not dynamically load DLLs, scripts, shell commands, arbitrary executables, HTML, or remote code from a plugin folder. A manifest cannot opt into a bundled provider or register executable code. This is a restricted extension system, not an operating-system sandbox for arbitrary native plugins.

An external manifest can contribute declared HTTPS or Windows Settings actions. HTTPS targets must have a host and cannot contain embedded credentials; `file:`, `javascript:`, executable paths, and other protocols are rejected. Actions require the matching declared permission and a user click. Discovery never opens links. Unknown permissions, duplicate action IDs, duplicate plugin IDs, incompatible API versions, oversized manifests, and linked plugin directories/manifests are rejected or diagnosed.

All plugins are disabled initially. Enabling a plugin with ungranted permissions displays an explicit consent prompt. Disabling cancels outstanding work, unloads the provider, and removes its capabilities and both component placements. Removing an external plugin moves its manifest to a reversible `.removed` backup; no application or user directory is recursively deleted. Removing a bundled provider clears its saved registration, permissions, and configuration and stops all activity. Its compiled implementation remains part of New Start and can be enabled again.

Authentication credentials do not belong in manifests or plugin configuration. The bundled Codex provider delegates authentication to the official CLI; it never reads or copies authentication files. A future provider needing credentials must use an appropriate Windows credential store rather than the general JSON state file.

## Install a data-only plugin

Create `%LOCALAPPDATA%\NewStart\Plugins\example-links\plugin.json`, then select **Find installed plugins** in the Plugins window:

```json
{
  "id": "example.links",
  "name": "Useful links",
  "version": "1.0.0",
  "apiVersion": 1,
  "provider": "links",
  "description": "A compact set of explicitly chosen links.",
  "permissions": ["open-web-links", "open-system-settings"],
  "actions": [
    { "id": "project", "label": "Project website", "uri": "https://example.com/" },
    { "id": "display", "label": "Display settings", "uri": "ms-settings:display" }
  ]
}
```

Manifest limits: API exactly `1`; valid version; identifiers use ASCII letters, digits, `.`, `-`, or `_`; plugin ID maximum 100 characters; name maximum 100 characters; up to 24 actions; manifest maximum 64 KB; up to 64 discovered directories. Permissions for `links` are `open-web-links` and `open-system-settings`. Each permission must cover an actual action target. Plugins with incompatible metadata remain unavailable rather than executing partial functionality.

State lives separately in `%LOCALAPPDATA%\NewStart\plugins-state.json`, with atomic replacement on save. It includes enabled/removed state, granted permissions, independent placements, refresh interval, and an optional explicitly selected Codex executable. It contains no usage responses, passwords, API keys, or copied Codex tokens. Existing New Start preferences are unaffected.

## Host and provider interfaces

`PluginManager(Dispatcher, optionalStorageFolder)` owns discovery and lifecycle. Its public-to-the-host contract is internal to the New Start assembly:

| Interface | Purpose |
| --- | --- |
| `Entries`, `Diagnostics`, `Changed` | Inventory, sanitised diagnostics, live component updates |
| `Discover()` | Re-scan manifests and unload superseded registrations |
| `SetEnabled(id, enabled, grantPermissions)` | Apply explicit consent and load/unload |
| `Configure(id, update)` | Persist validated plugin settings |
| `ComponentsFor("AppBar" or "Dock")` | Independently placed compact components |
| `BuildCompactButton(id, open)` | Accessible live status button, using the host expansion callback |
| `BuildPanel(id)` | Live expanded content for the existing adaptive surface |
| `Capabilities`, `Execute(id)` | Registered, permission-gated declarative actions |
| `OpenConfiguration(owner, preferences)` | Dedicated themed plugin management window |
| `RefreshAsync(id, force)` | Non-blocking bounded refresh; manual refresh is rate limited |
| `Remove(id)`, `Dispose()` | Reversible removal and complete cancellation/cleanup |

UI elements subscribe while loaded and unsubscribe when unloaded. The host continues to own expansion geometry, keyboard dismissal, focus, screen placement, and visual transitions; plugins do not introduce a parallel flyout system.

`IPluginProvider` exposes `ReadAsync(configuration, cancellation)` and `Dispose()`. It returns a normalised `PluginSnapshot` with compact text, status, optional timestamp, labelled rows, and optional progress values. The host updates UI only after a successful, current-generation result. Cancellation or superseded results cannot republish a disabled plugin's component. Provider factory, refresh, and disposal failures are contained. A 25-second host deadline prevents a slow asynchronous provider from occupying the interface. Providers must honour cancellation; this deadline is not a sandbox that forcibly stops arbitrary managed code.

`PluginProviderRegistry.RegisterTrusted(...)` is an explicit, compiled host extension point. Additional reviewed provider modules can register factories without changing AppBar, Dock, or manager rendering. `AllowExternalManifests` is false for the Codex provider. Registering a trusted native provider is a host development decision; no JSON manifest can invoke this interface. New Start currently ships `links` and `codex-usage` providers.

## Codex usage provider

The bundled **Codex usage** provider is disabled by default. It requests permission to read reported Codex account limits and to launch/stop the installed official Codex CLI. It locates native `codex.exe` on PATH or in a normal official npm installation; users can select the executable explicitly. No shell launcher is invoked and no CLI is downloaded automatically.

The provider starts `codex -s read-only -a on-request app-server`, performs the documented JSONL `initialize` / `initialized` handshake, and requests `account/rateLimits/read`. It prefers `rateLimitsByLimitId` over the legacy single-bucket field to avoid counting the same limits twice. Where the installed server supports `account/usage/read`, it also displays reported token activity. That optional request has a short deadline; missing support does not discard successfully obtained limits.

Supported reported data includes allowance windows, percentage used/remaining, window duration, reset times, bucket/model distinctions, plan label, available reset credits, and optional activity/credit summaries. Missing fields remain absent or explicitly unavailable. It does not convert token activity into a fabricated subscription allowance. A progress visual clamps at its bounds while the text preserves any reported overage.

**Codex allowance data is distinct from general ChatGPT chat/message limits and conventional OpenAI API billing.** There is no claim that this provider can read every ChatGPT subscription entitlement. API-key authentication or a server that does not expose account limits yields an honest unavailable state. Sign-in and sign-out remain in the official Codex CLI. New Start does not invoke login/logout, create conversations, submit prompts, start agent turns, approve interactive requests, scrape a browser, call private usage endpoints directly, or collect account names/email addresses.

The CLI uses its existing authorised sign-in and configuration. The app-server may perform its normal startup synchronisation; the consent prompt explains that the local CLI is launched. Refresh defaults to five minutes, configurable from one to sixty minutes. Manual refresh has a ten-second minimum interval. A single lookup is capped at eighteen seconds; optional activity lookup at 2.2 seconds. Closing/disabling unloads and cancels the child process. Standard error is drained and discarded, and raw responses/authentication material are never written to diagnostics. Stale successful values remain visibly marked after a failed refresh.

### Investigation and sources

The implementation follows the official [Codex app-server protocol](https://learn.chatgpt.com/docs/app-server) and its [account response types](https://github.com/openai/codex/blob/main/codex-rs/app-server-protocol/src/protocol/v2/account.rs). These supported account methods are preferable to inventing endpoints or reading credential files.

[Omarchy's current Codex usage collector](https://github.com/omacom/omarchy/blob/master/bin/omarchy-agent-usage-codex) also obtains allowance data through the authenticated local app-server and combines it with local activity information. New Start uses its own original C# client and does not copy the collector's code. In particular, it avoids an unnecessary account-identity request: [a reported Omarchy account-read stall](https://github.com/omacom/omarchy/issues/15351) illustrates why quota retrieval should remain independent of account profile loading. No credentials or private local paths from that investigation are included in this repository.

## Validation and current limits

`PluginValidation.Run(checks)` covers API compatibility, provider restrictions, permitted URI schemes, permission consent, capability IDs, independent placement, persisted state, unload/removal, legacy and multi-bucket Codex parsing, overages, optional metrics, and invalid/missing service data. Tests use synthetic payloads and isolated temporary settings; they do not sign into Codex or upload data.

Actual allowance availability depends on the installed CLI version, existing authorised account, service availability, and subscription. Live validation must be reported separately from synthetic parser tests. This API does not host arbitrary third-party binary code, supply unrestricted network access to data-only manifests, or promise advanced ChatGPT desktop/API usage data that is not reported by the service. External executable providers would require a separately designed isolated process protocol and stronger operating-system permissions before they could safely be supported.

On 16 November 2026, the explicit read-only `--verify-codex-usage` probe successfully retrieved two real allowance windows, reset times, durations and optional activity from an existing authorised CLI installation. Its local report stores availability/count flags only. It did not enable a plugin, save permission grants, copy credentials or start a conversation. The normal default remains disabled until user consent. A restricted development tool session could not access the CLI profile; an approved local run succeeded. This is not a runtime administrator requirement or a guarantee for other accounts/CLI versions.
