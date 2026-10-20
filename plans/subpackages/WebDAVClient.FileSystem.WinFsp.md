# Package plan: WebDAVClient.FileSystem.WinFsp

Status: proposed optional Windows SDK with native runtime/driver dependency. Windows portion of D-05.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [Windows feasibility](WebDAVClient.FileSystem.Windows.md), [sync/cache policy](WebDAVClient.Sync.md), [desktop agent](Desktop-Agent.md).

## Purpose and backend decision

Expose a real drive letter or mount point through user-mode filesystem callbacks. This is distinct from a Cloud Files sync root and does not imply complete NTFS semantics.

Use D-02 to select at most one initial backend. WinFsp is a candidate; Dokan is an alternative, not a second mandatory implementation. Built-in WebClient is deprecated and optional; ProjFS does not supply cloud-sync UX; a custom kernel driver is not recommended.

WinFsp requires an installed signed driver and review of its GPLv3/FLOSS exception or commercial license. Dokan has component-specific LGPL/MIT licensing and driver/network requirements. Neither is automatically covered by this repository's MIT license; review pinned binaries, bindings and redistribution rights.

## Dependencies and implementation

Depends on D-02, P-03/P-06 and an explicit writable cache/operation policy.

1. Define random read/write, truncate, flush/fsync, close, rename/replace, share modes, delete-on-close, enumeration, case and timestamp behavior.
2. Implement bounded reads and staged writes; avoid issuing one PUT for every small local write.
3. For cached mode, persist writeback and invalidate namespace metadata after mutation. For online-only mode, fail clearly on disconnect.
4. Detect/install compatible prerequisites through a separate signed installer and support safe unmount/recovery.

Define whether successful flush means server-committed or durably queued locally. Never imply server durability for a volatile buffer. Reject unsupported hard links, alternate streams, ACLs, sparse/memory-mapped operations explicitly unless implemented safely.

## Acceptance

Run filesystem contract tests and representative application saves, including large/sparse logical data, rename races, disconnect/reconnect, full cache, crash during flush, multiple mounts, missing driver and unmount with pending writes.

Ship a supported-operation table, measured durability behavior and approved distribution/license decision. Do not advertise feature parity with Cloud Files or NTFS merely because a drive letter appears.

## Sources

[WinFsp](https://github.com/winfsp/winfsp), [distribution](https://winfsp.dev/rel/), [Dokan](https://github.com/dokan-dev/dokany), [legacy mounting API](https://www.webdavsystem.com/mapdrive/programming/) and [WebClient deprecation](https://learn.microsoft.com/en-us/windows/whats-new/deprecated-features).
