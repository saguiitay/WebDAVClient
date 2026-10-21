# One collection synchronization batch

**Example:** [CollectionSynchronization.cs](../examples/WebDAVClient.Examples/CollectionSynchronization.cs).

```pwsh
dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- collection-sync
```

Configure [authentication](authentication.md). The server must implement
RFC 6578 `sync-collection`; a generic DAV or REPORT advertisement alone is not
enough. The command is read-only and saves no checkpoint.

## Initial and incremental requests

Without `WEBDAV_SYNC_TOKEN`, the example requests an initial sync-level 1 batch
with ETags and a limit of 100 entries. For an incremental request, supply an opaque
token previously obtained for **this collection and relevant authorization
context**. Never invent tokens or reuse one across unrelated endpoints.

`SyncCollectionAsync` returns the raw report response, an optional next
`SyncToken`, and `HasMore`. A response-level member 404 is a deletion **only**
in this report profile; it is not a generic rule for PROPFIND or HTTP errors.
An initial synchronization must not contain deletions.

`HasMore` recognizes a 507 on the requested collection. Other 507 outcomes
are not automatically interpreted as collection truncation. The example
distinguishes these and reports failed members/properties instead of silently
advancing a token. Tokens themselves are not logged.

## What your application still owns

Apply the batch durably, reconcile failures, then commit the returned token.
If truncated, follow the server/profile continuation rules explicitly.
Invalid-token errors require a new reconciliation strategy, not an automatic
reset that could infer deletions from incomplete data.

The library does not persist tokens, loop through continuations, infer stable
IDs, detect conflicts, maintain a local mirror, or provide durable file
synchronization. A successful example run only observes one batch; it does not
mean changes have been applied.

`ReportAsync` also accepts profile-defined XML and returns raw XML/member
outcomes. That seam is not a CalDAV, CardDAV, SEARCH, ACL or DeltaV client.
See [protocol support](protocol-support.md).
