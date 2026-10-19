# Critical performance review

Reviewed 2026-10-19 at revision `ac0ef8a` by a dedicated performance agent.

## Remediation progress

- PERF-01: Already addressed by correctness/reliability commits `44769de`,
  `370f337`, and `2d1f130`. MOVE/COPY and failed downloads dispose responses;
  successful download streams own response/content lifetime.
- PERF-02: Already addressed by `0510260`. Metadata parsing uses asynchronous
  XML throughout, with token-bound reads and body deadlines. Existing
  prefix-then-stall regressions reject synchronous network reads.
- PERF-04: `SendUploadAsync` uses `ResponseHeadersRead` for either primary or
  separate upload clients. Status-only full/partial uploads never buffer PUT
  response bodies and still dispose them. Eight new scenarios failed before
  the fix and pass afterward, covering success and failure routing.

Original findings below describe the reviewed revision, not current behavior.

## Outcome

**No critical performance issue was confirmed.** Downloads stream, uploads use
`StreamContent`, HTTP clients are reused, and XML parsing is forward-only.
The following evidence-backed improvements are noncritical. Severity depends on
workload; no production benchmarks or resource-exhaustion measurements were taken.

| ID | Priority | Finding | Confidence |
|---|---|---|---|
| PERF-01 | High, noncritical | Undisposed responses can impair connection reuse | 9/10 |
| PERF-02 | Medium | Metadata parsing synchronously reads network streams | 10/10 |
| PERF-03 | Medium | Listings materialize all entries and duplicate reference storage | 10/10 |
| PERF-04 | Medium | Upload responses are unnecessarily buffered | 10/10 |
| PERF-05 | Low | Concurrent cold requests duplicate discovery | 10/10 |

## PERF-01: Response cleanup and connection reuse

**Evidence:** `WebDAVClient\Client.cs:900-931` leaves MOVE/COPY responses
undisposed; `:1091-1096` does the same for failed downloads. Requests use
`ResponseHeadersRead` at `:1161`.

Repeated operations receiving nonempty, unread bodies can retain response-associated
transport resources and reduce reuse. With K outstanding responses, resource
retention can grow with K; this is a conditional static estimate, not a measured
socket count. Empty-body responses do not establish the same problem.

**Recommendation:** Dispose MOVE/COPY responses on every path. Dispose download
responses on failure or failed stream acquisition, preserving successful stream
lifetime. Coordinate with REL-02 rather than implementing two separate fixes.

**Acceptance:** Tracking-content disposal tests for success/error paths; bounded
loopback test with nonempty bodies and a low `MaxConnectionsPerServer`.

## PERF-02: Blocking metadata reads

**Evidence:** `WebDAVClient\Client.cs:365-367,873-875` invokes synchronous parsing;
`WebDAVClient\Helpers\ResponseParser.cs:103-125` calls synchronous `Read()`.
The same pattern appears in `LockResponseParser.cs:29-31` and
`PropPatchResponseParser.cs:37-39`.

Slow, chunked bodies can occupy O(C) workers for C simultaneous operations, even
though the public methods are async. Parsing remains approximately linear;
`ConfigureAwait(false)` does not make reads asynchronous.

**Recommendation:** Async XML parsing throughout the helper chain, backed by
cancellation-aware reads. Coordinate with REL-01.

**Acceptance:** Delayed-chunk streams that detect synchronous reads; measure
worker usage and unrelated-task latency under bounded concurrency.

## PERF-03: Large listing allocation

**Evidence:** `ResponseParser.cs:100-102,313-314` accumulates all items;
`Client.cs:365-397` allocates another list for parent filtering.
`ResponseParser.cs:67-69` parses all responses before selecting a single item;
`:345-355` materializes property XML even for property-name mode.

Space is O(N + V) for N entries and V retained property characters. The second
reference array adds approximately 8N bytes on a 64-bit runtime, about 7.6 MiB
for one million entries, excluding headers. This is a static estimate; no
ordinary-workload OOM was established.

**Recommendation:** Filter in place and skip unwanted XML without creating a
string. Consider a backward-compatible `IAsyncEnumerable<Item>` API later.

**Acceptance:** Generated 1,000/10,000/100,000-entry listings; record allocations,
peak memory, and time to first item, including large properties in propname mode.

## PERF-04: PUT response buffering

**Evidence:** `WebDAVClient\HttpClient\HttpClientWrapper.cs:39-41` uses the
default `ResponseContentRead`; `Client.cs:520-536,563-579` only inspects status.
The upload request itself streams at `Client.cs:1202-1204`.

For response size B and concurrency C, unnecessary buffering can approach O(CB).
Normal bodyless PUT responses avoid this cost.

**Recommendation:** Use `ResponseHeadersRead` in `SendUploadAsync`, retaining
caller response disposal.

**Acceptance:** Recording response content proves status handling does not first
buffer the body; existing routing tests do not cover completion behavior.

## PERF-05: Duplicate cold-start discovery

**Evidence:** `Client.cs:1233-1245` checks the cache, awaits discovery, then
assigns without coordinating concurrent callers.

C overlapping first operations can issue C discovery requests instead of one.
This is a cold-start cost, not recurring steady-state overhead.

**Recommendation:** Share in-flight initialization or use an async gate with a
second cache check. Define retry and per-caller cancellation semantics.

**Acceptance:** Gate discovery completion, start concurrent calls, and assert one
discovery request plus one actual request per operation. Use thread-safe captures.

## Validation and limitations

Local production code, relevant tests, workflows, and documentation were reviewed.
The parent ran the Release unit suite after restoring a missing dependency:
238 tests passed on each of net8.0 and net10.0. Existing passes do not establish
these workload-specific guarantees. No live server, intrusive benchmark, socket
trace, or production traffic profile was used. Successful download streams are
intentionally retained until caller disposal; that alone is not a leak.
