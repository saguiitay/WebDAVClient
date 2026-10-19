# Plan: sub-packages, Explorer integration and mounted drives

Status: proposed package/product architecture, reviewed 2026-10-19. No package names below imply an existing implementation or reserved NuGet/npm identity.

Related: [protocol completion](PROTOCOL-COMPLETENESS-PLAN.md) and [server scenarios](SERVER-SUPPORT-PLAN.md).

## 1. Separate the user experiences

"Windows Explorer handler" can describe several unrelated mechanisms. Decide which experience is being delivered before choosing an implementation.

| User need | Correct mechanism | What it does not provide |
| --- | --- | --- |
| Browse remote files in Explorer with on-demand/offline behavior | Cloud Files sync provider plus a local sync root. | A new drive-letter filesystem or automatic remote synchronization without an engine. |
| Add Lock, Versions or Copy Link to an existing Explorer item | Shell command/COM integration, scoped to owned roots. | A mount, content transport or server-side lock enforcement. |
| Open a web link in the associated desktop application and save back | URI/file activation plus a desktop agent and managed local working file or sync-root item. | Authentication delegation or reliable saveback from a normal downloaded file. |
| Open an Office document directly by URL | Installed Office URI scheme where supported, plus compatible server auth/locking. | Generic editing for every application or guaranteed browser-session sharing. |
| Expose an actual `W:` drive to arbitrary Win32 applications | Filesystem adapter such as WinFsp/Dokan or a supported legacy redirector. | Full NTFS semantics merely because a drive letter appears. |
| Open an editable remote namespace without local filesystem semantics | Explorer namespace extension. | Ordinary file paths usable by arbitrary applications. |
| Add preview, thumbnails or metadata columns | Dedicated shell/provider APIs. | A reason to download every file or execute its contents. |
| Mount through the built-in Windows WebDAV client | WebClient/redirector integration when available. | A future-proof foundation; Microsoft marks WebClient deprecated. |

Recommended first product: a Windows sync-root experience, not a custom Explorer namespace extension. Add actual drive-letter mounting as an explicit second product if needed. A mapped alias to a local sync folder may be a convenience, but it is not a network filesystem and must not be marketed as one.

## 2. Lessons from the site

The site contains three generations that should not be collapsed into one architecture:

1. The legacy Map WebDAV Drive product installs a driver, service and shell extension and exposes a drive API.
2. Older Edit Document Opener versions bridge browsers to platform WebDAV clients, including Windows WebClient/mini-redirector.
3. Current WebDAV Drive/virtual-filesystem guidance uses Windows Cloud Files and macOS File Provider with synchronization, placeholders, offline state and Explorer/Finder integration.

The same custom launch scheme can front different underlying architectures. Copying a `davX:`-style URL format does not reproduce its synchronization or authentication implementation. Use a project-owned activation contract rather than appropriating another application's registered protocol.

The modern recommendation does not mean "there are no drivers": Cloud Files uses Microsoft's built-in Cloud Filter driver. It means no new third-party filesystem driver is required by that integration path. WinFsp/Dokan are user-mode programming frameworks with separately installed kernel components.

## 3. Package strategy

Retain the current `WebDAVClient` identity. Avoid a monolithic `WebDAVClient.All` dependency that pulls server/native components into ordinary applications.

Publish a package only when it has a meaningful consumer or dependency/lifecycle boundary. A new namespace or method family is not by itself sufficient reason for a new NuGet package.

