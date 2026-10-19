# Reliability review

Reviewed 2026-10-19 by a dedicated reliability agent using local source, tests,
workflows, and documentation. No critical-severity defect was established.

## Ranked findings

## Remediation progress

- Test infrastructure: Added a separate asynchronous, token-aware handler with
  concurrent immutable request snapshots and no eager body buffering, plus a
  prefix-then-stall stream that rejects synchronous network reads. The legacy
  synchronous fixture remains unchanged.

- REL-05: All operation-level HTTP failures now use the existing conflict-aware
  exception factory. A public-operation matrix covers 409 and non-conflict
  failures, including discovery.

- REL-02: MOVE/COPY now dispose responses and nonempty content on success and
  failure. Failed download/stream-acquisition cleanup was already fixed in
  `44769de`; existing download regressions retain that coverage.
- REL-03: Already fixed in `5cf9206` (BUG-08), with resource-level failures,
  mixed listings, and failed propstat regression coverage.

The original findings and evidence below describe the reviewed revision.

| ID | Severity | Finding | Primary evidence | Confidence |
|---|---|---|---|---|
| REL-01 | High | Metadata bodies can hang beyond cancellation/HTTP timeout | `WebDAVClient\Client.cs:365-367,873-875,1161` | 10/10 |
| REL-02 | High | MOVE/COPY and failed downloads abandon responses | `WebDAVClient\Client.cs:900-931,1087-1096` | 10/10 |
| REL-03 | High | PROPFIND represents resource failures as successful items | `WebDAVClient\Helpers\ResponseParser.cs:134-151,302-315` | 10/10 |
| REL-04 | Medium | Initial discovery ignores caller cancellation | `WebDAVClient\Client.cs:1230-1245` | 10/10 |
| REL-05 | Medium | HTTP 409 exception mapping is inconsistent | `WebDAVClient\Client.cs:529,844,906,928,1080-1085` | 10/10 |
| REL-06 | Medium | Manual publishing bypasses automated tests | `.github\workflows\manual.yml:26-33` | 10/10 |

## REL-01: Body reads escape cancellation and timeout

`Client.cs:1161` uses `ResponseHeadersRead`. List/Get then read streams without
the operation token and parse synchronously (`:365-367,873-875`;
`ResponseParser.cs:101-125`). LOCK/refresh/PROPPATCH have the same issue
(`Client.cs:780-782,965-967,1042-1046`).

A server can send headers promptly and stall mid-XML. `HttpClient.Timeout` no
longer covers body consumption, and synchronous reads do not observe the caller
token. Operations can remain blocked after cancellation, delaying shutdown and
occupying workers.

**Recommendation:** Cancellable asynchronous body reads/parsing with an explicit
body deadline; preserve `OperationCanceledException`. Merely passing a token to
`ReadAsStreamAsync` does not cancel later XML reads. Coordinate with PERF-02.

**Acceptance:** A stream supplies a prefix then blocks; cancellation and a
separate deadline terminate List/Get/LOCK/refresh/PROPPATCH promptly and dispose
responses.

## REL-02: Incomplete response ownership

MOVE (`Client.cs:900-909`) and COPY (`:922-931`) neither consume nor dispose
responses. Failed downloads and failed stream acquisition lack cleanup
(`:1091-1096`). The wrapper contract assigns disposal to the caller
(`HttpClient\IHttpClientWrapper.cs:20-24`).

Repeated nonempty unread bodies can retain transport resources and impair
connection reuse. Empty-body success responses need not exhibit this behavior.

**Recommendation:** `using` for MOVE/COPY; dispose downloads on every path before
successful stream ownership transfer. Do not dispose successful responses while
their returned stream is still in use. Coordinate with PERF-01.

**Acceptance:** Extend tracking-response tests (`ClientTests.cs:373-386`) for
success/error MOVE/COPY, failed downloads, and throwing stream factories; assert
content/response disposal using nonempty bodies.

## REL-03: Multistatus conceals resource failure

List/Get accept 207 (`Client.cs:359-367,867-875`). The parser gates status
handling behind nondefault modes (`ResponseParser.cs:134-151`), appends every
closed response (`:302-315`), and single retrieval selects the first (`:67-69`).

This valid body becomes an ordinary item rather than a failed lookup:

