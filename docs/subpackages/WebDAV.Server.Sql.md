# Package plan: WebDAV.Server.Sql

Status: proposed second storage-provider NuGet package.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [server engine](WebDAV.Server.md), [detailed server plan](../SERVER-SUPPORT-PLAN.md).

## Purpose and dependencies

Store tenant-scoped hierarchy, properties, locks, journal and transactional namespace state in SQL. Stream content from database storage or coordinate a separate content store explicitly.

Delivery is S-06 after S-03/S-04 stabilize provider/lock contracts. Select supported database engines and drivers during implementation rather than implying every SQL dialect works.

## Implementation

1. Define tenant/stable-ID keys, unique parent/name constraints and conditional row versions.
2. Implement transaction/lock ordering, property rollback, staging and journal visibility.
3. Add indexes for parent enumeration, lock overlap and change ranges; avoid full-tree scans for every operation.
4. Provide schema migration/rollback, backup/recovery and an existing CMS/DMS policy integration sample.

Database-specific full-text indexing stays in the provider. If blobs are separate, use durable coordination rather than assuming a cross-store transaction.

## Acceptance

Run the same provider contract suite as filesystem storage. Verify concurrent namespace changes, lock overlap, tenant isolation, large streamed documents, migration/restart and backup consistency.

Publish realistic enumeration/transaction measurements and explicit database support. Search/version extensions remain capability-gated.

## Sources

[SQL server sample](https://www.webdavsystem.com/server/server_examples/cross_platform_asp_net_core_sql/) supplies storage scenarios; S-06 defines the release gate.
