# Plan: WebDAV server scenarios

Status: proposed new functionality. Reviewed 2026-10-19.

The repository currently has **no inbound server implementation**. This plan creates one beside the client, without changing the meaning or dependency footprint of the `WebDAVClient` package.

Related: [protocol plan](PROTOCOL-COMPLETENESS-PLAN.md) and [package and desktop plan](SUBPACKAGE-AND-DESKTOP-PLAN.md).

## 1. Target scenarios

| Scenario | Initial deliverable | Later capability |
| --- | --- | --- |
| Publish a filesystem directory | ASP.NET Core endpoint backed by a confined content root and durable metadata. | Out-of-band change reconciliation, quota, versions and sync journal. |
| DAV-enable an existing CMS/DMS/CRM | Provider contracts over application resources and authorization. | Virtual folders, federated sources, custom live properties and indexing. |
| Database-backed documents | SQL-backed hierarchy, dead properties, locks and transactional namespace operations. | Full-text search, version retention and change feeds. |
| Object storage | Separate adapter using metadata catalog plus streamed blob content. | Multipart resume, staged commits and backend-specific optimization. |
| Desktop document editing | Correct locks, validators, content operations and metadata, tested with current native applications. | Browser launch bridge, manual lock commands, automatic editing leases. |
| Explorer/Finder synchronization | Stable identity, incremental changes, deletion tombstones and safe conditional writes. | Notifications, on-demand hydration and offline reconciliation in desktop packages. |
| Read-only publishing | Explicitly configured read-only provider and accurate capabilities. | Application-specific property/search support. |
| CalDAV/CardDAV | Not in initial server release. | Separate domain profiles and parsers after core/ACL/sync maturity. |
| Reverse proxy / gateway | Optional later provider over another DAV service. | Identity mapping, delegated auth, upstream reconciliation and caching. |

Do not choose anonymous access, unbounded filesystem access, process-local locks or permissive TLS simply because a demonstration server uses them.

## 2. Standards and claims

The initial production target is an RFC 4918 server, with classes advertised **only when the actual resource/provider meets their obligations**. A class 1/3 read-only deployment and a class 1/2/3 writable deployment are different configurations.

Build a requirement ledger for sections 4-18, including live/dead properties, collections, locks, general request handling, methods, headers, status codes, pre/postconditions and XML extensibility. Method dispatch alone is insufficient for conformance.

OPTIONS must report only supported methods and applicable DAV tokens. XML capabilities such as supportedlock and supported-report-set must agree with endpoint behavior. Do not claim search/synchronization/versioning globally when only one collection supports them.

Class 2 locking is essential to the targeted editing scenarios, but not an authorization mechanism. Shared locks are optional server policy; support exclusive locks first, accurately advertise scope, and implement shared locks only with complete conflict tests.

## 3. Architecture and package boundaries

```text
ASP.NET Core endpoint
    authentication / host policies
    request limits / cancellation / diagnostics
    URI and destination normalization
    protocol dispatch and condition evaluation
    authorization + transaction coordinator
    storage provider and durable metadata
    content storage / index / journal / notifications
```

The shared wire layer owns qualified names, XML, headers, status/precondition models and condition parsing. It must not reference ASP.NET Core, filesystem paths, SQL or the outbound client's configuration.

| Component | Responsibility | Excluded responsibility |
| --- | --- | --- |
| `WebDAV.Protocol` candidate | Shared XML, conditions and wire results once there are two real consumers. | Hosting, business authorization, storage and retries. |
| `WebDAV.Server` candidate | Host-independent engine, provider contracts, DAV semantics and operation coordination. | Browser UI and native clients. |
| `WebDAV.Server.AspNetCore` candidate | Endpoint routing, DI/options, request/response adaptation, host auth and health integration. | Backend-specific hierarchy/locking logic. |
| `WebDAV.Server.FileSystem` candidate | Confined file content and durable metadata integration. | A general-purpose remote filesystem driver. |
| Other storage adapters | SQL, Azure or S3-specific persistence. | Silently emulating unsupported guarantees. |

Initially keep provider contracts with the server engine. Extract a separate abstractions package only if independent providers need that stable dependency boundary.

API shape to validate in S-01: an `AddWebDav(...)` service registration and a `MapWebDav("/dav", ...)` endpoint, not a special server mode on `Client`. These names are proposals, not existing extension methods.

Mount the endpoint under any configured path base, including behind a reverse proxy. A GET collection listing UI is optional application functionality; RFC collection operations must work without that UI.

## 4. Provider contract