```xml
<D:response xmlns:D="DAV:">
  <D:href>/webdav/missing.txt</D:href>
  <D:status>HTTP/1.1 404 Not Found</D:status>
</D:response>
```

Default allprop also does not gate typed values on successful propstat status.
Synchronization consumers can continue with misleading/incomplete metadata.

**Recommendation:** Track resource-level and property-level status separately.
Individual retrieval should throw a status-bearing failure. Listings must expose
failures explicitly or fail rather than silently report complete success. Honor
successful propstat values in every mode.

**Acceptance:** Sole 404, mixed success/failure, successful and failed allprop
groups, and named-property resource-level failure fixtures.

## REL-04: Lazy discovery drops cancellation

Public methods call `GetServerUrl` before their token-bearing request
(`Client.cs:339-341`). That helper has no token (`:1230`) and calls discovery
without one (`:1243`). Tests acknowledge the gap (`ClientTests.cs:665-671`).

An already-cancelled first operation can still send discovery; cancellation while
discovery is pending cannot stop it.

**Recommendation:** Thread tokens through URL resolution and discovery, checking
before I/O. If sharing initialization, define per-caller cancellation ownership.

**Acceptance:** No send for an already-cancelled token; cancellation during
headers/body; subsequent uncancelled discovery retries successfully.

## REL-05: Conflict exception contract is inconsistent

`Helpers\WebDAVException.cs:6-10` documents the 409 specialization;
`Client.cs:1080-1085` implements a factory used by PROPPATCH. Other methods create
plain `WebDAVException` (`:529,572,844,906,928,959,1012`); CreateDir separately
maps conflicts (`:600-601`).

Consumers catching `WebDAVConflictException` for reconciliation miss PUT/COPY/MOVE
conflicts, though the numeric code remains available.

**Recommendation:** Use the existing status factory consistently.

**Acceptance:** Parameterized 409 tests across operations checking specialized
type and status; other statuses remain ordinary WebDAV exceptions.

## REL-06: Inconsistent publication gates

`.github\workflows\manual.yml:26-33` restores/builds/packs/pushes without tests.
`publish.yml:40-47` tests before packing. The test project is included and
configured (`WebDAVClient.sln:10-11,52-57`;
`WebDAVClient.UnitTests.csproj:4-18`).

A compiling but failing revision can be published through the manual path.

**Recommendation:** One shared test-gated publishing job, or remove the duplicate
publisher. Explicitly pack the library.

**Acceptance:** Every NuGet push depends on tests for both frameworks. Validate a
failing-test scenario in a disposable CI branch without publishing.

## Additional hardening, not confirmed defects

- **Download lifetime:** `Client.cs:1087-1096` returns a raw stream and loses the
  response reference. Normal stream disposal generally releases transport
  resources; a default-transport success leak is not established. A delegating
  stream owning the response makes custom-content cleanup explicit.
- **Upload ownership:** Request disposal also disposes supplied `StreamContent`
  and the caller stream (`Client.cs:1176,1204,1212-1214`). Document this or add an
  intentional leave-open mechanism; retries require reopened/replayable input.
- **Token concurrency:** Providers run once per request without coordination
  (`BearerTokenAuthenticationHandler.cs:15-19,51-61`). Document thread safety and
  provide a single-flight refresh example, rather than globally serializing I/O.
- **Retry policy:** No automatic retries is not itself a defect. Future retries
  need replayability, bounded deadlines, jitter, `Retry-After`, and ambiguous
  mutation/partial-failure rules. DELETE/MOVE/COPY reject 207 but discard useful
  member-level recovery diagnostics.
- **Test infrastructure:** `Helpers\StubHttpMessageHandler.cs:19-47` has a
  synchronous responder, eager request buffering, ignored cancellation, and
  unsynchronized captures. Add a separate async/token-aware streaming handler.
- **CI/maintenance:** Add nonpublishing PR validation, align publisher concurrency,
  and distinguish skipped duplicate publication from a new release.
  `.github\copilot-instructions.md:17-19` incorrectly describes tests as absent.

## Validation and limitations

The parent restored a missing test dependency and ran the Release test project:
238/238 passed for net8.0 and 238/238 for net10.0. Existing tests do not exercise
the stalled-body and resource-lifetime scenarios above. No live server, remote
workflow history, branch protection, package history, or runtime resource
measurements were inspected. Severity reflects plausible recovery/availability
impact, not a claim that every call exhibits each issue.
