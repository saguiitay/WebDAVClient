# Changelog

Short, user-focused notes for each release. Each GitHub release links to its
versioned NuGet package. Detailed historical notes remain in
[the API reference](docs/reference.md#release-notes).

Historical GitHub releases from 2.3.0 through 3.2.0 were backfilled using the
source commit embedded in each published NuGet package. Earlier packages remain
available on NuGet but lack that metadata; no source tags were guessed for them.

## [Unreleased]

- Push code without publishing a NuGet package. Publication and GitHub release
  creation now require a manual workflow run; pushes and PRs only validate.

## [3.2.1] - Unreleased

- Add a separate, nonpackable independent-server compatibility harness with
  offline regressions and explicitly opted-in fixture workflows.
- Link versioned positive and negative server evidence, bounded diagnostics,
  and prioritized interoperability follow-up work.
- Verify guarded-write content/ETag integrity and valid/invalid lock-conditioned
  mutations with the versioned interoperability suite.
- No production API/runtime changes or automatic package publication.

## [3.2.0] - 2026-10-21

- Manage WebDAV locks with `DavLockLease`: explicitly renew or release a lease
  and inspect its conservative expiry and uncertain state.
- Handle empty downloads and more server response formats, including escaped
  Unicode resource names and unsigned lock timeouts.
- Reject ambiguous resource identities and unconfirmed lock grants. Lock refresh
  now requires HTTP 200 with lock-discovery XML; an empty response is not success.
- Opt into the PROPPATCH path-plus alias policy only for verified server profiles.
- No automatic lock renewal, retries, or full RFC conformance claim.

## [3.1.1] - 2026-10-21

- Find library code, tests and runnable samples under `src`, `tests` and
  `examples`, with matching build instructions.
- Keep MIT terms and upstream attribution in the package; retain the old
  combined-license record in Git history.
- No library API or runtime behavior changes.

## [3.1.0] - 2026-10-21

- Resume file downloads with `DownloadFileAsync`, strong validators and verified
  durable checkpoints.
- Stream into a partial file and finish without overwriting an existing
  destination.
- Require identity encoding when needed with `DownloadOptions.RequireIdentityEncoding`.
- Includes a runnable resumable-download example. No automatic retries or
  resumable uploads.

## [3.0.4] - 2026-10-21

- Connect using the new Nextcloud and ownCloud Server listing/download examples
  and app-password setup guides.
- Server compatibility remains unverified; no library API or runtime changes.

## [3.0.3] - 2026-10-21

- Start with a complete authenticated application, focused task guides and
  matching runnable examples.
- Find authentication, transfers, file management, locking and collection-sync
  guidance without reading the full API reference.
- No library API or runtime behavior changes.

## [3.0.2] - 2026-10-21

- Clarify storage and file-transfer use cases and improve NuGet search metadata.
- Document server compatibility limits without claiming verified Nextcloud or
  ownCloud support. No library API or runtime changes.

## [3.0.1] - 2026-10-21

- Correct the packaged quickstart to use the published v3 APIs directly from NuGet.
- Clarify MIT licensing and preserve historical upstream attribution.
- No library API or runtime behavior changes.

## [3.0.0] - 2026-10-20

- Use the new resource-oriented `DavClient` with immutable endpoints and
  per-request options. Existing `Client` / `IClient` APIs remain available.
- Inspect exact resource/property outcomes, structured errors and typed XML
  property values instead of flattening server responses.
- Stream listings, use conditional writes, validate download ranges and manage
  response ownership explicitly.
- Submit a single collection-sync batch; durable sync and automatic retries are
  not included.
- Migration is explicit: modern uploads retain input ownership and COPY/MOVE
  do not overwrite by default. See the
  [migration guide](https://github.com/saguiitay/WebDAVClient/blob/v3.0.0/README.md#v3-migration-and-contracts).

## [2.8.0] - 2026-10-20

- Cancel stalled metadata reads with configurable body deadlines and preserve
  member-level failure details for recovery.
- Improve range validation, resource URL handling, sparse listings and
  concurrent connection discovery.
- Reject cross-origin resource URLs and misleading nested XML metadata.
- Includes XML API documentation, safer samples and explicit authentication
  factories. No automatic retries.

## [2.7.0] - 2026-05-01

- Lock, refresh and unlock resources; submit lock tokens with mutations.
- Set and remove custom properties with PROPPATCH.
- Request selected property values or property names, and discover server
  capabilities with OPTIONS.
- Authenticate with static bearer tokens or an async token provider.
- Correct overwrite handling on COPY/MOVE and collection DELETE depth.

## [2.5.5] - 2026-05-01

- Reduce header lookup overhead for ordinary requests and uploads.

## [2.5.4] - 2026-05-01

- Reduce XML parsing allocations, especially for large directory listings.

## [2.5.3] - 2026-05-01

- Reduce per-call request-body allocations for MOVE and COPY.

## [2.5.2] - 2026-05-01

- Avoid repeated base-path resolution for each returned folder in a listing.

## [2.5.1] - 2026-05-01

- Add automated coverage for client operations, response parsing and exceptions.
- No new public API.

## [2.5.0] - 2026-05-01

- Fix shared HTTP handler disposal when using a separate upload timeout.

## [2.4.0] - 2026-05-01

- Target .NET 8 and .NET 10.
- **Breaking:** drop .NET 9 support.

## [2.3.0] - 2025-09-04

- Update framework support to .NET 8 and .NET 9.
- Refresh package metadata.