| Candidate artifact | Delivery form | Responsibility / dependencies | Release position |
| --- | --- | --- | --- |
| `WebDAVClient` | Existing NuGet | Portable async client, core protocol behavior and simple compatibility API. | Keep lightweight and backward compatible. |
| `WebDAV.Protocol` | Shared project initially; optional NuGet after stabilization | Qualified names, wire XML, conditions, multistatus and protocol errors. No host/native dependency. | Extract during client/server convergence, not speculatively before P-02. |
| `WebDAVClient.Extensions.DependencyInjection` | Optional NuGet | Factory registration, builder integration, options and diagnostics/resilience integration. | After P-08 design. |
| `WebDAVClient.Authentication.AzureIdentity` | Optional NuGet | Verified `Azure.Core.TokenCredential` adapter and explicit resource scopes. | Does not force `Azure.Identity` into core; integrate existing bearer provider. |
| `WebDAVClient.Extensions` | Optional NuGet or modules initially | Search, quota, synchronization REPORT, ACL and DeltaV protocol clients. | One extension package initially unless dependencies justify separation. |
| `WebDAVClient.Compatibility.ITHit` | Optional NuGet | Explicit paging/resume/progress/notification compatibility profile. | Only after recorded interoperability tests and concrete demand. |
| `WebDAVClient.Sync` | Optional NuGet | Durable local state, change reconciliation, transfer queue, conflict policy and cache. | Shared foundation for native sync products. |
| `WebDAVClient.FileSystem.Windows` | Windows-targeted NuGet | Cloud Files/WinRT interop, callbacks, placeholders and sync-root registration; consumes Sync. | Main Windows integration. |
| `WebDAVClient.Windows.Shell` | Native/COM artifacts plus optional managed SDK | Context commands, share/copy-link, property/state/thumbnail integration. | Minimal feature-specific components, distributed with agent installer. |
| `WebDAVClient.FileSystem.WinFsp` | Optional Windows SDK + native dependency | Actual drive/mount callbacks over client/cache; driver install required. | Candidate backend, gated by licensing and feasibility. |
| `WebDAVClient.FileSystem.MacOS` | Platform SDK plus app extension project | File Provider domains, enumeration, versions, hydration and change signaling. | Requires container app, signing and entitlements. |
| `WebDAVClient.FileSystem.Linux` | Platform SDK plus native dependency | FUSE integration; choose supported distributions and mount policy. | Later; no automatic parity promise. |
| `WebDAVClient.DesktopBridge` | Small shared schema/SDK | Versioned activation/IPC command schema and enrollment contracts. | No arbitrary process execution or unrestricted localhost API. |
| Windows/macOS desktop agent | Signed application installers | User-session lifecycle, account UI, sync worker, protected credentials and native activation. | Product, not just a NuGet package. |
| Browser companion | Small npm package; optional browser extension separately | Launch UX, install guidance, capability/version handshake; explicit origin policy. | After native agent exists. |
| `WebDAV.Server` | NuGet | Protocol engine and initial provider abstractions. | Server track. |
| `WebDAV.Server.AspNetCore` | Optional NuGet | Endpoint/DI/auth/diagnostics host adaptation. | Depends on server engine, not desktop. |
| `WebDAV.Server.FileSystem` / `.Sql` | Optional NuGets | Content and durable metadata adapters, one package per backend. | Filesystem first, SQL second. |
| `WebDAV.Server.Azure` / `.S3` | Optional NuGets | Cloud storage integration, SDK and metadata coordination. | Later provider-specific releases. |
| `WebDAV.Testing` | Internal helpers first; optional developer package | Wire fixtures, fake transport and provider contract harness. | Publish only when external consumers need it. |

Do not create every row immediately. Separate CalDAV/CardDAV, editor connectors and identity-provider integrations only when implementation requirements justify the dependency. Keep ordinary transfer progress and validators in the client rather than inventing tiny packages for individual features.

Avoid introducing a platform-neutral filesystem abstraction before the Windows and macOS prototypes expose actual shared needs. The sync model can be portable without pretending that CFAPI, File Provider and FUSE have identical callback semantics.

### Dependency direction

```text
WebDAV.Protocol <- WebDAVClient
WebDAV.Protocol <- WebDAV.Server <- WebDAV.Server.AspNetCore
                                      storage adapters

WebDAVClient + protocol extensions <- WebDAVClient.Sync
WebDAVClient.Sync <- Windows Cloud Files / macOS File Provider
WebDAVClient + cache policy <- optional WinFsp / FUSE adapters

native SDK + Sync + DesktopBridge <- signed user-session agent
agent IPC <- thin shell components / browser launch workflow
```

