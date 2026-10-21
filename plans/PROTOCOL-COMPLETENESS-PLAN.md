# Plan: complete WebDAV protocol support

Status: partially implemented; not a full-conformance claim. Historical baseline:
repository commit `ac0ef8a7b637b03114cf18a16a1e60d9a53396a1`, reviewed 2026-10-19.

### Core requirement and lifecycle update (2026-10-21)

The durable [RFC 4918 client requirement ledger](../docs/rfc4918-requirements.md)
now maps sections 4–18 to concrete APIs, deterministic test evidence, server-only
responsibilities and explicit unsupported/partial scope. It does not certify
server conformance or claim every historical milestone complete.

`DavLockLease` adds a dependency-free, explicitly renewed managed write-lock
lifecycle with validated acquisition identity/scope/depth, conservative monotonic
expiry, uncertainty on failed renewal/release, serialized operations and at most
one release attempt. There is no timer, automatic retry, reacquisition or durable
lease store. See [lock lifecycle reference](../docs/lock-lifecycle.md).

Core corrections also cover RFC 4918 empty-resource GET 204, no-cache directives
for DAV If requests through non-DAV-aware proxies, coded capability URIs containing
commas, and ignoring unexpected lock-kind XML extensions without accepting foreign
lookalikes. Timeout requests and grants support the full unsigned seconds range,
including immediately expired zero-second grants. Focused deterministic tests
cover these contracts on both target frameworks; independent-server evidence
remains a separate requirement.

A follow-up core receive-validation pass now enforces escaped structural resource
href forms/origins/uniqueness, known singleton cardinalities and element-only
containers in buffered and streamed core responses. Generic REPORT profile
cardinalities, arbitrary property/owner XML and legacy projection remain separate.
LOCK acquisition requires its token header; refresh requests are bodyless but
successful refresh responses must provide HTTP 200 with lockdiscovery XML.
The prior low-level empty-refresh-response tolerance is removed. Optional
lockroot omission retains the section 14.12 SHOULD applicability rationale.
Present lockroot hrefs remain scalar, raw evidence-only metadata, including
server-produced unescaped filenames; they are never promoted to request targets
or used to infer lock ownership.

Independent WsgiDAV 4.3.5 / cheroot 11.1.2 testing exposed a .NET URI
well-formedness false rejection for combined escaped Unicode/reserved octets;
wire grammar validation now preserves those identities. For interoperability,
one valid bare acquisition header token is accepted only with one exact
lockdiscovery match, while its nonstandard wire form remains inspectable.
Basic-authentication runs pass 19 observations plus two unavailable/unsupported
extension observations on each target framework, with confirmed clean teardown.
This narrow evidence does not certify other servers or all RFC server invariants.
PROPPATCH target validation additionally recognizes equivalent escape-hex digit
case without weakening case-sensitive path/query or escaped-separator identity.
Reserved-plus aliases remain distinct by default. Known server mappings may
explicitly select `PropPatchOptions.AllowPathPlusEncodingAlias` for path-only
response-target compatibility; queries, other reserved escapes, outbound URLs,
origin checks and mutation replay behavior are unchanged.

