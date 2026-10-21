# Uploads, downloads and byte ranges

**Example:** [Transfers.cs](../examples/WebDAVClient.Examples/Transfers.cs).
The full upload and cleanup path is in
[MutationExamples.cs](../examples/WebDAVClient.Examples/MutationExamples.cs).

## Run it on disposable storage

Configure [authentication](authentication.md), then supply an existing local file:

```pwsh
$env:WEBDAV_INPUT = "upload.bin"
$env:WEBDAV_OUTPUT = "download.bin"
dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- transfers --allow-writes
```

The command creates a unique collection, uploads your file with
`If-None-Match: *`, downloads it to a **new** local file, requests bytes 0-15
(inclusive), and attempts bounded remote cleanup. An empty source file can
produce 416 for the range; use a nonempty file. EOF clipping is valid for files
shorter than 16 bytes.

The local output is retained. If a transfer fails, a partial local file may
remain. A failed range step does not undo a previously completed full download.
Never use the example against irreplaceable data.

## Ownership and conditions

`UploadAsync` uploads a full representation and borrows its input stream by
default. Keep the input in a `using` scope. `DisposeContent = true` transfers
ownership after validation; a caller scope is still needed for earlier failures.
Use `IfNoneMatch = "*"` for create-only uploads or a server-observed strong
ETag with `IfMatch` to protect an update.

Always dispose `DavDownloadResult`, including 304 and rejection results.
Call `EnsureHttpSuccess()` before accessing content in a plain download.
For conditional GET, handle `NotModified` first: 304 has no content and isn't a
normal 2xx download. Pass cancellation to `CopyToAsync` / `ReadAsync` after the
method returns. The metadata body deadline does not limit download stream reads.

For production atomic completion, write a run-owned temporary file and rename
without replacement only after validation. Do not equate the existence of a
destination file with a complete transfer.

## Ranges are not durable resume

Use one `RangeHeaderValue` in `DownloadOptions.Range`. The library validates 206
and `Content-Range`, rejects ignored-range 200 responses, and requests identity
encoding. `IfRange` requires a range and a strong ETag or HTTP date. A validator
mismatch must be reconciled, not appended blindly.

The example reads its range separately, never appends it. It does **not** persist
checkpoints, reconcile changed entities, automatically retry, or promise durable
resumability. Such functionality is not part of the released client described here.
The modern API has no partial PUT. Legacy `UploadPartial` is a nonstandard,
server-dependent agreement; see the [legacy reference](reference.md).

Cancellation does not prove a PUT was rolled back. A lost response can leave an
uploaded run-owned resource even when creation was not confirmed. Inspect the
printed collection and reconcile before replaying. Cleanup failures are reported
and make the command fail; no cleanup attempts target existing user collections.