Use stable resource identifiers internally rather than filesystem paths as primary keys. Treat a URI as a binding in the namespace. This supports rename/move, tombstones and later bindings without assigning a new identity on every rename.

Proposed contract areas:

| Area | Contract requirements |
| --- | --- |
| Request context | Principal, tenant, root, correlation, cancellation and requested properties; request scoped, never process-global identity. |
| Lookup | Resolve authorized URI and, when supported, stable ID; distinguish not found, denied and invalid path. |
| Enumeration | Enumerate children with a stable ordering or documented consistency model; internally paged without claiming a public paging extension. |
| Content | Open read stream; stage write; validate conditions; commit/abort; report content validators and lengths. |
| Properties | Read live/dead qualified properties, protection rules, ordered transactional PROPPATCH. |
| Namespace | Create collection/file, delete, copy, move with overwrite and expected-version conditions. |
| Locks | Atomic acquire, conflict check, refresh, release, discovery and affected-resource lookup. |
| Transactions | Declare guarantees, lock ordering, commit boundaries, crash reconciliation and cancellation semantics. |
| Quota | Check/reserve/release space per tenant/principal/provider; report unknown separately from zero. |
| Change journal | Append committed changes/tombstones, read consistent token ranges, retention/invalidation. |
| Optional features | Search, versions, ACL and domain-resource operations only when implemented. |

Reject providers unable to implement required writable semantics, or publish them as explicitly read-only/restricted profiles. A provider capability flag cannot be a substitute for an exercised implementation.

### Metadata model

The minimum durable model should include:

- Resource: tenant, stable ID, parent/binding, name, resource type, content location, media type, length, creation time, content last-modified and content ETag.
- Dead properties: resource ID, namespace/local name, XML value and applicable language/attributes.
- Locks: opaque token, principal, tenant, lock root ID, scope/depth/type, owner XML, expiration and concurrency version.
- Journal: monotonically ordered position within a defined scope, resource ID, prior/current parent and name where needed, change kind, content/metadata versions and commit timestamp.
- Upload staging: operation ID, target identity, expected validator, staged bytes, quota reservation, expiry and commit state.
- Versions, ACL data and audit records when their optional profiles are enabled.

Separate content validators from metadata revisions where useful. Lock/unlock/refresh must not change the content last-modified time or manufacture a different content ETag. A metadata revision used for synchronization is not necessarily the HTTP representation ETag.

## 5. Operation semantics

### Reads and properties

Implement OPTIONS, HEAD, GET and every PROPFIND form from the protocol plan. Honor HTTP validators and ranges; stream content without requiring full buffering. A collection's HTML representation is not interchangeable with its DAV property representation.

Return exact propstat codes for missing, forbidden and protected properties. For PROPPATCH, evaluate instructions in document order and commit atomically; rollback the entire change when any instruction fails and report appropriate dependency failures.

Implement core properties with documented protection and COPY/MOVE behavior. Preserve namespace-aware XML and reject unsafe XML processing without rejecting legitimate mixed content.

### Namespace mutation

Validate URI syntax and topology, authorization, conditions, locks, quota and provider constraints before committing where possible. Preserve error precedence required by the standards and avoid information leaks before authorization.

MKCOL must handle missing parents, existing resources and unsupported request bodies accurately. Collection DELETE has infinite-depth semantics and must report blocked descendants without pretending the entire subtree disappeared.

COPY supports the required depth/overwrite semantics and dead-property propagation. MOVE changes bindings/identity as defined by the provider without turning into an unreported copy-delete sequence. Parent collection write permissions and locks matter for membership changes.

Collection COPY/MOVE/DELETE may produce 207 member outcomes; distinguish those from atomic PROPPATCH and all-or-none recursive lock acquisition. A namespace operation cannot claim success for members it never processed.

### Safe write lifecycle

For a staged PUT:

1. Resolve the resource and parent under the authenticated tenant; check applicable authorization and conditions.
2. Reserve quota, create an unexposed staging object, and stream with size/time limits.
3. Immediately before commit, atomically recheck validators, locks and namespace identity; a preflight check alone is race-prone.
4. Commit content, metadata and journal visibility with a defined transaction boundary or recoverable staged protocol.
5. Publish notification after durable commit and return validators only when HTTP semantics permit them.

If the connection drops after commit but before response delivery, the client sees an ambiguous outcome. Durable operation state and conditional follow-up reads provide reconciliation; indiscriminate retransmission does not.

Until commit, readers see the prior complete representation, not a half-uploaded replacement. On cancellation/disk-full/backend failure, release reservations and clean staging safely; preserve recovery state when cleanup itself fails.

