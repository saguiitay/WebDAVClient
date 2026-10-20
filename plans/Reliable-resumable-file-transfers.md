# Reliable resumable file transfers

## Available contract (upcoming 3.0.0)

The dependency-free `WebDAVClient.Transfers` namespace adds one operation:

```csharp
using WebDAVClient;
using WebDAVClient.Transfers;

using var client = new DavClient(new Uri("https://example.com/dav/"));
await client.DownloadFileAsync(
    DavPath.FromSegments("archives", "backup.zip"),
    @"C:\Downloads\backup.zip",
    cancellationToken);
```

The parent directory must already exist. The destination must **not** exist.
The client remains caller-owned. With an injected `HttpClient`, configure
authentication/TLS yourself and disable automatic redirects **and automatic
decompression** before injection. Transparent decompression can remove the
headers needed to validate byte offsets; it cannot be reliably detected afterward.

Call the same method with the same remote resource and local destination after
an interruption, including in a new process. There is no in-memory session to
restore and no automatic retry. A new cancellation token can be supplied.

This implements the conservative download portion of protocol plan P-06 using
the existing metadata-rich `DavClient.DownloadAsync` range contract. It does not
change legacy `Client.DownloadPartial` or `UploadPartial` behavior.

## Representation and validation requirements

- The initial request is a full identity GET. A known explicit `Content-Length`
  and a **strong** ETag are required, even for a zero-byte resource. Weak ETags,
  missing ETags, wildcard validators and unknown initial lengths are rejected.
  Dates alone cannot establish a stable byte representation.
- A resumed nonempty prefix sends `Range: bytes=<offset>-`, `If-Range: <ETag>`
  and `If-Match: <ETag>`. A zero-offset checkpoint uses a conditional full GET.
  Every response must repeat the exact saved strong ETag.
- An accepted range must be 206, cover precisely the requested remainder, and
  report the same complete representation length. The existing range validator
  rejects ignored-range 200, malformed/suffix-mismatched Content-Range, inconsistent
  Content-Length and encoded content before bytes are accepted.
- A resumed response may omit Content-Length when its validated Content-Range
  supplies the total; actual bytes are still counted exactly.
- Short bodies, extra bytes, connection errors and failure while checking EOF
  never publish a file. Receiving the declared number of bytes is **not** enough:
  the stream must also end successfully.
- HTTP 304 is not downloadable content. Rejections including 404, 412 and 416
  are explicit failures, not signals to append, discard state or retry.
- All offsets, lengths and range endpoints are 64-bit. Processing uses one
  64 KiB body buffer, independent of file size. Prefix verification is streaming.

`DownloadOptions.RequireIdentityEncoding` is also available for direct low-level
GETs. It sends `Accept-Encoding: identity` and rejects encoded successful
responses. Ordinary low-level GET behavior is unchanged when it is false.

## Checkpoints, ownership and publication

For destination `backup.zip`, the operation reserves:

| Artifact | Purpose |
| --- | --- |
| `backup.zip.davpart` | Staged bytes, never a completed destination |
| `backup.zip.davcheckpoint` | Versioned checkpoint for the durable prefix |
| `backup.zip.davcheckpoint.new` | Replacement checkpoint being flushed |
| `backup.zip.davlock` | Persistent exclusive-use sentinel |

Do not use these names for unrelated files. Use a trusted local directory and
avoid symlink/reparse-point aliases. Cooperating calls for the same destination
are serialized by an exclusive OS file handle; a conflicting call fails rather
than waits. The lock sentinel intentionally remains after success. **Never delete
it while another process might be using the destination.**

Every MiB of progress, the staged bytes are flushed to disk before a new
checkpoint is flushed and atomically replaces the previous checkpoint. The
checkpoint binds the resolved remote URI, strong ETag, total length, durable
offset, prefix SHA-256 and EOF-completion flag. Its serialized payload also has a
SHA-256 checksum. Checkpoints are bounded to 16 KiB; excessively long resource
identities are unsupported.

On restart, both data and checkpoint must exist. The complete durable prefix is
rehash-verified **before** any network request; shorter or altered data fails
closed. Extra uncommitted bytes are truncated back to the verified checkpoint.
Therefore up to approximately one checkpoint interval may need retransmission.
The digests detect accidental corruption, not a malicious local writer or an
untruthful server reusing a strong ETag for changed content. They are not a
server-provided end-to-end cryptographic authenticity guarantee.

