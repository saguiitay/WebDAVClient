# Protocol and extension support

These tables describe the operations and extensions available through
`DavClient` in the v3 package. Your server must also support the features you use;
an implemented client operation does not guarantee support on every server or
full RFC conformance. See [server compatibility](compatibility.md) for endpoint
and setup guidance. Differences in the legacy `Client` API are called out below.

## Core HTTP / WebDAV

| Feature | Client status | API / important boundary |
|---|---|---|
| OPTIONS and DAV/Allow discovery | Implemented | `GetCapabilitiesAsync`; resource-scoped advertisements, missing values remain unknown |
| HEAD metadata | Implemented | `HeadAsync`; check outer status |
| GET / full downloads | Implemented | `DownloadAsync`; response owner, 304 has no content |
| Single byte-range GET | Implemented | One validated bytes range, optional strong/date If-Range; no automatic append/resume |
| Durable resumable file downloads | Implemented (3.1+) | `DavFileTransfers.DownloadFileAsync`; strong ETag, known length, verified checkpoints and atomic no-overwrite publication; [guide](resumable-downloads.md) |
| Full PUT | Implemented | `UploadAsync`; streaming, caller-owned input by default |
| MKCOL | Implemented | `CreateCollectionAsync`; bodyless request, no recursive parent creation |
| DELETE | Implemented | `DeleteAsync`; collection infinity semantics and member outcomes |
| COPY / MOVE | Implemented | `CopyAsync` / `MoveAsync`; no overwrite by default; COPY depth zero/infinity |
| PROPFIND allprop / prop / propname / include | Implemented | `PropFindAsync` and `OpenListingAsync`; depth zero/one/infinity, subject to server rejection |
| Ordered atomic PROPPATCH request | Implemented | `UpdatePropertiesAsync`; caller inspects property-level outcomes; atomicity is a server obligation |
| Exclusive/shared write LOCK | Implemented | `AcquireLockAsync`; scope/depth validation, all observed locks retained |
| Lock refresh / UNLOCK | Implemented | Explicit `RefreshLockAsync` / `UnlockAsync`; no background lease renewal |
| Explicit lock lease lifecycle | Implemented (3.2+) | `DavLockLease`; observed expiry, caller-driven renewal and one release attempt; [guide](lock-lifecycle.md) |
| HTTP ETag/date conditions | Implemented | Typed request options; no implicit lost-update strategy |
| DAV `If` conditions | Implemented | Tagged/untagged lists, state tokens, ETags and `Not`; same-origin tagged resources. Upcoming 3.3 adds explicit `TaggedAbsolutePaths`; existing `Tagged` defaults stay unchanged |
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
| Durable local directory mirroring | Not implemented | Application-owned; not implied by file checkpoints or one sync REPORT |
| Automatic retries / background lock renewal | Not implemented | Reconciliation and renewal scheduling remain caller-owned |

Unknown XML and custom properties can be retained without implementing the
extension that defines them. Parsing a name is not implementation of its
authorization, error, continuation or mutation semantics.

## Response validation (3.2+)

Core multistatus responses and streamed listings enforce structural href format,
origin, uniqueness and known XML cardinalities. A malformed response fails
explicitly and cannot certify a complete listing. Generic REPORT profiles and
legacy projection retain their separate validation boundaries.

Successful LOCK acquisition requires one confirmed token header and matching
lockdiscovery evidence. Refresh requires HTTP 200 and lockdiscovery XML, even
though its request is bodyless. Full-resource GET accepts 204 as an empty stream;
range requests still require valid partial-content confirmation.

For detailed implementation/test mappings and remaining server obligations, see
the [RFC 4918 requirement ledger](rfc4918-requirements.md). It is not a server
compatibility certification.

## API reference

The public implementations are in [DavClient.cs](../src/WebDAVClient/DavClient.cs),
[options](../src/WebDAVClient/DavOptions.cs), [results](../src/WebDAVClient/DavResults.cs),
[protocol models](../src/WebDAVClient/Protocol), and
[CollectionSync.cs](../src/WebDAVClient/Extensions/CollectionSync.cs).
