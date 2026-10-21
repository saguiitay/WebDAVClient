# Protocol and extension support

**Scope:** `DavClient` in the current v3 package. "Implemented" means a public
client operation with automated contract coverage, **not RFC certification or
verified behavior on every server**. See [server compatibility](compatibility.md)
for independent-server evidence. Retained `Client` differences are called out.

## Core HTTP / WebDAV

| Feature | Client status | API / important boundary |
|---|---|---|
| OPTIONS and DAV/Allow discovery | Implemented | `GetCapabilitiesAsync`; resource-scoped advertisements, missing values remain unknown |
| HEAD metadata | Implemented | `HeadAsync`; check outer status |
| GET / full downloads | Implemented | `DownloadAsync`; response owner, 304 has no content |
| Single byte-range GET | Implemented | One validated bytes range, optional strong/date If-Range; no automatic append/resume |
| Full PUT | Implemented | `UploadAsync`; streaming, caller-owned input by default |
| MKCOL | Implemented | `CreateCollectionAsync`; bodyless request, no recursive parent creation |
| DELETE | Implemented | `DeleteAsync`; collection infinity semantics and member outcomes |
| COPY / MOVE | Implemented | `CopyAsync` / `MoveAsync`; no overwrite by default; COPY depth zero/infinity |
| PROPFIND allprop / prop / propname / include | Implemented | `PropFindAsync` and `OpenListingAsync`; depth zero/one/infinity, subject to server rejection |
| Ordered atomic PROPPATCH request | Implemented | `UpdatePropertiesAsync`; caller inspects property-level outcomes; atomicity is a server obligation |
| Exclusive/shared write LOCK | Implemented | `AcquireLockAsync`; scope/depth validation, all observed locks retained |
| Lock refresh / UNLOCK | Implemented | Explicit `RefreshLockAsync` / `UnlockAsync`; no background lease renewal |
| HTTP ETag/date conditions | Implemented | Typed request options; no implicit lost-update strategy |
| DAV `If` conditions | Implemented | Tagged/untagged lists, state tokens, ETags and `Not`; same-origin tagged resources |
| Qualified XML values and 207 outcomes | Implemented | Resource/property outcomes retained separately; bounded asynchronous parser |
| RFC 4918 class 1/2/3 certification | Not claimed | Implementing these methods or parsing DAV tokens is not a complete conformance audit |

The modern surface has no partial PUT. Legacy `UploadPartial` is a nonstandard,
server-specific agreement, not a standard resumable-upload feature. Basic,
Digest and Windows authentication depend on `HttpClientHandler` and the server;
bearer handlers are provided, but OAuth acquisition/refresh is application-owned.

## Extensions and higher-level features

| Standard / feature | Client status | Meaning |
|---|---|---|
| Generic XML REPORT execution | Implemented seam | `ReportAsync` returns XML and members; callers implement profile-specific semantics |
| RFC 6578 collection synchronization | Partial | One `sync-collection`, sync-level 1 batch; tokens/deletions/truncation exposed, no durable checkpointing or continuation loop |
| RFC 5323 SEARCH / DASL | Not implemented | Discovery may expose DASL strings; no SEARCH client/profile |
| RFC 3744 ACL | Not implemented | No ACL/privilege management API |
| RFC 3253 DeltaV/versioning | Not implemented | Generic REPORT does not implement version-control operations |
| RFC 4791 CalDAV | Not implemented | No calendar model, calendar-query client or complete CalDAV profile |
| RFC 6352 CardDAV | Not implemented | No address-book model/query client or complete CardDAV profile |
| RFC 5689 extended MKCOL | Not implemented | Current MKCOL does not accept a property-bearing body |
| RFC 5842 bindings | Not implemented | No BIND/UNBIND/REBIND methods |
| RFC 4437 redirect references | Not implemented | Ordinary HTTP redirects are not a redirect-reference profile |
| RFC 4331 quota properties | Raw property access only | Can request qualified properties; no typed quota profile or server-support guarantee |
| Vendor chunked/resumable uploads | Not implemented | Full PUT only on the modern API |
| Durable local mirroring / resumable file checkpoints | Not implemented | Application-owned; not implied by ranges or one sync REPORT |
| Automatic retries / managed lock leases | Not implemented | Explicit reconciliation and lifecycle remain caller-owned |

Unknown XML and custom properties can be retained without implementing the
extension that defines them. Parsing a name is not implementation of its
authorization, error, continuation or mutation semantics.

## How the table is grounded

The public implementations are in [DavClient.cs](../WebDAVClient/DavClient.cs),
[options](../WebDAVClient/DavOptions.cs), [results](../WebDAVClient/DavResults.cs),
[protocol models](../WebDAVClient/Protocol), and
[CollectionSync.cs](../WebDAVClient/Extensions/CollectionSync.cs).
Contract/wire fixtures are in [WebDAVClient.UnitTests](../WebDAVClient.UnitTests).
Update this table alongside actual shipped APIs/tests, not roadmap proposals.