Implementation update (v3 source): `DavClient` now provides the immutable
resource-oriented surface, shared bounded XML reading, rich outcomes, typed
properties/conditions, ordered PROPPATCH, metadata-rich transfers, lock options,
and streamed listing completion. Existing `Client`/`IClient` remain compatibility
APIs. A public REPORT seam and a single-batch sync-level 1 profile exercise the
extension boundary. See the [v3 migration reference](../docs/reference.md#v3-migration-and-contracts)
for current contracts.
This is not completion of P-01 through P-10: independent server conformance
evidence, DI/resilience integration, and the broader extension portfolio remain
outstanding. The ledger
and explicit managed lifecycle above are implemented, not automatic scheduling
or durable leases. No package publication or full RFC conformance is implied.

### Release-hardening smoke evidence (2026-10-20)

Both .NET 8 and .NET 10 sample paths were exercised successfully against an
independent WsgiDAV 4.3.5 / Cheroot 11.1.2 server on Windows, using anonymous
HTTP restricted to loopback and an initially empty temporary filesystem share:

| API | Observed successful operations |
| --- | --- |
| Modern `DavClient` (`--modern --mutate`) | PROPFIND, MKCOL, conditional full PUT, GET with byte-for-byte verification, DELETE |
| Legacy `Client` (`--mutate`, without `--partial-put`) | Root discovery and listing, OPTIONS, MKCOL, full PUT, LOCK, ranged GET with byte verification, UNLOCK, DELETE |

All four runs exited successfully and the share was empty afterward. Partial PUT
was intentionally not required. The runnable sample and its bounded cleanup are
covered by automated loopback tests, including both API paths and authentication
modes; the independent server run is a separate manual smoke check.

This is limited interoperability evidence, not a conformance matrix. It does not
establish independent-server authentication/TLS, modern locking/PROPPATCH/REPORT,
other server implementations, or production-scale performance. Those remain part
of the broader protocol plan. A green GitHub/Linux candidate run is still a
separate release gate; local Windows results do not substitute for it.

Related: [server plan](SERVER-SUPPORT-PLAN.md) and [package plan](SUBPACKAGE-AND-DESKTOP-PLAN.md).

## 1. Scope and definition of done

Deliver a client capable of expressing and interpreting the complete RFC 4918 authoring model, not merely sending its method names. Treat current HTTP semantics in RFC 9110 as the transport baseline, and RFC 2518 as an obsolete interoperability reference.

RFC 4918 classes describe server resources. The client should discover and interoperate with class 1 resources, class 2 locking resources, and class 3 resources implementing the RFC 4918 revisions. A server may advertise `1, 3` without locking, or `1, 2, 3` with locking. Extension support is separate.

Core completion requires:

- A traceable requirement ledger for RFC 4918 sections 4-18, including request/response XML, live/dead properties, locking, pre/postconditions, errors, and compliance advertisement interpretation.
- Complete per-resource and per-property outcomes, including successful outer HTTP responses containing failed members.
- Safe optimistic concurrency and lock-token submission for every applicable operation.
- Explicit standard-versus-extension discovery and no unsafe fallback on unsupported functionality.
- A versioned test corpus and interoperability results against multiple independent servers.
- Backward-compatible legacy behavior where possible, with documented and tested fixes where legacy behavior was incorrect.

The target is protocol capability and evidence. It does not imply that every server must implement optional locks, shared locks, search, versioning, calendars, or a particular storage model.

## 2. Reviewed baseline implementation (historical)

| Area | Present in source | Completion work |
| --- | --- | --- |
| Discovery | `GetServerOptions`; `ServerOptions` parses DAV/Allow. | Preserve unknown tokens; add extension metadata such as DASL and supported reports; distinguish observed capability from a guarantee of success. |
| PROPFIND | `allprop`, named `prop`, `propname`; depth; typed `Item`; targeted property bags. | `allprop` + `include`; lossless XML property values; exact statuses and resource errors across every mode. |
| PROPPATCH | Set/remove one text-valued custom property; checks returned propstat codes. | Ordered multi-property atomic request; XML values; lock conditions; property identity in results; permit writable DAV properties rather than rejecting the entire namespace. |
| Hierarchy | MKCOL, DELETE, COPY/MOVE, overwrite, source/destination lock tokens. | Recursive multistatus, request depth options, conditions on parent bindings/descendants, exact body rules and failure mappings. |
| Transfers | GET/PUT; byte-range download; partial PUT entry point. | HEAD, conditional and metadata-rich results, verified range responses, explicit partial-PUT compatibility and resumability contracts. |
| Locking | Exclusive write locks, fixed file/folder depth choices, refresh, unlock, one parsed active lock. | Scope/depth negotiation, multiple active locks, supportedlock, discovery, owner XML, timeout preferences, multi-resource conditions and lock lifecycle behavior. |
| Errors | `WebDAVException`, `WebDAVConflictException`, integer HTTP status. | Structured DAV:error conditions, multistatus, contextual metadata, consistent 409 mapping and malformed-response errors. |
| Transport/auth | Reusable HTTP wrapper, caller ownership, bearer handler, `ICredentials`, TLS callback, origin validation. | Request-scoped options, replay policy, credential isolation, cancellation through initial path resolution, diagnostics and DI adapters. |
| Automated tests | MSTest suite in solution for both target frameworks. | Requirement coverage, real-server integration, failure injection, native-client traces, large-resource fixtures. |

Evidence locations: `src\WebDAVClient\IClient.cs`; `Client.cs:254-1390`; `Model\Item.cs`; `Model\ServerOptions.cs`; helpers under `src\WebDAVClient\Helpers`; and `tests\WebDAVClient.UnitTests`.

Specific design constraints visible in current code:

- `ResponseParser.DispatchPropStat` buckets non-200 targeted properties together as not-found; the default allprop path does not expose per-property status. Successful parsing alone cannot establish property success.
- Targeted property values originate from `ReadInnerXml`, while `Item.FoundProperties` documents text values. The new contract must distinguish XML from text instead of relying on this ambiguity.
- `PropPatchRequestBuilder.ValidatePropertyNamespace` rejects every `DAV:` property. RFC 4918 does not make every live property protected: `DAV:displayname` SHOULD NOT be protected.
- `LockTokenHeaderHelper` supports common source/destination token patterns, not the full `If` condition grammar.
- DELETE/COPY/MOVE do not return recursive per-resource results. Rejecting a 207 generically is not the same as explaining a partially completed namespace operation.
- `UploadPartial` uses an exclusive `endBytes` argument and emits an inclusive wire endpoint of `endBytes - 1`. Preserve that legacy contract; do not silently reinterpret it.
- Download currently returns only the content stream. Its response lifetime and stream ownership need explicit tests before introducing a metadata-rich download result.
- URL resolution may perform a preliminary PROPFIND and caches an encoded base path. Advanced calls must preserve origin validation and define cancellation/cache invalidation when configuration changes.

These are planning inputs, not claims that this documentation fixes them.

## 3. Result and request model

Introduce an advanced surface without replacing the simple methods immediately. Names below are illustrative design concepts, not published types.

| Model | Required information |
| --- | --- |
| `DavResponse` | Outer status, selected headers, effective URI, bounded diagnostics, DAV pre/postcondition elements. |
| `DavMultiStatus` | Ordered resource responses; each href, response-level status or propstats, location and descriptions where applicable. |
| `DavPropertyResult` | Qualified name, exact status, XML value, description/error elements; missing, forbidden, failed dependency and malformed are distinct. |
| `DavPropertyValue` | Namespace-aware XML fragment plus explicit text access; preserve mixed content, attributes, language, empty values and nested namespaces. |
| `DavRequestConditions` | HTTP validators plus structured DAV `If` conditions; tagged resource URIs, OR-lists, AND-conditions, negation, ETags and state tokens. |
| `DavTransferResult` | Response-owning stream, ETag, content range/length/type, last-modified and effective status; deterministic disposal. |
| `DavCapabilities` | DAV tokens, Allow, DASL grammars, supported reports, supported lock entries and property availability at a given resource. |
| `DavLock` | Opaque token, scope/type, depth, owner XML, lock root, granted timeout and observation time. |

Keep `PropertyName` as the qualified-name primitive where possible. Share XML/status/condition representations between client and future server; do not force server storage contracts or ASP.NET types into client models.

Legacy methods can project rich results to `Item`/`bool`/exceptions. They must not silently turn failed child operations into success. Decide through an API review whether partial failure becomes a richer exception or is available only through new advanced methods; document every behavior change.

Keep HTTP failures under `WebDAVException` and preserve `GetHttpCode()`. Preserve actual outer status separately from member/property statuses; a 207 outer status with a 403 propstat is not an HTTP 403 response. Keep 409 compatibility while avoiding a proliferation of exceptions for every extension.

## 4. Core work packages

### P-01: normative ledger and wire fixtures

Priority: P0. Dependencies: none.

Inventory normative requirements and applicability, linking each to code, an acceptance fixture, and any documented deviation. Separate client obligations from server obligations. Record current wire behavior before refactoring and API compatibility before adding overloads.

Include namespaces, UTF-8/UTF-16, empty/missing bodies, unknown XML elements, response/property status ordering, absolute/path hrefs, and all status classes. No requirement is "covered" solely because a method exists.

Acceptance: every applicable MUST has a positive and negative test or a justified non-applicability entry; SHOULD deviations have rationale; protocol examples are original fixtures rather than copied vendor code.

### P-02: lossless XML, multistatus and errors

Priority: P0. Dependencies: P-01.

Unify hardened XML reading across PROPFIND, PROPPATCH, LOCK, REPORT and DAV:error while retaining structural/namespace validation. Preserve response-level and property-level status, resource identity and mixed-content values. Unknown extensions must survive without being interpreted as structural DAV elements.

Support 207 from collection operations and distinguish partial completion from atomic operations. Validate required structure; bounded response sizes, XML depth, property sizes and error snippets must prevent unbounded consumption. Keep DTDs/external resolution prohibited.

Acceptance:

- 200/403/404/409/423/424/507 propstats remain distinguishable regardless of their XML ordering.
- A nested property href/status/response cannot become a resource identity or result.
- A 207 DELETE with one denied child identifies that child and never reports unconditional success.
- Malformed, empty and wrong-root XML produce actionable errors rather than empty successful result sets.
- XML fragments round-trip with namespaces, attributes and language intact; default legacy property bags retain documented compatibility.

### P-03: conditional requests and safe namespace operations

Priority: P0. Dependencies: P-01/P-02.

Add request-scoped HTTP validators (`If-Match`, `If-None-Match`, date validators where relevant, `If-Range`) and the full DAV `If` grammar. A mutable global `CustomHeaders` collection is not an adequate per-operation concurrency API.

Use method-specific options objects on the advanced surface for conditions, headers, depth and other operation settings rather than continually extending positional argument lists. Snapshot mutable options/collections when constructing a request so concurrent callers cannot change an in-flight operation. Retain trailing cancellation tokens and existing legacy signatures; do not move cancellation exclusively into an options object.

Cover multiple affected resource URIs and lock tokens for parents, locked descendants and destination overwrites. Apply HTTP and DAV conditions according to their distinct semantics; do not reduce tagged lists to "all resources are locked." Unit-test the formal grammar and real request evaluation.

Expose supported depth choices where meaningful: PROPFIND 0/1/infinity; collection COPY 0/infinity; MOVE and collection DELETE semantics mandated by RFC 4918. Validate unsupported depth values before I/O. Check unnecessary COPY/MOVE bodies against the standard and independently hosted servers.

Acceptance:

- Stale ETag update fails without overwriting the server resource; create-if-absent is race-safe.
- Multiple descendant lock tokens and destination conditions can be represented without constructing raw header strings.
- `Overwrite: F`, self-copy/self-move, recursive conflicts and 207 member outcomes have fixtures.
- Cross-origin Destination/If tagged resource identifiers cannot bypass the established origin policy.

### P-04: complete property operations

Priority: P0. Dependencies: P-02/P-03.

Implement ordered sets/removes in a single PROPPATCH document. Atomicity is a server obligation: the client submits one request and interprets all outcomes; it must not fake a batch by looping existing single-property methods.

Provide PROPFIND `allprop` with `include`, exact property-name discovery, and typed access to all core DAV properties: creationdate, displayname, getcontentlanguage, getcontentlength, getcontenttype, getetag, getlastmodified, lockdiscovery, resourcetype and supportedlock.

Not every live property is returned by allprop and not every live property is writable. Model server protection/error responses explicitly. Allow `DAV:displayname` through the advanced API; retain the narrower legacy dead-property helper if needed for compatibility.

Acceptance:

- Ordered set/remove combinations are preserved; partial property failure exposes each property's status and 424 dependencies.
- Repeated changes to the same qualified property preserve caller order, including remove-then-set and set-then-remove; separate dictionaries of sets/removes cannot express the complete sequence.
- Text/XML values, namespace collisions, empty values, `xml:lang`, protected properties and authorization failures are distinct.
- Lock-token conditions apply to PROPPATCH and to MKCOL in a locked parent, not only to PUT/DELETE/COPY/MOVE.
- Discovery never treats 403 as "property does not exist."

### P-05: complete lock lifecycle

Priority: P0. Dependencies: P-02/P-03/P-04.

Expose exclusive/shared scope and permitted depth, supported-lock discovery, every active lock, owner XML and timeout preferences including Infinite. Support servers choosing a different timeout than requested; do not invent an expiry from the requested value when the granted value is absent.

Exercise creation of a locked empty resource at an unmapped URL (201), refresh with no body, expired/foreign tokens, collection locks and failed depth-infinity requests. Shared locks are optional for a server, but a general-purpose client must be able to use them when advertised.

Offer an optional lock lease helper with cancellation, bounded renewal, explicit lost-lock notification and best-effort release. Losing a lease stops protected writeback; it must never silently acquire a new lock and overwrite unseen changes.

Acceptance:

- Both exclusive-only and shared-capable servers are covered without assuming either policy.
- Supportedlock and lockdiscovery are parsed as structured properties, not opaque display strings.
- Multiple active locks retain their individual scope, depth, owner, root, token and timeout rather than collapsing into the first entry.
- A failed recursive LOCK cannot be treated as a granted tree-wide lock.
- Refresh/unlock preserve token opacity, verify outcomes, and report expiry or missing information explicitly.

### P-06: HTTP transfers and resource metadata

Priority: P0. Dependencies: P-02/P-03.

Add HEAD and conditional metadata-rich GET/PUT results. Handle 304 as a conditional outcome, not a normal content download. Define GET range policy for 206, ignored-range 200, unsatisfiable 416, zero-length files, unknown length, suffix/open-ended ranges and representations changed between chunks.

Do not append a 200 full response to a partially downloaded file. Validate Content-Range and stable validators before accepting resumed bytes. Document automatic decompression versus range semantics and request identity encoding when required.

Partial PUT is an explicit compatibility feature, not a requirement of RFC 4918. RFC 9110 section 14.5 describes private agreement and warns that a server may interpret the body as a full replacement. Capability/configuration must opt in before a new high-level resume workflow uses it.

Acceptance:

- Existing `UploadPartial` exclusive-end behavior remains covered; new APIs use unambiguous offset/count/total-length names.
- 64-bit offsets and logical files larger than 4 GiB work without buffering entire content.
- Caller-owned versus operation-owned upload streams have an explicit contract; non-seekable streams are supported for normal uploads.
- Disposing a transfer result closes the response; cancellation and failed downloads release resources.
- Transfer results expose relevant status, ETag, Content-Range and content metadata while preserving caller ownership of an externally supplied HttpClient/wrapper.
- A resumed download detects an ETag change and cannot concatenate incompatible versions.

### P-07: capability-aware operations and origin/path rules

Priority: P0. Dependencies: P-02/P-04/P-05/P-06.

Retain unfamiliar DAV extension tokens and inspect per-resource advertised features. Distinguish unsupported, forbidden, transient failure and unknown capability. A missing header does not prove a server can never support a method, and an advertised token does not prove conformance.

Validate URI identity for escaped delimiters, literal percent, plus, spaces, Unicode, different origins/ports and path-segment boundaries. Preserve opaque server hrefs for requests, use separate display decoding, and never change URI path identity using culture-aware or blanket case-insensitive comparisons.

Keep cached base-path resolution consistent with configuration changes and caller cancellation. Define redirect behavior per method and credential origin, especially body replay and HTTPS downgrade refusal.

Acceptance: origin regression tests remain green; property hrefs cannot redirect subsequent I/O; case-sensitive servers can distinguish `A.txt` and `a.txt`; cancelled first calls do not leave poisoned resolver state; read-only resources still work without speculative mutations.

### P-08: .NET integration and transport reliability

Priority: P1. Dependencies: P-03/P-06/P-07.

Implement the roadmap's builder, factory/DI, credential adapters, logging and resilience using existing .NET abstractions. Keep Azure Identity and hosting dependencies optional. Verify actual credential types before committing public signatures.

Retries require a fresh request, a replayable body and an operation-specific policy. Never indiscriminately retry a LOCK, ambiguous MOVE, or conditional PUT after a lost response. Respect Retry-After, cancellation and bounded backoff; surface ambiguous outcomes for reconciliation.

Use structured, redacted diagnostics with correlation and duration. Never log bearer tokens, cookies, lock tokens, user content or raw sensitive query strings by default.

Acceptance: cancellation covers initialization and I/O; replay tests exercise seekable/non-seekable bodies; concurrent callers do not leak headers; supplied clients/wrappers retain ownership; logging and retries are opt-in with safe defaults.

### P-09: performance and large listings

Priority: P1. Dependencies: P-02/P-06/P-08.

Add streaming enumeration only after defining how resource-level errors and response lifetime are surfaced. `IAsyncEnumerable` does not imply server-side pagination or a stable snapshot.

Suggested acceptance budgets, to calibrate on a named CI runner: bounded transfer buffers no larger than 8 MiB per active transfer excluding HttpClient/platform buffers; a streamed 100,000-item PROPFIND should not retain the entire response; cancellation observed within two seconds in local deterministic fixtures. Record runtime, fixture size, allocation and elapsed-time baselines before enforcing thresholds.

Retain a compatibility materialization path for legacy `IEnumerable<Item>` callers.

### P-10: conformance evidence and public claims

Priority: P0 release gate. Dependencies: P-01 through P-09.

Run unit and wire-contract tests on both supported target frameworks. Add isolated integration fixtures for at least two independent core servers (for example Apache mod_dav and Nextcloud) plus this project's future server. Test extensions against implementations that actually advertise them; core-server success is not evidence for DeltaV or DASL.

Publish a matrix with software versions, auth configuration, operating systems, supported features and known deviations. Native-client evidence from S-04 is complementary; do not require installed Office in ordinary unit-test CI.

Release gate: every applicable core MUST passes; no untriaged false-success or lost-update case; deviations are explicit; public API compatibility is reviewed; migration docs and README claims match tested capabilities.

## 5. Standard extension portfolio

Extensions are capability-gated. They must reuse the advanced property, condition, error and REPORT models rather than grow independent parsers.

| ID | Standard and scope | Client/server work | Dependency and acceptance |
| --- | --- | --- | --- |
| E-01 | RFC 4331 quota | Typed available/used bytes, unavailable/unknown states; server quota reservation and precise 507 conditions. | P-04; concurrent uploads cannot exceed enforced quota; a missing quota property is not zero free space. |
| E-02 | RFC 5323 SEARCH/DASL | Grammar discovery, `DAV:basicsearch`, typed query AST, scopes, predicates, property selection, ordering and limits. Server maps only supported grammar to authorized searches. | P-02/P-04/P-07; test scope, wildcard escaping, unsupported grammar and mixed property results. Do not expose raw SQL as a search API. |
| E-03 | RFC 6578 collection synchronization | supported-report-set, sync-token and sync-collection REPORT, initial/incremental sync, deletions, limits and invalid-token recovery. | P-02/P-04/P-07; S-05 for hosted server; token commit only after durable application of all returned changes. |
| E-04 | RFC 3744 ACL plus RFC 5397 current-user-principal | Principal discovery, privileges, protected/inherited ACEs, ACL methods/reports, current-user privileges and restrictions. | P-02/P-03/P-04; negative authorization tests; ACL is not merely a string-valued dead property. |
| E-05 | RFC 3253 DeltaV | Start with an explicitly named core-versioning profile: VERSION-CONTROL, CHECKOUT, CHECKIN, UNCHECKOUT, properties and REPORTs. Add advertised version-history/update/label packages deliberately. | P-02 through P-05; immutable version identity/content, checkout rollback and history tests. Core-versioning alone is not full DeltaV. |
| E-06 | RFC 4791 CalDAV, RFC 6352 CardDAV, RFC 6764 discovery | Separate client/server profiles, home sets, principal discovery, REPORT filters, calendar/contact media types, sync and domain parsers. Scheduling RFC 6638 is a further profile. | E-03/E-04 and server maturity; recurrence/timezone/vCard fidelity and domain interoperability suites required. |
| E-07 | RFC 5689 extended MKCOL; RFC 5995 add-member POST; RFC 8144 Prefer handling | Extended collection creation, server-assigned resource creation, and minimal/representation response handling. | P-02/P-03/P-04/P-07; feature negotiation, returned locations and property-error bodies exercised. |
| E-08 | RFC 5842 bindings, RFC 3648 ordered collections, RFC 4437 redirect references | BIND/UNBIND/REBIND with resource identity/cycle handling; ORDERPATCH; MKREDIRECTREF/UPDATEREDIRECTREF and reference-aware requests. | Explicit later opt-in profiles; graph/cycle and authorization design before implementation. Do not silently model bindings as ordinary copied files. |

For E-05, separately track working-resource, workspace, version-controlled-collection, baseline, activity and merge packages; deliver only with an explicit requirement and full package tests. For E-06, separately evaluate calendar availability, timezone distribution and managed attachments when requested. They are not part of the initial completeness claim.

For all later profiles, publish an unsupported-feature entry and detection guidance until implemented. A generic REPORT method or XML escape hatch is useful extensibility, not a claim that every extension is supported.

### Synchronization must distinguish standard and vendor behavior

RFC 6578 provides collection change tokens and deletion reporting. IT Hit additionally documents stable resource/parent IDs, lookup by ID and a Sync-ID workflow used by its native clients. Do not assume `parent-resource-id`, global recursive tokens or a particular ID URL shape are universally standardized by RFC 6578.

A sync implementation must define collection scope, sync-level support, truncation/continuation, token invalidation, retention and access filtering. Generic servers without stable IDs can support path-based reconciliation with limitations; rename detection cannot be guaranteed from ETags alone.

### Vendor compatibility profiles

The site explicitly documents these as specialized behavior:

| Feature | Treatment |
| --- | --- |
| IT Hit paging and sorting, advertised through `paging` | Optional adapter; do not assume ordinary PROPFIND supports server paging. |
| `resumable-upload`, proprietary upload-progress REPORT and CANCELUPLOAD | Optional compatibility adapter with recorded wire fixtures; not part of RFC 4918 completion. |
| Content-Range PUT | Explicit agreement with the server, integrity and offset checks; never inferred from GET range support. |
| WebSocket notifications / folder invalidation | Hints or vendor-specific events; synchronization still reconciles authoritative state. |
| Office/MS-OFBA and other Microsoft-specific behavior | Compatibility profiles with current platform tests, not mandatory core semantics. |

## 6. Acceptance matrix

| Suite | Minimum cases | Execution |
| --- | --- | --- |
| XML/status | Namespace and structural spoofing, mixed propstats, response errors, XML fragments, malformed/oversized bodies. | Deterministic fixtures on both TFMs. |
| Property | allprop/include/propname/prop; ordered atomic patch; protected/writable live properties; locks. | Fixtures plus real-server runs. |
| Namespace | MKCOL, DELETE, COPY/MOVE depths, overwrite, parent/descendant locks, partial failures. | Local isolated trees; concurrent mutation tests. |
| Lock | Shared/exclusive discovery, refresh, expiry, unmapped URL, multiple locks and condition lists. | Deterministic time and real lock server. |
| Transfer | 0-byte and >4-GiB logical data, non-seekable streams, 200/206/304/416, changed ETag, interrupted PUT. | Bounded-memory streams and faulting transport. |
| Auth/origin | Existing auth constructors, refresh race, redirects, TLS, credentials per origin, cache changes. | Stub transport plus isolated auth servers. |
| Extensions | Negotiation and negative cases for each claimed RFC/package. | Version-pinned specialized servers. |
| Performance | Large listings/transfers, concurrent operations, cancellation and resource release. | Named runner and published measurements. |

Existing targeted commands remain:

```powershell
dotnet test .\tests\WebDAVClient.UnitTests\WebDAVClient.UnitTests.csproj -c Release -f net8.0
dotnet test .\tests\WebDAVClient.UnitTests\WebDAVClient.UnitTests.csproj -c Release -f net10.0
```

Add any new integration-test projects to the solution before relying on solution-level test runs. Keep networked suites explicitly selected; public demonstration endpoints are not stable CI fixtures and must not receive destructive tests.

## 7. Release sequencing

Use capability gates, not dates inherited from the old roadmap:

| Release increment | Work | Exit evidence |
| --- | --- | --- |
| Core result foundation | P-01/P-02 | Requirements ledger and faithful result/error fixtures. |
| Authoring correctness | P-03/P-04/P-05 | Safe concurrent writes, complete property operations and lock lifecycle. |
| Transfer and operational reliability | P-06/P-07/P-08/P-09 | Verified ranges, cancellation, ownership and bounded resource use. |
| Core support claim | P-10 | Published interoperability and compliance matrix. |
| Practical extensions | E-01/E-02/E-03 | Quota/search/sync implementations with independent negotiation. |
| Specialized profiles | E-04 through E-08 | Explicitly selected profiles, platform/backend support and compatibility notes. |

Update release notes/version fields together only when implementing a user-visible release. This plan itself needs no package version bump.

## 8. Research anchors

Normative requirements come from [RFC 4918](https://www.rfc-editor.org/rfc/rfc4918.html) and [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html). Consult the individual RFCs named in the extension portfolio for their separate requirements; vendor documentation does not redefine those standards.

The site's [property management](https://www.webdavsystem.com/client/programming/managing_properties/), [locking](https://www.webdavsystem.com/client/programming/locking/), [DeltaV](https://www.webdavsystem.com/client/programming/deltav/), [search](https://www.webdavsystem.com/client/programming/search/) and [synchronization](https://www.webdavsystem.com/client/programming/sync/) guides supply comparison scenarios, not a requirement to copy their API shapes.

The [paging guide](https://www.webdavsystem.com/server/documentation/paging/) and [resumable-upload wire guide](https://www.webdavsystem.com/javaserver/doc/resumable_upload/how_it_works/) explicitly identify vendor extensions. Keep these separate from the core compliance claim.

## 9. Lessons from skazantsev/WebDavClient

Reviewed 2026-10-19 at commit [`acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2`](https://github.com/skazantsev/WebDavClient/tree/acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2), dated 2025-03-30. This is a source-level comparison of a client library, not runtime interoperability evidence, an RFC conformance certification or a recommendation to replace this project's architecture.

### Adopt the ideas, not the entire implementation

| Upstream reference | Recommendation for this project | Work package |
| --- | --- | --- |
| [LockParameters](https://github.com/skazantsev/WebDavClient/blob/acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2/src/WebDav.Client/Request/LockParameters.cs) groups request headers, depth, scope, timeout and cancellation. | Add method-specific advanced options without growing positional overloads or mutating global headers. Preserve our trailing cancellation-token convention and snapshot request state. | P-03/P-08. |
| [WebDavPropertyStatus](https://github.com/skazantsev/WebDavClient/blob/acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2/src/WebDav.Client/Core/WebDavPropertyStatus.cs) retains qualified name, status and description. | Keep exact per-property outcomes and outer status separately; expose failures without losing property identity. | P-02. |
| [ProppatchRequestBuilder](https://github.com/skazantsev/WebDavClient/blob/acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2/src/WebDav.Client/Request/ProppatchRequestBuilder.cs) submits multiple XML-valued sets/removes together. | Submit one atomic server request, but use an ordered instruction list rather than grouping every set before every removal. Model text and XML explicitly. | P-04. |
| [LockResponseParser](https://github.com/skazantsev/WebDavClient/blob/acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2/src/WebDav.Client/Response/LockResponseParser.cs) returns a collection of active locks; acquisition supports shared/exclusive scope. | Use multiple-lock discovery and scope selection as concrete scenarios; retain our stronger parsing and full lifecycle requirements. | P-05. |
| [WebDavStreamResponse](https://github.com/skazantsev/WebDavClient/blob/acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2/src/WebDav.Client/Response/WebDavStreamResponse.cs) owns the stream and HTTP response. | Provide an explicitly disposable transfer result, adding validators, range/content metadata and tested ownership semantics. | P-06. |
| [PROPPATCH method tests](https://github.com/skazantsev/WebDavClient/blob/acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2/src/WebDav.Client.Tests/Methods/ProppatchTests.cs) check request XML, headers and cancellation independently of parsers. | Extend our existing MSTest wire-contract checklist and separate request construction from response parsing tests. Do not migrate test frameworks or inherit incorrect expected behavior. | P-01/P-10 and WebDAV.Testing. |
| [SearchRequestBuilder](https://github.com/skazantsev/WebDavClient/blob/acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2/src/WebDav.Client/Request/SearchRequestBuilder.cs) builds projection, scope and a LIKE predicate. | Use as a small SEARCH example, not the complete design: E-02 still needs grammar discovery, a typed query model, ordering, limits and unsupported-feature behavior. | E-02. |

### Counterexamples and required regression cases

- **Malformed XML must not be a successful empty listing.** Upstream [parser tests](https://github.com/skazantsev/WebDavClient/blob/acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2/src/WebDav.Client.Tests/Response/PropfindResponseParserTests.cs#L31-L44) explicitly accept that outcome for a 207 response. Our P-02 behavior must fail clearly, and D-01 must never reconcile such a response as remote deletion.
- **HTTP 2xx is not aggregate DAV success.** Upstream [WebDavResponse](https://github.com/skazantsev/WebDavClient/blob/acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2/src/WebDav.Client/Response/WebDavResponse.cs) uses only the outer status, and ProppatchResponse does not override that calculation. Test 207 with denied/failed-dependent properties and failed child operations; preserve both transport and operation outcomes.
- **XML local names alone are not structural identity.** Upstream [XML helpers](https://github.com/skazantsev/WebDavClient/blob/acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2/src/WebDav.Client/Helpers/LinqToXmlExtentions.cs) match local names, sometimes case-insensitively. Retain namespace-aware, case-correct and structurally bounded DAV parsing; test foreign namespaces and nested lookalike elements.
- **Supplying HttpClient does not transfer ownership by default here.** Upstream [dispatcher disposal](https://github.com/skazantsev/WebDavClient/blob/acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2/src/WebDav.Client/Core/WebDavDispatcher.cs) disposes its supplied client. Preserve this repository's caller-owned wrapper/client contract and test usability after disposing the DAV client/result.
- **Convenience helpers are not full protocol coverage.** Upstream [IfHeaderHelper](https://github.com/skazantsev/WebDavClient/blob/acad5a8f7d3f08cec058404b1e3f9a9806e1b6b2/src/WebDav.Client/Helpers/IfHeaderHelper.cs) expresses one untagged token list. P-03 still requires tagged multi-resource conditions, ETags, negation and alternative lists; a simple SEARCH builder likewise does not establish full DASL support.

These findings reinforce P-02 through P-06 and supply additional examples for E-02 and the testing package. They do not change the server, durable-sync or native-platform architecture. No upstream dependency or source code is introduced; any future reuse must separately review and preserve the applicable MIT notices.
