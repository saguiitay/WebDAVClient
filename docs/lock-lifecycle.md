# Explicit lock lifecycle

`DavLockLease` adds an explicitly renewed lifecycle for one RFC 4918 write lock
in **3.2.0**. This addition is not available in earlier packages.
`DavClient` remains the low-level, result-oriented surface; the
[locking example](locking.md) demonstrates that API with configured authentication
and run-owned cleanup.

The complete program below uses an anonymous endpoint. Configure an authenticated
transport as described in [authentication](authentication.md) when required.
It changes the display-name property of an existing `document.txt`; use a
disposable test endpoint. No timer is started and no mutation is retried.

<!-- compile-example: explicit-lock-lifecycle -->
```csharp
using System;
using System.Threading;
using WebDAVClient;
using WebDAVClient.Protocol;

using var client = new DavClient(new Uri("https://example.com/dav/"));
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
var path = DavPath.FromSegments("document.txt");
var lease = await DavLockLease.AcquireAsync(
    client, path, cancellationToken: deadline.Token);

try
{
    await lease.RenewAsync(cancellationToken: deadline.Token);
    var conditions = lease.CreateConditions();
    var result = await client.UpdatePropertiesAsync(
        path,
        new[]
        {
            DavPropertyChange.Set(DavXmlValue.Text(
                new WebDAVClient.Model.PropertyName("DAV:", "displayname"), "Document"))
        },
        new DavRequestOptions { Conditions = conditions }, deadline.Token);
    result.EnsureSucceeded();
}
finally
{
    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await lease.ReleaseAsync(cleanup.Token);
}
```

Acquisition, renewal, mutation and release failures propagate to the caller.
The explicit release has its own deadline and is attempted even if renewal or
mutation fails. A release failure can mask an earlier exception; production
error handling should preserve/report both failures. `await using` is also
supported, but `DisposeAsync` has no caller-supplied cancellation deadline.

An ETag is useful even with a lock: acquiring a lock does not establish that the
representation still matches the version the application previously read.
Pass that previously observed ETag to `CreateConditions(entityTag)` when needed.
`CreateConditions()` tags the original lock target, optionally ANDing the token
with the supplied entity tag. For a depth-infinity lock, use these conditions on
an affected descendant; for a MOVE/COPY involving other locked bindings, construct
the complete multi-resource `DavIfConditions` yourself.

## Confirmation and state

Acquisition requires a successful 200/201 result, exactly one valid
`Lock-Token` header, and exactly one matching write-lock observation in
`DAV:lockdiscovery`. Granted scope and depth must match the request. Additional
shared-lock observations are not treated as ownership of those locks.
Server-provided lockroot/owner URLs are data and are never followed.
`DavLock.Root` preserves the scalar lockroot href verbatim, including unescaped
filenames emitted by servers such as SabreDAV 4.7.0. Correct URI escaping remains
a server obligation; this optional metadata is not used to establish
ownership or manufacture a request target. Its container/href cardinality still
must be valid; token identity, scope, depth and granted timeout checks are unchanged.

| State | Meaning |
| --- | --- |
| `Active` | A matching lock and an infinite or not-yet-elapsed finite timeout were confirmed. |
| `Expired` | The last confirmed finite timeout elapsed locally. |
| `Uncertain` | The timeout was omitted, or an explicit renewal/release is pending or failed to confirm its outcome. |
| `Released` | The one release attempt returned HTTP 204. |

Finite expiry is measured with `TimeProvider` monotonic timestamps, starting
**before** each acquisition/renewal request. Network and XML parsing time therefore
consume the grant conservatively. Wall-clock adjustments do not extend a lease.
The optional `TimeProvider` supports deterministic clocks in applications/tests.
Missing timeout values never inherit the requested preference.
An explicit `Second-0` grant produces an immediately expired lease, not an
infinite or unknown one.

`Active` is not proof of present server-side exclusivity. A server can revoke
locks, lose state, or reject a particular mutation. Always submit conditions and
inspect the operation result. `CreateConditions` refuses non-active local states.

## Explicit renewal, release and failure

RFC 4918 section 10.5 requires servers to bracket the `Lock-Token` header.
For interoperability with servers such as WsgiDAV 4.3.5 that omit those brackets,
the client accepts a single syntactically valid bare absolute token **only**
when exactly one validated lockdiscovery observation confirms the same opaque
token. Missing, multiple, invalid, contradictory or ambiguous tokens still fail.
`DavLockResult.LockToken` provides the confirmed token with brackets; raw
`GetHeader("Lock-Token")` and `Headers` preserve the server's wire deviation.
This tolerance neither fabricates a grant/timeout nor follows the token URI.

`RenewAsync` issues exactly one bodyless refresh request with one untagged token,
as required by RFC 4918 section 9.10.2. There is no acquisition fallback, background
renewal, or automatic retry. Expired/uncertain observations may be reconciled by an
explicit renewal. A successful renewal must identify the same token and retain
scope/depth. Both low-level and managed refresh now require HTTP 200 with a
`DAV:prop`/`DAV:lockdiscovery` response. RFC 4918 section 9.10.2 requires a
bodyless **request**, not a bodyless successful response. Missing response
confirmation is a protocol error and cannot establish renewal.

Renewals/releases are serialized. Cancellation while waiting for the serialization
gate does not change the last confirmed state. Once an operation starts, a
transport failure, rejection, malformed body, or cancellation leaves the state
uncertain rather than asserting continued ownership.

`ReleaseAsync` and `DisposeAsync` together make **at most one** UNLOCK attempt.
Failure propagates to that caller; subsequent calls do not replay the request.
The retained `Token`, `Path` and `Observation` allow deliberate server
reconciliation using the low-level API. A release attempt prevents later
renewals. Disposal can throw; applications must account for cleanup failures
without masking an earlier operation failure.

Acquisition failures can also be ambiguous: a lock may have been created before
a response was lost or rejected as malformed. No lease is returned in that case,
and no compensating UNLOCK is guessed. Discover/reconcile server state explicitly.

The client stays caller-owned and must outlive the lease. This helper is neither
a durable lock store, a distributed mutex, a fairness mechanism, nor a guarantee
that a server supports shared locks. Callers wanting automatic scheduling must
build their own explicit, cancellation-aware policy above this API.

## Evidence

`tests\WebDAVClient.UnitTests\DavLockLeaseTests.cs` exercises grant validation, finite/
infinite/omitted timeouts, monotonic acquisition/renewal cost, expired-state
reconciliation, tagged validators, rejection/transport/cancellation outcomes,
serialization, and one-attempt cleanup on both target frameworks.
These deterministic handler tests do not establish independent-server lock
interoperability or prove server-side atomicity.

A bounded local run against pinned WsgiDAV 4.3.5 / cheroot 11.1.2 exercised the
independent interoperability harness on both target frameworks: basic
authentication yielded 19 passing observations and two unavailable/unsupported
extension observations per framework, with zero nonpassing outcomes and an empty
fixture share after cleanup. Anonymous runs yielded 18 passing observations
without the basic-authentication boundary probe. This is narrow interoperability
evidence, not server certification or evidence for other server implementations.
