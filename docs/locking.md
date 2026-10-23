# Locks and conditional writes

**Example:** [Locking.cs](../examples/WebDAVClient.Examples/Locking.cs).
Acquisition, token handling, unlock and collection cleanup are in
[MutationExamples.cs](../examples/WebDAVClient.Examples/MutationExamples.cs).

```pwsh
dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- locking --allow-writes
```

Use a disposable endpoint with PUT, MKCOL, LOCK, UNLOCK and DELETE permission.
Some WebDAV servers do not implement locking; OPTIONS advertisements are a hint,
not proof that a particular resource/account can acquire a lock.

## Lifecycle

The example creates a run-owned file, obtains an exclusive write lock, submits
its token in a structured DAV `If` condition during an upload, explicitly
refreshes the lock and attempts unlock before deleting the run-owned collection.
Tokens are not printed.

In 3.2.0, successful refresh requires HTTP 200 with a `DAV:prop` response
containing `DAV:lockdiscovery`. The request remains bodyless; an empty successful
response is a protocol error, not confirmation that the lock was renewed.

`LockOptions` supports exclusive/shared scope and depth zero/infinity.
`Timeouts` is an ordered preference list, not a promise of the granted duration.
Inspect observed lock metadata. Missing granted timeouts stay unknown; a 207
recursive LOCK is not a granted tree-wide lock.

Modern lock tokens passed to `DavCondition.StateToken`, `RefreshLockAsync` and
`UnlockAsync` are bare opaque URIs, without `<...>` brackets. The example obtains
the canonical `Lock-Token` response header and removes its wire brackets.
If no usable token header is returned it fails explicitly and advises
reconciliation rather than fabricating a token.

## Explicit lease helper (3.2+)

[`DavLockLease`](lock-lifecycle.md) wraps one confirmed lock with conservative
expiry tracking, serialized explicit renewal and at most one release attempt.
It does not schedule renewal, retry mutations or reacquire a lost lock.
The existing runnable example intentionally retains the low-level API so you can
see acquisition, conditional writing, refresh and cleanup separately.

## HTTP validators and DAV `If` are different

`IfMatch` protects the HTTP representation. `DavIfConditions` represents the
DAV grammar, including state tokens, ETags, `Not`, alternative lists and tagged
resource conditions. Do not treat alternative lists as if every listed resource
must be locked. A lock on one path is not permission to replace any destination.
Use tagged source/destination conditions as your server and operation require.

Typed conditions do not guarantee server enforcement. Independent tests found
that WsgiDAV 4.3.5 accepts an absolute-URI-tagged MOVE with a valid lock token
and a false ETag. Do not rely on that profile for the affected guarded mutation;
see the [version-specific restriction and evidence](../interoperability/GAPS.md#p0-do-not-rely-on-ignored-tagged-move-conditions-in-wsgidav).

### Explicit absolute-path resource tags (upcoming 3.3.0)

`DavIfConditions.TaggedAbsolutePaths(...)` selects the other RFC 4918
resource-tag form for a verified configuration. This API is in the upcoming
3.3.0 source, not the published 3.2.0 package. For a source file with a confirmed
lock token and quoted strong ETag:

```csharp
var conditions = DavIfConditions.TaggedAbsolutePaths(
    new DavTaggedConditions(source, new DavConditionList(
        DavCondition.StateToken(lockToken),
        DavCondition.EntityTag(strongEtag))));
var result = await client.MoveAsync(source, destination,
    new MoveOptions { Conditions = conditions }, cancellationToken);
result.EnsureSucceeded();
```

Choose the form before sending the mutation. `Tagged(...)` keeps its existing
absolute-URI behavior; there is no discovery, automatic fallback or replay.
Both factories resolve and validate **every** resource against the configured
scheme/host/port before rendering a header. The opt-in omits only the authority:
resolved escaped paths, endpoint prefixes, queries and condition lists remain
unchanged. It does not fix servers that ignore conditions in both forms or
establish compatibility with untested reverse-proxy configurations.

## Failure and cleanup

412 can mean an unsatisfied validator/condition; 423 can mean a conflicting lock.
An account can lack permission despite an advertised LOCK method.
Keep tokens private. Cleanup uses independent five-second deadlines and reports
failure; a server/network failure can still leave resources requiring manual
cleanup. The client does not automatically renew leases or retry mutations.
Cancellation is not rollback and can leave the result of lock acquisition
ambiguous.

The [legacy reference](reference.md#lock--unlock-and-refresh) retains
`LockInfo` recipes. Its bracket acceptance differs from modern bare-token APIs.
