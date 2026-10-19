# Sub-package and desktop plan index

Status: proposed architecture, reviewed 2026-10-19. Names are candidates, not existing implementations or reserved package identities.

Each proposed package has its own plan under `docs\subpackages\`, including dependencies, implementation scope and acceptance criteria. Applications have separate plans because their installation/lifecycle cannot be delivered by NuGet alone.

Related: [protocol completion](PROTOCOL-COMPLETENESS-PLAN.md), [server scenarios](SERVER-SUPPORT-PLAN.md) and [overall delivery sequence](README.md).

## 1. Package plans

The existing `WebDAVClient` remains the portable async core; its implementation plan is [protocol completion](PROTOCOL-COMPLETENESS-PLAN.md). Installing it must not pull in server hosting, cloud SDKs, a database, COM registration or filesystem drivers.

| Candidate package and plan | Delivery boundary | Release position |
| --- | --- | --- |
| [WebDAV.Protocol](subpackages/WebDAV.Protocol.md) | Shared wire XML, names, conditions, results and errors; no host/storage/native dependencies. | Internal project first; extract when client and server both consume it. |
| [WebDAVClient.Extensions.DependencyInjection](subpackages/WebDAVClient.Extensions.DependencyInjection.md) | Factory/builder, DI/options and opt-in diagnostics/resilience. | P-08 integration. |
| [WebDAVClient.Authentication.AzureIdentity](subpackages/WebDAVClient.Authentication.AzureIdentity.md) | Optional Azure.Core.TokenCredential adapter around existing bearer support. | After configuration/lifetime design. |
| [WebDAVClient.Extensions](subpackages/WebDAVClient.Extensions.md) | Quota, search, synchronization, ACL and DeltaV profiles. | One extension boundary initially; no speculative per-verb packages. |
| [WebDAVClient.Compatibility.ITHit](subpackages/WebDAVClient.Compatibility.ITHit.md) | Explicit vendor paging/resume/progress/notification integration. | Only with concrete demand and tested server versions. |
| [WebDAVClient.Sync](subpackages/WebDAVClient.Sync.md) | Durable state, queue, reconciliation, conflicts and cache. | D-01; foundation for native sync. |
| [WebDAVClient.FileSystem.Windows](subpackages/WebDAVClient.FileSystem.Windows.md) | Cloud Files/WinRT, placeholders, callbacks and sync roots. | D-02 feasibility, then D-03 provider. |
| [WebDAVClient.Windows.Shell](subpackages/WebDAVClient.Windows.Shell.md) | Thin native/COM commands and optional managed SDK. | D-07; installed with the agent. |
| [WebDAVClient.FileSystem.WinFsp](subpackages/WebDAVClient.FileSystem.WinFsp.md) | Real Windows mounts with a separate native driver/runtime. | D-05, conditional on backend/license decision. |
| [WebDAVClient.FileSystem.MacOS](subpackages/WebDAVClient.FileSystem.MacOS.md) | File Provider SDK and app-extension project. | D-04; native toolchain/signing gate. |
| [WebDAVClient.FileSystem.Linux](subpackages/WebDAVClient.FileSystem.Linux.md) | FUSE integration with native dependencies. | Later D-05; explicit distribution matrix. |
| [WebDAVClient.DesktopBridge](subpackages/WebDAVClient.DesktopBridge.md) | Versioned activation/IPC/enrollment schema and SDK. | D-06; no unrestricted local API. |
| [WebDAV.Server](subpackages/WebDAV.Server.md) | Host-independent engine and initial provider contracts. | S-01 through S-05 foundations. |
| [WebDAV.Server.AspNetCore](subpackages/WebDAV.Server.AspNetCore.md) | Host routing, DI, auth, streaming and operational adaptation. | S-01 through S-04 hosting. |
| [WebDAV.Server.FileSystem](subpackages/WebDAV.Server.FileSystem.md) | Confined file content plus durable metadata. | First writable provider, S-02/S-03. |
| [WebDAV.Server.Sql](subpackages/WebDAV.Server.Sql.md) | Transactional SQL hierarchy/metadata/content integration. | Second provider, S-06. |
| [WebDAV.Server.Azure](subpackages/WebDAV.Server.Azure.md) | Azure-specific storage SDK and metadata coordination. | Later S-07. |
| [WebDAV.Server.S3](subpackages/WebDAV.Server.S3.md) | S3-specific multipart/content/catalog coordination. | Later S-07. |
| [WebDAV.Testing](subpackages/WebDAV.Testing.md) | Original wire fixtures, fake transport and provider contracts. | Internal helpers first; publish only for external consumers. |

## 2. Application and distribution plans

| Artifact and plan | Responsibility | Dependencies |
| --- | --- | --- |
| [Desktop agent and installers](subpackages/Desktop-Agent.md) | Signed per-user applications, account UI, credentials, sync lifecycle and native installation/update/removal. | Sync plus the selected native adapter; optional bridge/shell. |
| [Browser companion](subpackages/Browser-Companion.md) | Small npm launch helper; optional browser extension separately permissioned/distributed. | Native agent and bridge contract. |

Do not create every package immediately or bundle them into `WebDAVClient.All`. A namespace alone is not a package boundary; publish only when real consumers, dependencies or lifecycle justify it.

## 3. Different experiences require different mechanisms

| User need | Mechanism | Not equivalent to |
| --- | --- | --- |
| Browse remote files with on-demand/offline behavior | Cloud Files sync root or macOS File Provider. | A new drive-letter filesystem. |
| Lock, show versions or copy a link in Explorer | Thin shell commands over agent IPC. | A mount or server authorization. |
| Edit a browser-selected document and save back | Native activation plus enrolled sync root or managed working copy. | A downloaded file with reliable automatic saveback. |
| Open Office directly by URL | Installed Office URI scheme and compatible auth/locking. | Generic editing or browser-session delegation. |
| Expose an actual Windows drive to arbitrary applications | WinFsp/Dokan-style filesystem adapter. | Full NTFS semantics or merely assigning an alias to a sync directory. |
| Browse a non-filesystem namespace in Explorer | Namespace extension, deferred unless explicitly needed. | Normal paths usable by arbitrary applications. |
| Show previews, thumbnails or columns | Bounded feature-specific shell/provider APIs. | Permission to execute documents or hydrate an entire library. |
| Use built-in Windows DAV mapping | Optional legacy WebClient/redirector integration. | A future-proof mandatory dependency; WebClient is deprecated. |

Recommend a Windows sync-root product first. Treat mandatory drive letters as a separate requirement and investment.

The research spans three generations: the legacy driver/service/shell Map Drive product, older browser openers using platform DAV redirectors, and modern Cloud Files/File Provider synchronization. A shared-looking custom URI scheme does not make these interchangeable.

Cloud Files uses Microsoft's built-in Cloud Filter driver; "no new third-party driver" is not "no drivers." WinFsp/Dokan require separately installed native components.

## 4. Dependency direction

```text
WebDAV.Protocol <- WebDAVClient
WebDAV.Protocol <- WebDAV.Server <- WebDAV.Server.AspNetCore
WebDAV.Server <- individual storage providers

