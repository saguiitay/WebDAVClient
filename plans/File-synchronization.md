# File synchronization

Status: implemented portable core in the upcoming **3.0.0 source**, in the existing
`WebDAVClient` assembly under `WebDAVClient.Sync`. This is not a separately
published `WebDAVClient.Sync` package, desktop agent, or background daemon.
The broader [subpackage proposal](subpackages/WebDAVClient.Sync.md) remains a
future direction; its queue, native filesystem provider, and workload acceptance
claims are not implemented here.

## Deliberately explicit modes

`FileSyncSession` supports:

* **Pull**: one complete RFC 6578 sync-level 1 REPORT, followed by conditional full
  downloads into immediate local files. Subcollections are rejected, not silently
  skipped. There is no recursive scan and no PROPFIND fallback.
* **Explicit outgoing reconciliation**: create-only or ETag-conditional full PUT,
  and ETag-conditional DELETE, individually requested by the caller. Local
  disappearance is never automatically interpreted as a remote deletion.
* **Conflict-stop**: preserve local edits and the old checkpoint. No implicit
  remote-wins/local-wins/bidirectional policy exists.

No method discovers capabilities, retries transport failures, infers renames from
ETags, or resets an invalid token. A server rejecting REPORT is an explicit error.

## Public API

```csharp
FileSyncSession(DavClient client, DavPath collection,
    string directory, string stateDirectory);

Task<string> PullAsync(CancellationToken cancellationToken = default);
Task<string?> GetSynchronizedETagAsync(string name,
    CancellationToken cancellationToken = default);
Task<DavMultiStatusResult> UploadAsync(string name, Stream content,
    string? expectedETag = null, CancellationToken cancellationToken = default);
Task<DavMultiStatusResult> DeleteRemoteAsync(string name, string expectedETag,
    CancellationToken cancellationToken = default);
```

`FileSyncConflictException : IOException` identifies insufficient validators,
unsupported report shapes, or divergent local content. Transport, malformed XML,
HTTP rejection during pull, cancellation, filesystem and JSON errors also
propagate. Outgoing HTTP rejections remain inspectable modern results; call
`EnsureSucceeded()` if throwing behavior is desired.

The session retains but does not own `DavClient`. All outgoing input streams remain
caller-owned. `GetSynchronizedETagAsync` returns a committed validator (or null for
an untracked name), not a stable resource identity, and first finishes any pending
local recovery.

## Example: pull, explicitly publish an edit, reconcile

Both directories must already exist. They must be disjoint, caller-exclusive,
non-link directories on the **same filesystem**, supporting atomic file
replacement. Store state outside the synchronized data directory and protect its
permissions like other sensitive application data.

```csharp
using System;
using System.IO;
using System.Threading;
using WebDAVClient;
using WebDAVClient.Sync;

// Application-specific absolute directories, on the same filesystem.
string files = Path.GetFullPath("files");
string state = Path.GetFullPath("sync-state");
Directory.CreateDirectory(files);
Directory.CreateDirectory(state);

using var client = new DavClient(new Uri("https://example.com/dav/"));
var sync = new FileSyncSession(client, DavPath.Root, files, state);
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));

string token = await sync.PullAsync(deadline.Token);

// After the application edits an existing local file, explicitly publish it.
string name = "notes.txt";
string? previousETag = await sync.GetSynchronizedETagAsync(name, deadline.Token);
using (var content = File.OpenRead(Path.Combine(files, name)))
{
    var put = await sync.UploadAsync(name, content, previousETag, deadline.Token);
    put.EnsureSucceeded(); // 412 preserves the local file and old checkpoint.
}

// Consume the server's authoritative new revision/token, not the PUT response
// alone. Identical local/downloaded bytes make this reconciliation idempotent.
token = await sync.PullAsync(deadline.Token);
```

A null upload validator means **create-only** (`If-None-Match: *`), never
unconditional replacement. For an explicit deletion, obtain the previously
committed non-null validator, call `DeleteRemoteAsync(name, validator)`, inspect
the result, then pull. A confirmed remote deletion removes a local file only if
its bytes still match the committed version, or if it is already absent.

Do not edit files concurrently with a call, and serialize outgoing calls with
pulls in the application. If a PUT succeeds but a later pull sees different local
bytes, it correctly stops rather than overwriting the second local edit.

## Durable ordering and restart

The state directory is reserved for this session. The versioned checkpoint is
bound to the canonical local root and absolute remote collection. Do not move,
copy between accounts, hand-edit, or selectively delete its contents.

1. Hold an exclusive cross-session `session.lock` file handle.
2. Finish a previous `pending.json` journal, if present, before network access.
3. Read the committed token and request one batch.
4. Fully parse/validate **every** resource outcome and path before downloading.
   Missing/weak ETags, collections, denied properties, duplicate/case-aliased
   names, ambiguous hrefs and any 507 fail the whole batch.
