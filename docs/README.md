# WebDAV ecosystem implementation plans

Research date: 2026-10-19. Status: proposals, not implemented features or release commitments.

## Recommendation

Evolve this repository in three connected but independently releasable tracks:

1. Complete and demonstrate RFC 4918 client interoperability before adding more protocol verbs. The existing client already implements the main verbs; its largest gaps are structured outcomes, conditional requests, complete property/lock semantics, and interoperability evidence.
2. Add a separately packaged, storage-independent ASP.NET Core WebDAV server. A client library cannot become a server by exposing its outbound methods as endpoints. Authorization, namespace mutations, durable properties, locking, and crash recovery require their own engine and provider contracts.
3. Build desktop integration on top of a durable synchronization layer. Prefer Windows Cloud Files and macOS File Provider for Explorer/Finder integration. Treat actual drive-letter filesystems, browser protocol handlers, and shell extensions as different products with different dependencies and deployment models.

The result should remain usable as a small .NET client package. Installing `WebDAVClient` must not install ASP.NET Core hosting, cloud SDKs, a sync database, COM registration, browser extensions, or filesystem drivers.

## Documents

| Document | Purpose |
| --- | --- |
| [Protocol completeness plan](PROTOCOL-COMPLETENESS-PLAN.md) | Current implementation, RFC 4918 completion backlog, standard extensions, compatibility policy, and conformance gates. |
| [Server support plan](SERVER-SUPPORT-PLAN.md) | Hosting architecture, storage and transaction contracts, authorization, locking, synchronization, deployment, and server milestones. |
| [Sub-packages and desktop plan](SUBPACKAGE-AND-DESKTOP-PLAN.md) | Proposed package boundaries, Explorer handlers, drive mounting, desktop editing, synchronization, installers, and platform decisions. |

Read these as supplements to `ROADMAP.md`, not a declaration that its previously proposed APIs exist. Repository behavior is grounded in the checked-in source at commit `ac0ef8a7b637b03114cf18a16a1e60d9a53396a1`; the NuGet publication state was not verified.

The research scanned 3,226 reachable URLs across the primary website, its .NET/Java/AJAX reference hosts and the related User File System website, then examined relevant architecture and programming material in depth. Coverage excludes robots-disallowed, authenticated, unlinked and query-specific content; it is not a manual review of every generated API member. Useful source links are retained in each plan rather than separate crawl reports.

## What "full WebDAV support" means

The first release objective is complete, tested **RFC 4918 client support**, with a separately verified server supporting classes 1, 2, and 3 where the configured provider can meet their requirements. Classes are resource/server compliance declarations, not a client certification. Class 3 denotes RFC 4918 revisions over RFC 2518; it is not a superset of optional extensions, and it does not require class 2 locking.

The next objective is an explicitly enumerated extension portfolio: quota, search, collection synchronization, ACL/principals, DeltaV, and optional calendar/contact profiles. Other standard extensions have their own capability gates. Vendor paging, upload-progress reports, and push notifications must never be presented as universal WebDAV requirements.

There is no honest single "all WebDAV extensions" checkbox. Release documentation must name RFCs, feature packages, provider restrictions, and the versions of real clients/servers exercised. The protocol plan gives each family a delivery or deferral path.

## Current baseline and constraints

The library targets `net8.0` and `net10.0`. `IClient`, `Client`, the HTTP wrapper, response parsers, authentication handler, and models are client-side code. There is no checked-in inbound WebDAV server, sync engine, desktop agent, filesystem provider, or browser companion.

Already present: PROPFIND variants, PUT/GET and partial-transfer entry points, MKCOL, DELETE, COPY/MOVE, exclusive LOCK/UNLOCK/refresh, one-property PROPPATCH, OPTIONS, bearer authentication, custom headers, cancellation parameters, and hardened origin/XML handling.

The existing MSTest project **is** in `WebDAVClient.sln` and targets both frameworks. Earlier descriptions of an empty, excluded unit-test project are stale. The console application remains a manual smoke tool, not an interoperability suite.

Preserve the following across all tracks:

- Existing client ownership/disposal rules, constructor behavior, `BasePath`/`Server` normalization, and collection/file href distinction.
- Trailing cancellation tokens and `ConfigureAwait(false)` in library code.
- HTTP details and partial failures in errors; the `WebDAVException` family and existing 409 compatibility contract.
- Secure defaults and origin/tenant boundaries; no weakening to accommodate a desktop client.
- The original package identity and its lightweight dependency footprint.

Changes to `IClient` affect third-party implementers as well as callers. New advanced interfaces or facades are preferable to repeatedly expanding the existing interface. A major-version change needs an API compatibility report, migration examples, and explicit acceptance.

## Integrated delivery sequence

Work-package IDs are defined in the individual plans. Dependencies below are actual prerequisites, not a demand to serialize independent work.