### Cross-store consistency

Filesystem renames and SQL transactions, or blob promotion and metadata commits, are not one atomic transaction. Define a state machine with durable intent, hidden staging, compare-and-swap metadata, recovery and idempotent cleanup.

For cross-provider MOVE, either reject with a documented status/profile or implement an explicitly tested coordinated operation with accurate partial-failure reporting. Do not advertise atomic cross-backend rename without supporting evidence.

## 6. Locking and concurrency

The lock store must survive restarts and be shared across application instances. An in-memory store may exist in tests or a clearly labeled single-process sample, not as the production class 2 default.

Required semantics:

- Acquire and overlap checks are atomic. Depth-infinity collection locks conflict with covered descendant mutations and other locks as required.
- Lock an unmapped URL by creating the corresponding locked empty resource according to RFC 4918, not by reviving obsolete RFC 2518 lock-null assumptions.
- Evaluate DAV `If`, HTTP preconditions and required token submission with the correct resource association.
- Verify the authenticated lock principal as well as the token. Possession of a token does not grant unrelated write permission.
- Refresh preserves token identity and uses the server-granted timeout; expiry is evaluated consistently across nodes.
- UNLOCK validates lock root, token and permission; privileged administrative unlock is audited.
- MOVE/DELETE/COPY update or remove lock state according to RFC semantics; destination locks and parent membership locks are not overlooked.
- Failed depth-infinity acquisition grants no partial requested tree lock.

Use consistent lock ordering and database isolation/compare-and-swap. If a distributed lease protects backend work, use a fencing/version mechanism so a paused worker cannot commit after its lease expires. Process-local semaphores alone cannot enforce a multi-node protocol.

Test overlapping collection locks, simultaneous acquire, stale renewal, deletion of a locked subtree, a server restart during a write, clock skew, node failover and administrative unlock.

## 7. Storage adapters

### Filesystem: first supported backend

Confine content beneath an explicitly configured root. Reject traversal, encoded separators that change topology, device/UNC escape and symlink/reparse-point escape; define policy for links rather than relying on string prefix checks.

Use a durable metadata catalog for properties, locks, identities and journal state. Do not require NTFS alternate streams or extended attributes for correctness, and do not fall back silently to a temporary directory if metadata persistence is unavailable.

Define external-change policy. A directory watcher can miss events or overflow; periodically reconcile changes. Without a reliable journal/reconciliation model, advertise no authoritative incremental synchronization.

Normalize platform-forbidden names through a documented mapping/rejection policy; do not apply Windows case rules to Linux resources. Verify root confinement with filesystem races, not just static path examples.

### SQL: second backend

Keep namespace, dead properties, locks and journal in transactional tables with tenant-scoped keys. Stream large content from database storage or a coordinated content store. Use unique parent/name constraints and conditional update versions for conflict safety.

Avoid recursive full-tree scans for every operation. Add indexes for parent enumeration, resource ID, active-lock overlap and journal ranges. Database-specific full-text search belongs to the adapter.

### Azure and S3: later adapters

Evaluate Azure Data Lake hierarchical namespace separately from ordinary blob/object storage. Object-key prefixes are not automatically transactional directories.

Use an authoritative metadata catalog, staged/multipart uploads, object-version tracking and garbage collection. The WebDAV validator model must not blindly assume a multipart/storage ETag is a content checksum.

Document rename/copy cost, permissions, credential flow, retry and consistency guarantees. Cloud SDK dependencies stay outside the base server package.

## 8. Authentication, authorization and browser access

Use ASP.NET Core authentication/authorization policies rather than inventing a parallel identity system. Initial examples should demonstrate bearer auth and an application-policy integration; a separate opt-in Basic-over-HTTPS sample can demonstrate legacy clients.

Consider Windows Negotiate for enterprise deployments through host-supported configuration. Digest and MS-OFBA are compatibility projects, not mandatory defaults. Never disable certificate validation, allow plaintext credentials or weaken client policy to make a demo work.

Authorization must cover reads, properties, enumeration, create/delete bindings, source/destination copy/move, locks, ID lookup, reports, versions and search. Filtering directory listings alone is insufficient. Stable-ID aliases and historical versions must enforce the same tenant/resource policies as normal paths.

For browser clients:

- Configure exact allowed origins, credentials policy, allowed DAV methods/request headers and exposed response headers such as DAV, ETag, Lock-Token and relevant range metadata.
- Separate CORS OPTIONS preflight from ordinary authenticated capability discovery. No state-changing request is authorized merely because preflight passed.
- Cookie-authenticated writes need anti-CSRF protection appropriate to the application's origin/session model; an XML/DAV body does not make CSRF irrelevant.
- Do not return an HTML sign-in page as a 200 DAV success. Use a clear protocol failure and the application's supported authentication interaction.
- Protect any bridge ticket/enrollment endpoint independently and never place broad bearer credentials in launch URLs.

Rate-limit expensive recursive PROPFIND, search and REPORT requests; bound XML, request body, depth and work units. Honor cancellation without abandoning an already committed transaction.

## 9. Microsoft Office and native-client compatibility

Treat IT Hit's compatibility guidance as a source of scenarios, not a guarantee about every current version. Much of the site also describes old Windows, Office and browser versions.

Test current supported versions of Word/Excel/PowerPoint, LibreOffice, Windows Explorer via the chosen provider, macOS Finder, and Linux DAV clients where offered. Record version/build, auth flow and server configuration.

Important workflows:

- Probe, create an empty resource, acquire/refresh a lock, PUT content, update properties and unlock.
- Transactional saves using temporary files, replace/rename, delete and retry.
- Concurrent open/edit, 423 handling, ETag conflict and user recovery.
- Non-root DAV endpoint, escaped/Unicode filenames, long paths and unknown MIME types.
- Interrupted save, expired authentication, delayed lock renewal and server restart.

Do not promise real-time Office coauthoring from WebDAV locking or ETag support. Office may offer its own merge workflow; FSSHTTP/WOPI and editor integrations are separate protocols with separate licensing and implementation requirements.

Native Office URL protocols are a launcher path, not a substitute for server authentication or correct locks. Modern sync-root editing is a different path where the desktop agent handles transfer and reconciliation.

## 10. Synchronization, search and versioning

### Synchronization contract

Implement RFC 6578 only when the provider can return correct changes and deletions since a token. Persist a journal of committed operations and tombstones; never derive deleted entries by enumerating only currently existing files.

Tokens are opaque, scope-bound and tied to a journal generation/retention policy. Authorize every request under the current principal and ensure ACL changes cannot leak previously hidden data. If state retention or authorization changes invalidate a token, return an explicit invalid-token response and require safe re-enumeration.

Return a consistent watermark for a page of changes. Pagination/truncation must not skip mutations at the boundary; continuation behavior must be part of the documented profile and RFC response semantics.

For native synchronization, add stable resource IDs and, if required, an explicit ID-resolution endpoint/property profile. RFC 6578 alone does not standardize the vendor's parent-ID and global Sync-ID conventions. Rename/deletion records must contain enough old/new identity information for safe reconciliation.

Push notifications are authenticated, tenant-scoped hints to fetch authoritative changes. Use a durable outbox or equivalent post-commit mechanism, deduplicate events, and recover correctly after disconnected clients. Never make notification delivery the only record of a mutation.

### Other profiles

Implement quota before allowing uncontrolled large writes. Add DASL only with authorized scopes, supported grammar declarations, bounded query complexity and proper backend parameterization.

DeltaV requires immutable versions/history, checked-in/out states, rollback and auto-versioning policy. An object-storage "version" flag does not implement RFC 3253 by itself.

ACL/principal support and CalDAV/CardDAV are later packages/profiles with dedicated requirements, domain serialization and interoperability tests. Keep these optional so a file server does not require calendar/contact dependencies.

## 11. Hosting and operations

Support Kestrel directly and deployment behind IIS or another reverse proxy. Document forwarding headers from trusted proxies, path base, public origin, body-size/time limits, streaming behavior and supported methods.

In IIS deployments, make sure built-in WebDAV modules/request filtering do not intercept the custom endpoint. Test deployment configurations rather than recommending globally disabling protection. Reverse-proxy timeout and request-size limits must agree with application limits.

Multi-instance deployments share metadata, locks, journal and content. Sticky sessions are not a substitute for distributed correctness. Backups must restore a consistent content/catalog pair and invalidate incompatible sync-token generations.

Provide redacted structured logs, activity traces and counters for method/status/duration, active locks, in-flight bytes, journal lag, conflicts, aborted staging and recovery failures. Avoid logging file bodies, credentials or reusable tokens.

Liveness must not perform destructive writes. Readiness should detect unavailable metadata/lock stores and prevent writes when required durability is absent. Include migrations, rollback constraints, recovery tooling, quota management and administrator lock inspection.

## 12. Milestones and acceptance

