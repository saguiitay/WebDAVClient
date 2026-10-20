# Managed lock lifecycle

This reference describes the upcoming 3.0.0 source API, not a published package.
`DavClient` remains the low-level, result-oriented surface.
`DavLockLease` adds an explicitly renewed lifecycle for one RFC 4918 write lock.

```csharp
using System;
using WebDAVClient;
using WebDAVClient.Protocol;

using var client = new DavClient(new Uri("https://example.com/dav/"));
var path = DavPath.FromSegments("document.txt");
await using var lease = await DavLockLease.AcquireAsync(client, path);

// No timer is started. Renew explicitly before the observed timeout expires.
await lease.RenewAsync();
var conditions = lease.CreateConditions("\"previous-etag\"");
var result = await client.UpdatePropertiesAsync(
    path,
    new[]
    {
        DavPropertyChange.Set(DavXmlValue.Text(
            new WebDAVClient.Model.PropertyName("DAV:", "displayname"), "Document"))
    },
    new DavRequestOptions { Conditions = conditions });
result.EnsureSucceeded();
```

An ETag is useful even with a lock: acquiring a lock does not establish that the
representation still matches the version the application previously read.
`CreateConditions()` tags the original lock target, optionally ANDing the token
with the supplied entity tag. For a depth-infinity lock, use these conditions on
an affected descendant; for a MOVE/COPY involving other locked bindings, construct
the complete multi-resource `DavIfConditions` yourself.

## Confirmation and state

Acquisition requires a successful 200/201 result, exactly one bracketed
`Lock-Token` header, and exactly one matching write-lock observation in
`DAV:lockdiscovery`. Granted scope and depth must match the request. Additional
shared-lock observations are not treated as ownership of those locks.
Server-provided lockroot/owner URLs are data and are never followed.

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

`RenewAsync` issues exactly one bodyless refresh request with one untagged token,
as required by RFC 4918 section 9.10.2. There is no acquisition fallback, background
renewal, or automatic retry. Expired/uncertain observations may be reconciled by an
explicit renewal. A successful renewal must identify the same token and retain
scope/depth. The low-level API tolerates bodyless successful refresh responses
for compatibility; such a response **cannot** confirm a managed renewal.

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

`WebDAVClient.UnitTests\DavLockLeaseTests.cs` exercises grant validation, finite/
infinite/omitted timeouts, monotonic acquisition/renewal cost, expired-state
reconciliation, tagged validators, rejection/transport/cancellation outcomes,
serialization, and one-attempt cleanup on both target frameworks.
These deterministic handler tests do not establish independent-server lock
interoperability or prove server-side atomicity.