After exact length and EOF validation, the complete file and final checkpoint
are flushed. The staged file is atomically renamed, without replacement, in the
same directory. Only then is the checkpoint removed. A crash before rename can
recover from a complete checkpoint **without** another network request. A
destination appearing concurrently is never overwritten; the complete staging
state remains available for explicit reconciliation.

Every response is disposed on success, cancellation and failure. Input streams
are never required for this operation; nonseekable response streams work.
Cancellation is passed through network/body/disk I/O. Synchronous durable flushes
and the final filesystem rename are not interruptible. Once publication has
occurred, a later cancellation request cannot roll it back.

The implementation uses .NET durable file flushes and same-directory atomic
renames. It does not promise portable directory-fsync semantics or survival of
every power-loss/filesystem/hardware failure. Missing or inconsistent artifacts
after such failures are rejected rather than guessed at.

## Explicit recovery

| Failure | Recovery |
| --- | --- |
| Cancellation, interrupted/short/overlong body | Call again with the same arguments; only the last durable verified prefix is reused. |
| Changed/missing/weak ETag, changed total, ignored/malformed range, HTTP rejection | Reconcile the remote representation. Do not append or retry blindly. Preserve state for investigation, or explicitly remove the transfer artifacts while no operation is running and start anew. |
| Invalid checkpoint, altered/short prefix, orphan data/checkpoint | No network request is made. Restore a known matching pair, or explicitly discard both and restart. |
| Failure before the first checkpoint | Ordinary exceptions clean up the uncheckpointed staged file. A process crash in this small creation window can leave orphan data requiring explicit cleanup. |
| Destination already exists | Never overwritten. Reconcile it separately; choose a different destination or deliberately remove it only when appropriate. |
| Crash or cleanup error after atomic rename | The destination may already be complete even though checkpoint cleanup did not finish or the caller did not observe success. Reconcile/verify the destination before discarding stale checkpoint artifacts. |

Protocol/header/body-integrity and checkpoint/prefix errors are explicit
`InvalidDataException` or the existing `WebDAVException` contracts; HTTP codes
remain inspectable on `WebDAVException`. Filesystem/network failures use their
existing I/O exceptions. Cancellation uses `OperationCanceledException`.
Do not log checkpoint contents indiscriminately: they contain the resource URI.

## Reliable uploads: conditional full PUT, not partial upload

The existing `DavClient.UploadAsync` already supports standard conditional full
PUT with explicit stream ownership. Open a fresh replayable source for **each
caller-approved attempt**, and retain it in a using scope:

```csharp
using var content = File.OpenRead(@"C:\Exports\backup.zip");
var result = await client.UploadAsync(
    DavPath.FromSegments("archives", "backup.zip"),
    content,
    new UploadOptions { IfNoneMatch = "*" },
    cancellationToken);
result.EnsureSucceeded();
```

Use `If-Match` with a previously observed strong ETag to replace only a known
version. The default `DisposeContent = false` retains caller ownership;
`DisposeContent = true` explicitly transfers it after argument validation.
Ordinary full uploads support nonseekable caller streams, but a consumed
nonseekable stream is not replayable. A source factory or reopened immutable
file is required for another attempt.

An exception/cancellation after sending PUT may mean that the server committed
the mutation but the response was lost. Reconcile using HEAD/GET metadata and,
when required, content/application checksums **before** deciding whether another
conditional full PUT is appropriate. A matching length alone is not proof of
identity. There is no automatic replay, retry loop, upload journal, source
snapshot guarantee or resumable-upload protocol in this namespace.

GET byte-range support does **not** establish partial-PUT support. RFC 9110
section 14.5 requires private agreement for partial PUT; a server can interpret
such a request as a complete replacement. No high-level partial-upload feature
is implemented, because there is no negotiated/configured agreement in the
current modern API. Legacy partial PUT remains an explicitly nonstandard
compatibility operation, not an inferred capability.

## Deliberate limits

No automatic restart on changed content, overwrite, multi-range/parallel
download, unknown-length resume, Last-Modified-only resume, application progress
callbacks, custom checkpoint interval/location, arbitrary stream destination,
directory synchronization, server-side checksum negotiation or upload retries
are provided. The small API favors verifiable restart and publication semantics
over heuristic recovery. Filesystem permissions, free space and server
conformance remain caller/deployment responsibilities.