Arrows mean "is used by." Native agents may connect to any compatible server; they must not require this project's server packages. Server packages must never reference a desktop host.

## 4. D-01: durable synchronization foundation

Priority: P1. Dependencies: P-02/P-03/P-06/P-07; E-03 for efficient standards-based incremental sync. A limited polling profile can prototype earlier.

### Persistent state

Use a local transactional database for account/root identity, remote resource IDs, href mappings, validators, token checkpoints, pending operations, cache state and failure state. Select the database during the implementation spike, with schema migration and backup/recovery tests.

Store credentials outside that database in OS-protected storage. Queue entries reference an account/credential handle, not a copied bearer token.

Define states such as remote-only, hydrated-clean, locally-modified, uploading, conflict, deleted-pending, authentication-required and unrecoverable-error. Persist transitions before acknowledging work that must survive process failure.

### Incoming synchronization

Prefer RFC 6578 when actually advertised and implemented. Apply each returned batch and its checkpoint transactionally. If the process stops mid-batch, replay safely; never advance the token ahead of persisted local changes.

If the server has only PROPFIND, reconcile authorized folders using complete snapshots and validators. An incomplete listing, 403, timeout or truncated response must never be interpreted as proof that all absent items were deleted.

Stable remote IDs improve rename/move detection. Without them, document a path-based fallback and conservative conflict behavior. Equal ETags do not prove that two paths are the same object.

Notifications trigger reconciliation. Reconnect always checks authoritative state; dropped, repeated, reordered or forged notifications cannot directly delete local content.

### Outgoing changes and conflicts

Use conditional writes with the validator from the last known synchronized version. On 412, preserve local content and surface conflict; never silently use last-writer-wins.

Coalesce safe redundant work, but retain dependencies: create parent before child, upload staged content before replace, resolve rename before subsequent path operations. Use stable operation IDs and reconciliation for ambiguous server outcomes.

Handle application save patterns including temp-file/rename/replace. Local byte-range writes are not automatically remote partial PUT; cache dirty ranges and safely upload/commit the final representation using the selected server profile.

Offline changes and locks have different guarantees. A remote lease may expire while offline; offline edits remain unsynchronized local changes and require conditional reconciliation after reconnect. Do not show "exclusively locked on server" without a current granted lease.

### Cache and data safety

Bound cache size and per-account transfer concurrency. Evict only clean, unpinned data; never evict unsynced edits to satisfy a cache quota. Use atomic cache writes, local access controls, content validation and cleanup of abandoned staging.

Track content and metadata versions independently when available. Missing content validators are an explicit degraded capability, not permission to assume content is unchanged.

Acceptance: restart/replay after every state transition, two-device concurrent edits, expired token, renamed parent with pending child upload, failed/incomplete enumeration, disk full, credential expiry, revoked access, lost response after commit, and manual conflict resolution all preserve user data.

## 5. D-02: Windows feasibility and backend decisions

Priority: P1. Dependencies: P-01; can run alongside protocol completion.

Prototype each product requirement with a tiny isolated proof, not a full implementation:

| Question | Experiment | Decision output |
| --- | --- | --- |
| Cloud Files sync-root viability | Register a root, populate placeholders, hydrate a file, perform edit/rename/delete, unregister. | Minimum supported Windows builds, filesystem requirements and package identity strategy. |
| .NET/native boundary | Exercise async callback completion/cancel, handle ownership, callback retention and process exit under stress. | Supported interop approach and a lifecycle specification. |
| Actual drive-letter requirement | Compare Cloud Files root UX with WinFsp and, if needed, Dokan. | Choose at most one initial driver backend or explicitly defer drive letters. |
| Shell deployment | Register minimal commands/state provider in a signed packaged app; verify cleanup. | Native COM surface, bitness and installer requirements. |
| Application behavior | Capture simple Office and representative non-Office save sequences. | Required local/remote lock and replace semantics. |

Microsoft's Cloud Files documentation describes availability from Windows 10 version 1709; that is API history, not a recommendation to support every old Windows build. Target currently supported OS releases, initially Windows 11 x64/ARM64, and verify required API/NTFS/package constraints. Make Windows 10/LTSC support an explicit product decision.

