# Package plan: WebDAV.Server.FileSystem

Status: proposed first writable storage-provider NuGet package.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [server engine](WebDAV.Server.md), [detailed server plan](../SERVER-SUPPORT-PLAN.md).

## Purpose and dependencies

Expose a confined content root with durable metadata for dead properties, stable identity, locks, journal and upload state. Depends on S-01; initial delivery is S-02/S-03, with authoritative synchronization gated by S-05.

Do not require NTFS alternate streams or filesystem extended attributes for correctness. Failure to open metadata storage must not silently fall back to temporary or process-local state.

## Implementation

1. Specify root confinement, platform name/case rules and explicit symlink/reparse policy.
2. Implement content staging with durable intents, atomic replacement where supported and recoverable catalog/content coordination.
3. Persist properties/identity and integrate shared lock/quota/journal contracts.
4. Define external-write policy, watcher overflow handling and periodic reconciliation.

Filesystem operations and database commits are not one transaction. Recovery must make their visibility coherent and reclaim abandoned staging safely.

## Acceptance

Run the common provider suite plus traversal, encoded separator, UNC/device and symlink-race escape cases. Test Linux case-sensitive names independently of Windows restrictions.

Inject disk full, interrupted replace, metadata outage and restart at each commit boundary. External changes must be reconciled or invalidate sync state; an unreliable watcher alone cannot justify RFC 6578 guarantees.

## Sources

[Filesystem server sample](https://www.webdavsystem.com/server/server_examples/cross_platform_asp_net_core_file_system/) and the server plan's filesystem/transaction sections.
