# Package plan: WebDAV.Server.Azure

Status: proposed later Azure storage-provider NuGet package.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [server engine](WebDAV.Server.md), [detailed server plan](../SERVER-SUPPORT-PLAN.md).

## Purpose and dependencies

Integrate Azure storage SDKs and credentials without adding cloud dependencies to core client/server packages. Evaluate ordinary Blob Storage separately from Data Lake hierarchical namespace semantics.

S-07 depends on stable provider contracts and S-05/S-06 as applicable. Search/versioning are separately negotiated features, not automatic consequences of Azure hosting.

## Implementation

1. Specify supported services, authorization model, metadata catalog and stable identity.
2. Implement streamed staged/block uploads, conditional metadata commits and recoverable content promotion.
3. Define rename/copy cost, quota, retries, object-version mapping and garbage collection.
4. Add shared journal/lock coordination and deployment diagnostics with least-privilege credential examples.

Object prefixes are not transactional directories; SDK retries do not make namespace changes atomic. A storage ETag is not automatically a content checksum.

## Acceptance

Run provider contracts for each supported service/profile, including interrupted upload/promotion, lost responses, cross-instance locks, credential expiry, conditional conflict and orphan cleanup.

Document consistency, rename cost and unsupported cross-backend MOVE rather than claiming filesystem parity. Backups/recovery restore a coherent content/catalog generation.

## Sources

[Azure Blob/Data Lake server example](https://www.webdavsystem.com/server/server_examples/azure_blob_data_lake/) and the server plan's object-storage and cross-store consistency sections.