Do not choose ProjFS merely because it also projects files. Microsoft describes it as suited to high-speed backing stores and directs cloud-sync scenarios requiring status/progress to Cloud Files.

Acceptance: a written platform/licensing decision, working install/uninstall proof and a callback cancellation/leak stress run. The spike must reject unsuitable approaches, not just select the first one that displays a directory.

## 6. D-03: Windows Cloud Files provider and agent

Priority: P1. Dependencies: D-01/D-02; P-05 for integrated locking.

Implement:

- Per-user root registration, account enrollment and secure credential storage.
- On-demand directory population and content hydration, including read ranges requested by the platform.
- Pin/unpin and online/offline state; clean dehydration without deleting unsynced data.
- Local changes, namespace operations and durable writeback.
- Standard platform state/progress indicators plus explicit conflict/authentication/error UI.
- Cancellation, bounded concurrency, backoff and recovery after sleep/network/process interruption.
- Multiple accounts/roots without sharing credentials or identity mappings.

Keep provider callbacks short and obey native completion/timeout semantics. Do not block Explorer callbacks on interactive authentication or run network work in an in-process shell extension. Retain native callbacks/handles safely until outstanding work is completed or cancelled.

Use a per-user agent, not an unrestricted system service holding every user's credentials. A privileged installer/driver helper, if needed for another backend, should expose a narrowly scoped administrative operation surface.

Automatic remote locks require a documented policy for write-open, transactional replacement, renewal and release. Local file locks, share modes and remote DAV locks are not equivalent. Test applications that open/write repeatedly, crash or keep documents open overnight.

Acceptance:

- Placeholders are visible before content is fetched; reading hydrates; pinned content opens offline.
- Local writes survive an agent crash and reconcile conditionally.
- Server-side changes update clean files but cannot overwrite local dirty content.
- Native errors are meaningful in Explorer and the agent; no failed save is marked in-sync.
- Sleep/resume, reboot, expired auth and root removal recover without orphaning content.
- Two users/accounts cannot read each other's cache, IPC commands or secrets.

A Cloud Files provider presents a directory-based sync root. If the product also exposes a drive letter, document exactly which additional mechanism does so; do not conflate root registration with creating a filesystem volume.

## 7. D-04: macOS File Provider

Priority: P2. Dependencies: D-01 and a macOS-specific native-binding/package spike.

Ship a container application and replicated File Provider extension. The extension handles domains, enumeration, stable identifiers, item/content versions, content fetching and changes; the container handles account enrollment and lifecycle UI.

Adapt the sync model to Apple's ownership of local copies rather than duplicating a second independent cache with conflicting authority. Use persistent ID/href mapping when the server lacks directly resolvable stable IDs; explicitly limit rename fidelity and reconciliation where identity cannot be established.

Include sign-in UI integration, Keychain-backed credential access, app-group sharing as required, extension entitlements, signing, notarization and upgrade compatibility. Native binding feasibility with the supported .NET/macOS toolchain is a prerequisite, not a detail assumed from older Xamarin samples.

Acceptance: Finder population, fetch/edit/save/rename/delete, remote changes, offline materialized files, conflicts, revoked auth, multiple domains, extension termination/restart, container update, and clean removal. Validate on current macOS versions and architectures chosen for support.

## 8. D-05: real drive mounting and Linux

Priority: P2/P3. Dependencies: D-02 for Windows; P-03/P-06 and a cache/operation policy for writable adapters.

### Windows backend comparison

| Backend | Suitable use | Constraints |
| --- | --- | --- |
| Cloud Files | Explorer/Finder-style cloud sync UX on Windows. | Local sync root, not a true remote volume; built-in platform restrictions. |
| WinFsp | Real drive letter/mount point and user-mode filesystem callbacks. | Installed signed driver; native dependency; GPLv3 with FLOSS exception or commercial licensing must be reviewed for distribution. |
| Dokan | Alternative user-mode filesystem and network-drive integration. | Installed driver/network components; component-specific LGPL/MIT licenses; interop/support burden. |
| Built-in WebClient | Best-effort legacy interoperability tool. | Deprecated; authentication, service, size and platform constraints; not a mandatory dependency. |
| ProjFS | Specialized high-speed projection scenarios. | Does not supply the cloud-sync UX required here; not the default. |
| Custom kernel driver | No initial recommendation. | Very high correctness, security, signing, deployment and maintenance cost. |

