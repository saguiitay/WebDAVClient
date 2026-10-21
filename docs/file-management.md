# Create, copy, move, update properties and delete

**Example:** [FileManagement.cs](../examples/WebDAVClient.Examples/FileManagement.cs),
with creation/upload/cleanup in
[MutationExamples.cs](../examples/WebDAVClient.Examples/MutationExamples.cs).

```pwsh
dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- file-management --allow-writes
```

Configure a [disposable authenticated endpoint](authentication.md) with permission
for MKCOL, PUT, COPY, MOVE, PROPPATCH and DELETE. Support is server-dependent.

## What it demonstrates

The executable creates a uniquely named collection and a source file, copies
that file, moves the copy, sets/removes an example custom property in one ordered
PROPPATCH, and deletes the moved file. It then attempts to remove the collection
it created. It never uses a pre-existing collection as its cleanup target.

| Operation | Modern method | Important default |
|---|---|---|
| Create directory | `CreateCollectionAsync` | Bodyless MKCOL, no automatic parent creation |
| Full file upload | `UploadAsync` | Borrowed stream; example uses create-only `IfNoneMatch = "*"` |
| Copy | `CopyAsync` | `Overwrite = false`; COPY depth zero/infinity only |
| Move | `MoveAsync` | `Overwrite = false`; no copy/delete fallback |
| Change properties | `UpdatePropertiesAsync` | Ordered set/remove in one server-atomic request |
| Delete file/collection | `DeleteAsync` | Collection deletion has infinite-depth semantics |

Every operation calls `EnsureSucceeded()`. HTTP 207 is not a single flat success:
inspect member/property outcomes, especially after recursive operations. A
property-only namespace mutation response does not confirm completion.
The example removes its custom property in the same request to leave no
persistent metadata on a successful run.

## Protect real application data

Use `IfMatch` / `IfNoneMatch` and, where applicable, structured DAV `If`
conditions. Destinations and tagged conditions must stay on the configured
scheme/host/port. Same-origin paths can still be outside the endpoint directory;
the endpoint is not a sandbox.

Do not automatically replay DELETE/MOVE/COPY or PROPPATCH after a timeout or
partial result. Reconcile state first. Confirmed run-owned cleanup uses its own
five-second deadline; cleanup failure is visible and returns a failing exit code.
An ambiguous MKCOL is not treated as confirmed ownership, so manual
reconciliation can still be necessary.

For locking and lock-token-aware updates, see [locking](locking.md).