5. GET each changed file with `If-Match` on the reported strong ETag and
   `Accept-Encoding: identity`. Reject any nonidentity content encoding, require full
   HTTP 200 and the matching response ETag, stream into a unique staging file,
   flush it to disk, and hash it with SHA-256.
6. Check every affected local file against the old committed SHA-256 or the exact
   staged target hash. Publish a flushed recovery journal by atomic replacement
   **before** replacing/deleting any local files.
7. Replay each journal operation: if the target already matches the intended
   hash (or is already absent for a deletion), it is complete. Otherwise verify
   the old hash, then atomically replace from staging or delete the old file.
8. Only after all operations succeed, flush and atomically replace
   `checkpoint.json` with the new file hashes, ETags and token; then remove the
   journal and leftover stages.

Cancellation/errors before journal publication do not replace/delete local files
or advance the checkpoint. Once the journal exists, cancellation/errors may
leave some target files changed, but **not a prematurely advanced token**.
Restarting the session replays that journal idempotently, including a crash after
the checkpoint replacement but before journal removal. Stage files orphaned by a
crash before journal publication are reclaimed on the next recovery.

This is process-restart recovery on a filesystem honoring atomic same-filesystem
replacement and flush semantics, not a cross-platform guarantee against power
loss, hardware write-cache failure, hostile filesystem changes, or filesystem
corruption. Portable .NET cannot flush directory entries on every supported
platform. No credentials are serialized.

## Deletions, expired tokens and conflict recovery

Only an explicit **resource-level 404 in an incremental REPORT** is a deletion.
An initial null-token report cannot contain deletions. Absent members do not
imply deletion, even in initial synchronization. A denied, malformed, canceled,
truncated or failed response cannot drive deletion. Reports are buffered under
the client's XML limits before any local operations are planned.

An invalid/expired token remains an HTTP/DAV error and leaves state intact. There
is intentionally no automatic reset or mass full-snapshot comparison. To
rebaseline, archive the old data **and complete state directory**, choose fresh
empty data/state directories, perform a null-token pull, then explicitly compare
and reconcile the preserved local edits. Never erase just a token or journal to
force progress.

For local conflicts, preserve the edited file outside the synchronized directory,
restore the committed local revision (or the exact intended remote bytes), then
retry. A pending journal remains authoritative until its intended local changes
finish. Missing/corrupt staged recovery data requires restoring that data from a
backup, or archiving both roots and explicitly rebaselining. No method discards an
unresolved journal.

For an ambiguous outgoing result (lost PUT/DELETE response), reconcile the server
before replaying. Conditional writes prevent blind overwrite, but the session is
not a durable outgoing outbox and does not assign idempotency keys. Reopen streams
for each explicitly authorized replay.

## Containment, resource bounds and unsupported features

* Hrefs must remain at the configured origin and directly inside the selected
  collection. Dot segments, encoded separators, query/fragment escapes and unsafe
  portable filenames are rejected. Names are decoded once and escaped once on
  requests; Unicode, literal percent signs and spaces are preserved.
* Reject symbolic links/reparse points in roots, ancestors and affected files,
  and reject local directories masquerading as files. The caller must prevent
  concurrent renames, link/hardlink manipulation and local writes during calls.
  This is not an adversarial filesystem sandbox or an atomic compare-and-swap
  against another process modifying a file between hash validation and replace.
* Use a dedicated root with portable, case-distinct filenames. The core rejects
  case collisions and common Windows device names, but does not normalize Unicode
  or emulate every filesystem's name-equivalence rules. Choose application-safe
  normalization on case/normalization-insensitive filesystems.
* Transfers are sequential with fixed-size stream buffers; content is never
  buffered wholly in memory. Metadata/state are proportional to tracked files.
  Disk staging must fit the entire changed batch, and the caller manages quotas.
  Supply cancellation/deadlines; there is no scheduler, background worker or cache
  eviction.
* Injected transports must disable both redirects and automatic decompression.
  The owned `DavClient` transport already does so. Synchronization uses the shared
  `DownloadOptions.RequireIdentityEncoding` contract, not a separate encoding parser.
* No recursive directories, automatic upload scan/deletion, file watching,
  native placeholder providers, vendor stable IDs, rename recognition, resumable
  transfers, sparse/partial writes, automatic token recovery, database migration,
  queue coalescing, lock lease integration, encryption, ACL replication, or
  preserved timestamps/permissions. No claim of the subpackage proposal's
  100,000-item/multi-gigabyte/24-hour soak acceptance is made.

The existing `Extensions.CollectionSync` remains the lower-level single-batch
seam for applications needing a transactional database or another durable sink.