WinFsp and Dokan licensing are not interchangeable. Evaluate the exact pinned versions, exceptions, bindings and redistribution model before shipping. Do not claim that either backend is covered automatically by this repository's MIT license.

### Filesystem semantics

Define behavior for random reads/writes, truncation, flush/fsync, close, rename/replace, case sensitivity, reserved names, share modes, delete-on-close, directory enumeration and timestamps. Map unsupported operations explicitly, including hard links, alternate streams, ACLs, sparse files or memory mapping where not safely supported.

A successful local flush needs an explicit durability meaning: server-committed or durably queued locally. Never report server durability when only a volatile buffer exists.

Use bounded read caching and a writeback journal for cached mode; distinguish an online-only mode that fails clearly on disconnect. Do not issue one remote PUT for each tiny local write. Cache metadata cautiously and invalidate after namespace mutations.

### Linux

Choose a supported libfuse API/binding and distribution matrix. FUSE is native/kernel integration, not a pure managed package. Consider a `davfs2`/GVfs interoperability sample when it meets the requirement instead of owning another filesystem implementation.

Default to per-user mounts. Any `allow_other` or shared mount requires explicit permission enforcement and a documented threat/ownership model; do not expose one user's authenticated server session as a machine-wide unrestricted mount.

Acceptance: OS filesystem contract tests, representative app saves, large/sparse logical data, rename races, disconnect/reconnect, full cache, crash during flush, multiple mounts, driver unavailable and clean unmount with pending writes.

## 9. D-06: browser-to-desktop editing bridge

Priority: P2. Dependencies: D-03 or D-04 for sync-root editing; P-03/P-05/P-06 for a separate managed-working-copy mode.

### Activation and authorization

Use a project-owned URI activation format and, optionally, RFC 4709 DAV mount documents. RFC 4709 describes an informational mounting-document format; it is not a filesystem implementation or assurance that browsers have a compatible handler installed.

A launch request contains only a bounded command and a resource reference, plus a short-lived one-use ticket when the server supports that trust model. It must not contain passwords, bearer tokens, session cookies, raw executable paths or shell command lines.

Two enrollment modes are necessary:

| Mode | Behavior |
| --- | --- |
| Managed server/portal | Native app is enrolled for a trusted origin/account; server issues an audience/device-bound launch ticket; redemption verifies user, resource, action and expiry. |
| Arbitrary compatible DAV server | Agent asks the user to enroll/confirm the endpoint and authenticates natively using the server's supported method. Do not assume the server has this project's ticket endpoint. |

Validate endpoint scheme/origin, user enrollment, allowed command, resource containment and ticket replay. Custom URI activation does not authenticate the web page that caused it, and an Origin header alone is not a credential.

If loopback HTTP is needed, bind only to loopback, authenticate every request, validate Host/Origin, reject DNS-rebinding/cross-origin misuse and avoid a global unauthenticated "open any URL" API. Prefer OS activation and authenticated per-user IPC where possible.

Do not scrape browser credential databases or automatically export cookies. The vendor describes cookie-copying integrations, but those have privacy, permission and lifecycle implications. Prefer native OAuth/system-browser sign-in; isolate any unavoidable browser extension/native-messaging compatibility flow behind explicit consent and narrow permissions.

### Editing workflow

Resolve the resource into an existing enrolled sync root where possible. Hydrate and launch via OS association; monitor the provider's durable local changes, not just whether the launched process exits.

For an optional working-copy mode, download to a protected workspace, preserve remote validator/lock context, monitor replace/rename saves, upload conditionally, surface conflicts and retain unsynced files. A process may hand the document to an existing application instance, so process termination is not a reliable "editing finished" signal.