WebDAVClient + protocol extensions <- WebDAVClient.Sync
WebDAVClient.Sync <- Windows Cloud Files / macOS File Provider
WebDAVClient + cache policy <- optional WinFsp / FUSE adapters

native SDK + Sync + DesktopBridge <- signed per-user agent
agent IPC <- thin shell components / browser launch workflow
```

Arrows mean "is used by." Agents must work with compatible third-party servers; servers never depend on desktop hosts.

Avoid a universal filesystem abstraction until Windows/macOS prototypes expose genuine shared requirements. A portable sync model does not make their callback and cache ownership identical.

## 5. Delivery gates

P-/E- identifiers are defined in the protocol plan; S- identifiers in the server plan. Detailed D- acceptance cases now live in the linked component plans.

| Gate | Primary plan | Prerequisites and exit evidence |
| --- | --- | --- |
| D-01: durable sync | [Sync](subpackages/WebDAVClient.Sync.md) | P-02/P-03/P-06/P-07; optional E-03. Recovery, conflict and checkpoint safety without data loss. |
| D-02: feasibility | [Windows provider](subpackages/WebDAVClient.FileSystem.Windows.md) | P-01; runs alongside protocol work. API/backend/license decisions, signed install/uninstall proof and callback lifetime stress. |
| D-03: Windows beta | [Windows provider](subpackages/WebDAVClient.FileSystem.Windows.md) and [agent](subpackages/Desktop-Agent.md) | D-01/D-02; P-05 for locking. Native CRUD, hydration, offline/conflict, cancellation, multi-root and reboot. |
| D-04: macOS beta | [macOS provider](subpackages/WebDAVClient.FileSystem.MacOS.md) and [agent](subpackages/Desktop-Agent.md) | D-01 and Apple binding/package spike. Finder/domains/versions/lifecycle/signing gates. |
| D-05: actual mounts | [WinFsp](subpackages/WebDAVClient.FileSystem.WinFsp.md) or [Linux](subpackages/WebDAVClient.FileSystem.Linux.md) | Backend-specific feasibility and safe client/cache semantics. Filesystem contracts, clear durability, licensing and supported-operation matrix. |
| D-06: browser editing | [Bridge](subpackages/WebDAVClient.DesktopBridge.md), [companion](subpackages/Browser-Companion.md) and [agent](subpackages/Desktop-Agent.md) | Native editing path and explicit enrollment/authentication; no credential-bearing launches; replay/origin/conflict cases. |
| D-07: ecosystem release | [Shell](subpackages/WebDAVClient.Windows.Shell.md) and [agent/installers](subpackages/Desktop-Agent.md) | D-03/D-04 as applicable; D-06 if offered. Signed update/rollback, IPC compatibility and unsynced-data-safe removal. |

Calibrate measurable workloads on named hardware: 100,000 items, multi-gigabyte hydration/writeback, 10,000 queued mutations, 24-hour soak and repeated termination at durable checkpoints. These are proposed test workloads, not measured capacity claims.

## 6. Deferred integrations

These are future scope decisions, not additional committed packages. Give each a separate implementation plan only when selected.

| Product idea | Boundary / prerequisite |
| --- | --- |
| Web file browser | Separate web assets/UI, upload/search/selection/paging/accessibility; not the small launch companion. |
| SharePoint connector | Explicit auth and supported APIs/version matrix; generic WebDAV does not guarantee SharePoint Online compatibility. |
| Office/WOPI or online editor connector | Separate server protocol, authorization, licensing and lifecycle. WebDAV does not promise coauthoring. |
| Thumbnail/preview service | Sandboxed conversion, resource limits and content authorization. |
| CalDAV/CardDAV desktop/mobile integration | Dedicated domain clients/parsers and platform calendar/contact providers. |
| Mount/diagnostic CLI | Possible .NET tool for account setup/capability inspection/redacted diagnostics; invokes the agent rather than arbitrarily registering OS state. |

Cloud storage providers already have individual plans above and belong to the server track, not filesystem drivers.

## 7. Decisions and distribution invariants

Before implementation, approve sync root versus required drive letter, supported OS/architectures, acceptable native licensing and offline/locking guarantees.

Default to original implementations over open standards and documented APIs. Public documentation or licensed source availability does not grant permission to copy vendor code. A commercial optional adapter requires explicit redistribution rights, target support and a clean dependency boundary.

NuGet does not install URI handlers, drivers, native extensions or sync roots. The agent plan owns signed distribution, managed configuration, IPC migration and safe removal; platform plans own their native requirements.

Keep credentials in protected OS storage and secrets out of deployment files. Preserve unsynced content during cache pressure, component failure, upgrades and uninstall. Never equate an offline edit with a current remote lock or a locally queued write with server durability.