| ID | Scope | Dependencies | Required exit cases |
| --- | --- | --- | --- |
| S-01 | Contracts, endpoint, requirement ledger and read-only vertical slice | P-01/P-02 wire contract; independent of desktop work. | OPTIONS/HEAD/GET/PROPFIND, path-base and auth isolation, streamed large content, invalid XML/URI cases. |
| S-02 | Writable filesystem provider, metadata catalog and transactional properties | S-01; P-03/P-04 condition/property design. | Atomic ordered PROPPATCH; staged PUT; correct MKCOL/COPY/MOVE/DELETE; 207 failures; crash recovery; no root escape. |
| S-03 | Persistent class 2 locks and multi-instance writes | S-02; P-05 lock model. | Overlap/expiry/refresh/unlock, parent and descendant conditions, restart/failover, accurate classes 1/2/3 advertisement. |
| S-04 | Conformance, native editing and hosting hardening | S-03. | External conformance tool plus original RFC fixtures; isolated native-client scripts; reverse-proxy/IIS tests; failure injection and operability docs. |
| S-05 | Stable identities, quota and RFC 6578 journal | S-02/S-03; E-01/E-03. | Concurrent quota reservation; no lost changes; tombstones; token expiry, paged continuation, external-change reconciliation and tenant isolation. |
| S-06 | SQL provider and sample CMS integration | S-03/S-04 provider contract stability. | Same provider contract suite; transactions, schema upgrades, policy integration and realistic large-data measurements. |
| S-07 | Object storage, search and versioning profiles | S-05/S-06 as applicable; E-02/E-05. | Explicit semantics/cost profile; interrupted multipart writes; bounded authorized search; immutable version rollback. |
| S-08 | ACL/domain servers and advanced extensions | E-04/E-06/E-07/E-08 plus core release. | Profile-specific conformance and third-party interoperability, not merely happy-path demos. |

### Test strategy

Create server unit, provider-contract and integration test projects and add them to the solution. Both supported frameworks need protocol/provider fixtures; real native-client runs can use dedicated OS agents and scheduled release gates.

Use a pinned external tool such as litmus as one input, not sole certification. Evaluate legacy assumptions in its tests against RFC 4918, and maintain original fixtures for requirements it does not cover.

Exercise at least two server processes against the same metadata store. Inject failure before/after staging, catalog commit, blob promotion, journal append and response transmission. Verify data, properties, locks, journal and quota together after restart.

Suggested initial scale targets, to validate and publish on named hardware: 100,000 entries in a collection, multi-gigabyte streams, 100 concurrent mixed clients and 10,000 active locks. Record throughput, p95 latency, memory, storage I/O and recovery time; these are test workloads, not claimed production limits.

## 13. Risks and decisions

| Risk | Mitigation / release condition |
| --- | --- |
| Provider cannot meet atomicity requirements | Reduce advertised profile or defer writes; never silently degrade. |
| In-memory locks pass unit tests but fail scale-out | Shared persistent store and two-process contention/failover tests are mandatory. |
| External filesystem writes bypass journal | Explicit reconciliation strategy and token invalidation; do not advertise unsupported sync fidelity. |
| Backend copy/delete mimics MOVE poorly | Document/reject unsupported cross-store behavior until recovery semantics are proven. |
| Native applications use unexpected save sequences | Record versioned traces and test temp-file/replace flows; avoid filename-based guesses as the only protection. |
| Search, versions or ID aliases bypass authorization | Run authorization contract tests for every access path, including historical and indexed data. |
| Vendor tutorials mix legacy and modern APIs | Use their scenarios but implement against current RFCs, ASP.NET Core and platform guidance. |

## 14. Research anchors

The recommendations are grounded particularly in the site's [class 1 server guide](https://www.webdavsystem.com/server/documentation/creating_class_1_webdav_server/), [class 2 guide](https://www.webdavsystem.com/server/documentation/creating_class_2_webdav_server/), [authorization guidance](https://www.webdavsystem.com/server/documentation/choosing_authentication/programming-auth/), [filesystem sample](https://www.webdavsystem.com/server/server_examples/cross_platform_asp_net_core_file_system/), [SQL sample](https://www.webdavsystem.com/server/server_examples/cross_platform_asp_net_core_sql/), [Azure sample](https://www.webdavsystem.com/server/server_examples/azure_blob_data_lake/), [sync guide](https://www.webdavsystem.com/server/documentation/sync/) and [Office merge guide](https://www.webdavsystem.com/server/documentation/ms_office-merging/).

These sources provide implementation scenarios, not normative requirements or guarantees about current vendor behavior. The architecture above is a proposal for this repository.
