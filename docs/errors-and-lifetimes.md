# Errors, cancellation and lifetimes

The complete error-handling entry points are the [README program](../README.md)
and [example host](../examples/WebDAVClient.Examples/Program.cs).
The runnable getting-started workflow is covered by automated failure tests.

## Modern results versus exceptions

Well-formed server rejections are inspectable results on `DavClient`.
Call operation-specific `EnsureSucceeded()` for core multistatus/LOCK results
or inspect the result explicitly. `EnsureHttpSuccess()` checks only the outer
2xx status, not every member; a 207 body can report failures. For a conditional
download, handle `NotModified` before normal success checking.

Transport failures, cancellation and malformed protocol responses throw.
`WebDAVException` retains outer `ResponseStatusCode`, optional
`FailedStatusCode` and available member diagnostics. A 409 is
`WebDAVConflictException`. Error XML is bounded; malformed XML may be exposed
through an inner exception. Do not assume every error is an authentication issue.

| Outcome | Typical application action |
|---|---|
| 401 / 403 | Check authentication versus authorization; do not log credentials |
| 404 | Check endpoint/path and whether the resource actually exists |
| 409 | Reconcile parent/resource conflicts |
| 412 / 423 | Re-read validators or lock state; do not blindly overwrite |
| 429 / 503 | Consider a bounded application policy and `Retry-After`; mutation ambiguity still matters |
| 507 | Inspect context: storage exhaustion versus RFC 6578 collection truncation |
| 207 | Inspect every relevant resource/property and profile semantics |

Legacy `Client` boolean mutations return true on confirmed success and throw on
failure; they do not use false as a general rejection result. Its
`ResourceStatuses` and HTTP code mapping remain documented in the
[recovery reference](reference.md#recovery-and-retry-policy).

## Who owns what?

| Resource | Modern API | Legacy API |
|---|---|---|
| Owned transport | Dispose `DavClient` | Dispose `Client` |
| Injected `HttpClient` | Caller-owned | Caller-owned |
| Upload input | Caller-owned unless `DisposeContent = true` | Request-owned once created; caller scope still needed before request creation |
| Download | Dispose `DavDownloadResult` | Dispose returned response-owning stream |
| Streaming listing | Dispose `DavListing`; consume once | `List` is a buffered projection |

Finish in-flight operations and returned streams before disposing their
transport. Reopen upload content for each separately reconciled retry.

## Cancellation and budgets

Pass a trailing cancellation token to I/O operations, then pass it again to
download stream reads/copies. A returned stream does not automatically retain
the method's token. `HttpClient.Timeout` bounds send/headers; modern metadata
acquisition/reading uses `DavClientOptions.MetadataTimeout` (100 seconds by
default), while legacy metadata uses `Client.ResponseBodyTimeout`.
For streaming listings that deadline continues through enumeration.

The examples use a 60-second operation budget. Mutation cleanup has independent
five-second budgets. A forced process termination can prevent cleanup; inspect
the printed run-owned collection if the application did not exit normally.

## No automatic retries, and no implicit rollback

A disconnect or cancellation can occur after a server committed a mutation.
First reconcile remote state, then choose conditional replay or manual recovery.
Do not infer that a failed recursive operation changed nothing. The client does
not transparently retry, downgrade MOVE to COPY/DELETE, or refresh tokens itself.

Avoid logging full request/response bodies, authorization headers, lock/sync
tokens or unredacted user paths. Even sanitized exception resource context can
contain application-sensitive path components.
