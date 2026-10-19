# Package plan: WebDAVClient.FileSystem.Linux

Status: proposed optional platform SDK with native dependencies. Linux portion of D-05; later priority, without automatic Windows/macOS parity.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [sync/cache policy](WebDAVClient.Sync.md), [Windows drive semantics](WebDAVClient.FileSystem.WinFsp.md).

## Purpose and dependencies

Provide a per-user FUSE mount when existing davfs2/GVfs integration does not meet the requirement. First select supported distributions, libfuse API/binding and native dependency versions.

Depends on P-03/P-06, a Linux-specific feasibility spike and an explicit cache/writeback contract. FUSE is native/kernel integration, not a pure managed NuGet deployment.

## Implementation

1. Verify mount/unmount, cancellation, handle lifetime and native callback behavior.
2. Define filesystem behavior for random I/O, truncation, fsync/close, rename, enumeration, case, permissions and timestamps.
3. Implement bounded cache and conditional staged writes with a journal if offline/cached mode is offered.
4. Package native dependency detection, mount policy and per-user lifecycle for named distributions.

State whether flush acknowledges remote commit or durable local queuing. Reject unsupported operations and surface disconnect/authentication errors rather than claiming success.

Default to per-user mounts. `allow_other` or shared mounts require explicit permission enforcement and an ownership/threat model; one user's remote credentials must not create an unrestricted machine-wide mount.

## Acceptance

Run filesystem contract and application-save tests covering large logical files, rename races, disconnect/reconnect, full cache, crash during flush, multiple mounts, missing native dependencies and unmount with pending writes.

Compare the maintained adapter with a documented davfs2/GVfs interoperability sample before accepting the maintenance cost. Publish supported distributions and filesystem limitations.

## Sources

[libfuse](https://github.com/libfuse/libfuse) defines the native integration boundary. The protocol plan defines safe conditional transfer behavior.