| Gate | Deliverable | Required prerequisites | Can proceed independently |
| --- | --- | --- | --- |
| G0 | Requirements ledger, current behavior fixtures, package/API decisions | None | Server provider-contract prototypes; native platform feasibility spikes. |
| G1 | Reliable client core: P-01 through P-07 | G0; individual dependencies in protocol plan | Server S-01 can use protocol fixtures before all advanced client APIs ship. |
| G2 | Client conformance and operational integration: P-08 through P-10 | G1 | Read-only server development and authentication adapters. |
| G3 | Durable class 2/3 server: S-01 through S-04 | Shared wire contracts; S-02/S-03 ordering | Windows/macOS installation and callback-lifetime spikes. |
| G4 | Sync-capable client/server and durable sync engine | E-01/E-03; S-05; D-01 | Browser bridge schema and shell command UX prototypes. |
| G5 | Windows provider and desktop agent beta | D-01, D-02, D-03 | macOS D-04; optional WinFsp proof of concept D-05. |
| G6 | Native editing and browser-to-desktop workflow | D-03/D-04 and D-06 | Optional web file browser, Linux adapter, advanced extensions. |
| G7 | Hardened ecosystem release | Conformance, recovery, packaging and supported-platform gates | Demand-driven optional integrations. |

A native read-only prototype may use basic listings before G4; it is not a production synchronization product. A useful online-only filesystem can also be delivered without RFC 6578 if its refresh and conflict limitations are explicit.

## Prioritization

| Priority | Work | Why |
| --- | --- | --- |
| P0 | Multistatus/error fidelity, conditionals, properties, locks, safe ranges | Prevent false success, lost updates, and unreliable edits. |
| P0 | Server transactional namespace, authorization, persistent locks | Correctness cannot be retrofitted around an endpoint-only implementation. |
| P1 | DI/auth adapters, diagnostics, capability policy, quota | Makes client and server operable in real applications. |
| P1 | Sync journal, stable identities, tombstones, conflict recovery | Shared foundation for Explorer/Finder and offline work. |
| P1 | Windows Cloud Files plus signed user-session agent | Main Explorer integration path without dependence on WebClient. |
| P2 | macOS provider, browser bridge, shell commands, search/versioning | Broader workflows after reliable transport and synchronization. |
| P2 | Actual drive-letter backend | Separate investment if a drive letter, rather than an Explorer sync root, is a requirement. |
| P3 | CalDAV/CardDAV, extended DeltaV, SharePoint/editor-specific integrations | Specialized products; do not block core WebDAV delivery. |

## Sizing and staffing assumptions

These are planning ranges in focused engineer-weeks, not estimates derived from the vendor's product size. They include focused tests and documentation, but exclude customer migration, external audits, procurement, and store-review delays.

| Workstream | Initial planning range | Principal uncertainty |
| --- | --- | --- |
| RFC 4918 client completion and interoperability | 8-14 | Compatibility of richer results with legacy methods and real-server differences. |
| ASP.NET Core server with filesystem provider and durable metadata | 12-20 | Transaction recovery, recursive lock behavior, external filesystem changes. |
| SQL and object-storage adapters | 4-8 each | Atomic namespace model, object consistency, identity and quota integration. |
| Durable sync engine plus first Windows provider/agent | 14-24 | File-operation semantics, callback lifetime, recovery, local cache behavior. |
| macOS provider and packaging | 8-14 additional | Native binding, extension lifecycle, entitlements and identity requirements. |
| Browser bridge, shell commands and installer hardening | 6-10 | Secure enrollment, application save behavior, platform packaging. |

These ranges overlap and must not simply be summed into a calendar promise. Start with one protocol/backend engineer and one desktop engineer; add platform expertise before shipping native extensions. Re-estimate after G0 and each feasibility spike.

## Decisions required before implementation

The recommendations below are defaults for planning, not decisions already taken by the maintainer.

| Decision | Recommended default | Revisit when |
| --- | --- | --- |
| Client API evolution | Additive advanced facade/interfaces; retain existing `IClient`. | A clean break is explicitly approved for a major version. |
| Shared protocol distribution | Extract a minimal shared wire layer once both client and server consume it. | Independent consumers require a public package sooner. |
| Server hosting | ASP.NET Core on supported .NET LTS targets. | A concrete embedded-host requirement exists. |
| Server metadata | Durable transactional metadata store, including for filesystem content. | A provider proves equivalent durability without it. |
| Windows integration | Cloud Files sync root with native shell integration. | Product requirements explicitly need a drive letter or non-NTFS host. |
| Driver-backed mounting | Select one backend after licensing/semantics spike; WinFsp is a candidate, Dokan an alternative. | Distribution requirements rule out the selected backend. |
| Modern auth | Retain the existing token-provider abstraction; adapters are optional. | A host requires a different standard .NET credential abstraction. |
| Browser handoff | One-use authorized launch ticket, explicit enrollment and native authentication. | A controlled legacy environment requires an isolated compatibility adapter. |
| Offline conflict policy | Never silently overwrite; preserve both versions and surface resolution. | A customer explicitly chooses a narrower documented policy. |
| Commercial dependencies | No required IT Hit runtime or copied implementation. | An optional integration is separately approved and licensed. |

The existing roadmap uses illustrative credential names/namespaces that need correction before implementation. For Azure Identity integration, the verified type is `Azure.Core.TokenCredential`; do not add a dependency based on an assumed `System.ClientModel.Primitives.TokenCredential` API.

## Completion policy

An implementation milestone is complete only when its acceptance cases pass, failure behavior is documented, supported platforms/providers are named, and packages can be installed and removed cleanly. Do not label basic verb coverage as "full protocol support," a shell menu as "drive mounting," or a successful transfer as "safe synchronization."

This documentation change does not implement any proposed feature, adopt the vendor's SDKs, alter the existing version, or change the current public API.
