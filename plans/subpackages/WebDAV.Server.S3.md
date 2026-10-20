# Package plan: WebDAV.Server.S3

Status: proposed later S3 storage-provider NuGet package.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [server engine](WebDAV.Server.md), [detailed server plan](../SERVER-SUPPORT-PLAN.md).

## Purpose and dependencies

Integrate streamed object storage with a durable metadata catalog for hierarchy, identities, properties, locks and journal. Keep SDK/service dependencies outside the base server.

Delivery belongs to S-07 after provider contracts and S-05/S-06 foundations as applicable. Select exact supported S3 services; "S3-compatible" is not proof of identical behavior.

## Implementation

1. Specify object key/version mapping, credential scopes and the catalog's authoritative namespace.
2. Implement multipart staging, conditional commit, durable intent/recovery and abandoned-upload cleanup.
3. Coordinate copy/rename/delete costs and partial failure with exact DAV outcomes.
4. Publish consistency, retry, quota, backup and garbage-collection policies per supported service.

Prefixes are not atomic directories. Multipart/storage ETags must not be treated as content checksums without guarantees. Reject or explicitly coordinate unsupported cross-store MOVE.

## Acceptance

Run provider contracts plus interrupted multipart upload, failed promotion/catalog commit, lost responses, concurrent writes, stale credentials, orphan cleanup and shared locks.

Verify restores and sync-token generation invalidation. Claim only the services and semantics exercised; use authorized, isolated integration fixtures.

## Sources

The [server plan](../SERVER-SUPPORT-PLAN.md) specifies S-07 and the cross-store state machine. The [Java server feature overview](https://www.webdavsystem.com/javaserver/features/) supplies object-storage use cases, not a guarantee about any selected backend.