Commands can include Open/Edit, Reveal Folder, Lock/Unlock, Versions and explicit Print. Printing, arbitrary application selection and externally linked assets need separate consent and policy. Never enable remote arbitrary execution through extensible "commands."

Acceptance: agent absent/outdated, no association, user cancels, ticket expiry/replay, untrusted origin, URI escaping, revoked account, local edits while offline, save conflicts, and application crash. Failure leaves the user with actionable state and preserved content.

### Browser companion scope

Begin with a small framework-independent JavaScript helper and accessible install/error UI, not a second full WebDAV client. A browser extension is optional for a specific capability; distribute and permission it independently.

For direct browser file management, either build a separately scoped browser client/web UI or an ASP.NET backend-for-frontend. The existing .NET package does not run in every browser merely because its API is async. Blazor/WASM additionally requires browser HTTP/CORS/header constraints to be validated.

## 10. Shell commands, previews and optional integrations

Keep shell handlers thin and resilient. Prefer platform-supported packaged commands and out-of-process components where possible. If an in-process native COM shim is required, it forwards bounded authenticated IPC and never owns networking, synchronization or secrets.

Initial Windows commands: Reveal on Server, Copy Link, Keep Offline/Free Space through the platform, Lock/Unlock, Show Sync Errors and Resolve Conflict. Enable actions only for owned roots and valid item states.

Use native cloud-provider status indicators before adding legacy icon-overlay handlers. Add thumbnails only through bounded, authenticated metadata/content retrieval or a trusted rendering service. Do not execute/render arbitrary documents inside Explorer's process or recursively hydrate an entire library for thumbnails.

Consider versions, custom property columns and preview handlers after the core agent is stable. An Explorer namespace extension is deferred unless non-filesystem navigation is explicitly needed.

Optional later products inspired by other site sections:

| Product | Boundary / prerequisite |
| --- | --- |
| Web file browser | Separate web asset/component package with upload, selection, search, paging and accessibility; not core protocol. |
| SharePoint connector | Explicit authentication and supported SharePoint APIs/version matrix; do not assume arbitrary SharePoint Online paths behave as generic WebDAV. |
| Office/WOPI or other online editor connector | Separate server-side protocol, authorization, licensing and lifecycle project. |
| Thumbnail/preview service | Sandboxed conversion, resource limits and content authorization. |
| CalDAV/CardDAV desktop/mobile integration | Separate domain clients and platform document/contact/calendar providers. |
| Cloud storage server providers | Belong to the server track, not filesystem drivers. |
| Mount/diagnostic CLI | A .NET tool can configure accounts, inspect capabilities and collect redacted diagnostics; it invokes the agent instead of registering arbitrary OS state directly. |

## 11. Installers, distribution and support

NuGet does not register URI schemes, install an app extension, provision a signed driver, enroll an account or configure Explorer. Ship signed installers/app bundles separately and make install-time permissions explicit.

| Platform | Required deliverables |
| --- | --- |
| Windows Cloud Files | Packaged or otherwise supported signed desktop distribution, app identity/COM registration strategy, per-user roots, activation and auto-start. |
| Windows driver backend | Separate prerequisite installer/version detection, administrative consent, signed-driver policy and rollback. Mount execution may still run as a normal user. |
| macOS | Signed/notarized container and File Provider extension, entitlements, app-group/Keychain setup and update policy. |
| Linux | Native dependency checks, distribution packages, mount configuration, desktop activation and per-user background lifecycle. |
| Browser | Versioned helper assets and optional extension/store/native-messaging manifests with minimal permissions. |

Enterprise distribution may need Intune/MSIX, signed MSI alternatives, managed root/account policy, proxy and certificate support and deterministic upgrade configuration. Keep secrets out of machine-readable deployment files.

Persist versioned configuration and provide migration/rollback rules. Agent and shell/native components must negotiate compatible IPC versions. A newer shell command must not send an unknown destructive operation to an older agent.

Uninstall/unregister policy must preserve unsynced content: stop new work, warn about pending uploads, offer safe export/recovery, unregister roots/providers/handlers, remove credentials with consent and delete only explicitly selected clean cache. Never recursively delete a user's working root as routine cleanup.

