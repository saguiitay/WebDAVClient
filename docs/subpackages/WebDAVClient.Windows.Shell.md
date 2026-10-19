# Package plan: WebDAVClient.Windows.Shell

Status: proposed native/COM artifacts and optional managed SDK. Owns shell work in D-07.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [Windows provider](WebDAVClient.FileSystem.Windows.md), [desktop agent](Desktop-Agent.md), [bridge](WebDAVClient.DesktopBridge.md).

## Purpose and boundaries

Add commands, properties/state and later thumbnails/previews for owned roots. A shell handler is not a mount, content transport, authentication system or server lock implementation.

Prefer packaged commands and out-of-process components. If in-process native COM is necessary, use a thin shim forwarding bounded authenticated per-user IPC. Keep network work, synchronization, secrets and interactive sign-in outside Explorer.

## Dependencies and delivery

Depends on the D-02 deployment spike and a working D-03 provider/agent. Commands depend on their actual client capabilities.

1. Add Reveal on Server, Copy Link, Lock/Unlock, Show Sync Errors and Resolve Conflict.
2. Use platform Keep Offline/Free Space and provider state rather than extra legacy icon overlays.
3. Scope actions to enrolled roots and valid item states; negotiate IPC versions.
4. Evaluate versions/property columns and bounded thumbnails after the agent is stable.

Do not execute or render arbitrary remote documents inside Explorer, recursively hydrate a library for thumbnails, or block shell UI on remote requests. Use a trusted rendering service or isolated component where needed.

An Explorer namespace extension remains deferred unless non-filesystem navigation is explicitly required; it does not provide normal paths to arbitrary applications.

## Acceptance

- Slow/offline/crashed agents do not hang Explorer; invalid paths and unauthorized IPC fail clearly.
- Multiple users/accounts cannot access each other's commands or state.
- Native bitness, packaged registration and signed update/uninstall work on supported architectures.
- Old/new components reject unsupported IPC operations rather than misinterpreting commands.
- Uninstall follows the agent's unsynced-data preservation policy.

## Sources

[Microsoft shell handlers](https://learn.microsoft.com/en-us/windows/win32/shell/handlers) and the site's [modern drive architecture](https://www.webdavsystem.com/ajax/next_version/).
