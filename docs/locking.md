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

`LockOptions` supports exclusive/shared scope and depth zero/infinity.
`Timeouts` is an ordered preference list, not a promise of the granted duration.
Inspect observed lock metadata. Missing granted timeouts stay unknown; a 207
recursive LOCK is not a granted tree-wide lock.

Modern lock tokens passed to `DavCondition.StateToken`, `RefreshLockAsync` and
`UnlockAsync` are bare opaque URIs, without `<...>` brackets. The example obtains
the canonical `Lock-Token` response header and removes its wire brackets.
If no usable token header is returned it fails explicitly and advises
reconciliation rather than fabricating a token.

## HTTP validators and DAV `If` are different

`IfMatch` protects the HTTP representation. `DavIfConditions` represents the
DAV grammar, including state tokens, ETags, `Not`, alternative lists and tagged
resource conditions. Do not treat alternative lists as if every listed resource
must be locked. A lock on one path is not permission to replace any destination.
Use tagged source/destination conditions as your server and operation require.

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
