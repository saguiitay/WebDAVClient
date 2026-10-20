# Package plan: WebDAVClient.Sync

Status: proposed optional NuGet package. Owns milestone D-01.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [protocol plan](../PROTOCOL-COMPLETENESS-PLAN.md), [desktop agent](Desktop-Agent.md).

## Purpose and dependencies

Provide portable durable synchronization, transfer scheduling, conflict recovery and cache policy. Depend on P-02/P-03/P-06/P-07; E-03 enables efficient standards-based incremental sync. A limited polling profile may prototype earlier.

Do not introduce a universal filesystem callback abstraction before Windows/macOS prototypes establish actual shared requirements. Native providers retain their platform-specific ownership models.

## Persistent state

Use a transactional local database for account/root identity, stable IDs, href mappings, validators, token checkpoints, pending operations, cache and failure state. Select the database in the implementation spike; include schema migration and recovery.

Credentials belong in OS-protected storage, not queue records. Queue entries reference account/credential handles.

Persist transitions before acknowledging durable work: remote-only, hydrated-clean, locally-modified, uploading, conflict, deleted-pending, authentication-required and unrecoverable-error.

## Incoming synchronization

Prefer RFC 6578 only when actually implemented. Persist each batch and its checkpoint transactionally; restart safely without advancing the token ahead of durable local changes.

With PROPFIND alone, reconcile complete authorized snapshots. A timeout, 403, truncated response or incomplete listing must never imply mass deletion. Without stable IDs, document path-based fallback and limited rename fidelity; equal ETags do not establish identity.

Notifications trigger reconciliation, not direct destructive changes. Reconnect checks authoritative state even after apparently successful push delivery.

## Outgoing changes and conflicts

Use validators from the last synchronized version. A 412 preserves local content and exposes conflict rather than silently overwriting.

Coalesce only safe work while retaining parent/child, rename and upload/replace dependencies. Reconcile ambiguous outcomes using durable operation IDs and server state.

Support temporary-file/rename/replace save patterns. Local random writes are not automatic remote partial PUT; stage dirty content and commit through the selected safe profile.

Offline edits do not preserve an expired remote lock. Show remote exclusivity only while a current granted lease exists, and reconcile offline edits conditionally after reconnect.

## Cache and delivery

Bound cache and per-account concurrency. Evict only clean unpinned content, never unsynced edits. Use atomic cache writes, access controls, content validation and explicit abandoned-staging cleanup.

Track content and metadata revisions separately when available; absent validators are a degraded capability, not proof of unchanged content. On macOS, respect File Provider's ownership of local copies rather than creating a competing cache.

Implement persistence/queue first, then incoming reconciliation, outgoing conflict-safe writes and failure recovery.

## Acceptance

Exercise restart after every state transition, two-device concurrent edits, expired tokens, renamed parents with pending child writes, incomplete listings, disk full, credential expiry, revoked access, lost responses and manual conflict resolution. Every case must preserve user data.

Suggested release workloads: 100,000 items, multi-gigabyte transfers, 10,000 queued offline mutations and a 24-hour soak with repeated process termination. Calibrate latency/memory budgets on named hardware.

## Sources

[RFC 6578](https://www.rfc-editor.org/rfc/rfc6578.html), [incoming synchronization](https://www.userfilesystem.com/programming/synchronization/incoming_sync/), [Sync-ID](https://www.userfilesystem.com/programming/synchronization/sync_id/) and [content/metadata ETags](https://www.userfilesystem.com/programming/etags/). Vendor identity conventions are not automatically RFC 6578 requirements.