## 12. Milestones and acceptance gates

| ID | Deliverable | Dependencies | Exit evidence |
| --- | --- | --- | --- |
| D-01 | Durable sync/cache engine | Client conditions/results/transfers; optional E-03. | Recovery and conflict matrix passes without silent data loss. |
| D-02 | Platform/backend spikes | P-01; independent of full sync engine. | Chosen APIs/backend/license model; install and callback lifecycle proof. |
| D-03 | Windows provider and agent | D-01/D-02; P-05 for locks. | Native CRUD, hydration, offline, conflict, cancellation, multi-root and reboot cases. |
| D-04 | macOS provider and app | D-01 plus Apple platform spike. | Domains, Finder, versions, extension lifecycle and signing gates. |
| D-05 | Actual drive mounting / Linux adapter | Backend-specific feasibility; safe client/cache operations. | Filesystem contract and distribution tests; clear durability/unsupported-operation policy. |
| D-06 | Browser launch/edit workflow | A native editing path and authentication/enrollment design. | No credential-bearing launches; replay/origin/install/conflict cases pass. |
| D-07 | Shell extras and enterprise release | D-03/D-04; D-06 when offered. | Signed deployment, IPC compatibility, update/rollback and unsynced-data-safe uninstall. |

Suggested measurable release workloads: 100,000 remote items, multi-gigabyte hydration/writeback, 10,000 queued offline mutations, 24-hour soak and repeated kill/restart at queue checkpoints. Establish environment-specific latency/memory budgets in the spikes; do not reuse vendor marketing figures as this project's measured capacity.

## 13. Build-versus-integrate decision

The default plan is an original implementation over open standards and documented OS APIs. No IT Hit SDK license, proprietary source, application installer or protocol implementation is included or implied.

A commercial optional adapter can be evaluated if it materially reduces maintenance cost, but must have explicit redistribution rights, target-platform support, licensing documentation and a clean dependency boundary. Public samples and "source available with license" claims are not permission to copy code.

Before implementation, confirm four product decisions: sync root versus mandatory drive letter; supported OS versions/architectures; acceptable native licensing; and offline/locking guarantees. The recommendations above provide a concrete starting point without treating those tradeoffs as already approved.

## 14. Research anchors

Key sources are the site's [modern drive architecture](https://www.webdavsystem.com/ajax/next_version/), [architecture comparison](https://www.webdavsystem.com/ajax/next_version/comparison/), [Windows drive sample](https://www.webdavsystem.com/client/examples/webdav_drive/), [macOS sample](https://www.webdavsystem.com/client/examples/webdav_drive_mac/), [legacy mounting API](https://www.webdavsystem.com/mapdrive/programming/), [browser cookie handoff](https://www.webdavsystem.com/ajax/programming/open-doc-webpage/cookies-authentication/) and [enterprise configuration](https://www.webdavsystem.com/ajax/programming/open-doc-webpage/install/webdav-config-file/).

Platform choices are cross-checked against [Microsoft Cloud Files](https://learn.microsoft.com/en-us/windows/win32/cfapi/build-a-cloud-file-sync-engine), [ProjFS guidance](https://learn.microsoft.com/en-us/windows/win32/projfs/projected-file-system), [WebClient deprecation](https://learn.microsoft.com/en-us/windows/whats-new/deprecated-features), [Apple File Provider](https://developer.apple.com/documentation/fileprovider/replicated-file-provider-extension), [WinFsp](https://github.com/winfsp/winfsp), [Dokan](https://github.com/dokan-dev/dokany) and [libfuse](https://github.com/libfuse/libfuse).

The related User File System documentation on [incoming synchronization](https://www.userfilesystem.com/programming/synchronization/incoming_sync/), [Sync-ID](https://www.userfilesystem.com/programming/synchronization/sync_id/) and [content/metadata ETags](https://www.userfilesystem.com/programming/etags/) informs the durable sync model. Its identity conventions are not automatically requirements of RFC 6578.
