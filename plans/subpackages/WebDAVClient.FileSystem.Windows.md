# Package plan: WebDAVClient.FileSystem.Windows

Status: proposed Windows-targeted NuGet SDK. Owns D-02 feasibility and the provider portion of D-03.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [sync](WebDAVClient.Sync.md), [desktop agent](Desktop-Agent.md), [shell](WebDAVClient.Windows.Shell.md).

## Purpose and dependencies

Integrate Cloud Files/WinRT interop, callbacks, placeholders and sync-root registration with the durable sync engine. Production depends on D-01/D-02 and P-05 for integrated locking.

A Cloud Files root is a local directory-based sync experience, not a new filesystem volume or drive letter. It uses Microsoft's built-in Cloud Filter driver; no new third-party driver is needed for this path.

## D-02: feasibility gate

Run alongside protocol completion after P-01:

| Experiment | Required decision |
| --- | --- |
| Register/populate/hydrate/edit/rename/delete/unregister a root. | Supported OS builds, filesystem constraints and package identity. |
| Stress async callback completion/cancellation, GC retention, handles and process exit. | Interop approach and lifetime contract. |
| Compare sync-root UX with WinFsp and, only if necessary, Dokan. | Whether actual drive letters justify a separate backend. |
| Install/uninstall a minimal signed shell/provider application. | COM bitness, registration and installer requirements. |
| Capture Office and non-Office save patterns. | Local/remote locks and replacement behavior. |

Cloud Files API history begins with Windows 10 version 1709; that is not a support promise for old releases. Initially target supported Windows 11 x64/ARM64, verify APIs/NTFS/package constraints, and explicitly decide Windows 10/LTSC support.

Do not choose ProjFS as a cloud-sync substitute: its high-speed projection focus does not provide the required status/progress experience. The spike must reject unsuitable approaches, not merely show a directory.

## D-03: implementation

1. Register per-user roots and connect account state through the agent.
2. Populate placeholders and hydrate requested ranges with cancellation and bounded concurrency.
3. Support pin/unpin, offline state and clean dehydration without losing dirty content.
4. Persist local namespace/content changes and reconcile remote updates through Sync.
5. Surface platform progress/state plus agent conflict/authentication UI; recover from sleep, reboot and termination.

Keep callbacks short and obey completion/timeouts. Retain callbacks/handles until work completes or is cancelled. Do not block Explorer on interactive sign-in or own networking in a shell extension.

Local share modes, local file locks and remote DAV leases differ. Define write-open/replace/renew/release policy; test repeated opens, crashes and overnight editing.

## Acceptance

- D-02 produces install/uninstall proof, callback cancellation/leak stress evidence and a platform/license decision.
- Reading hydrates; pinned content opens offline; placeholders appear before content fetch.
- Writes survive crashes; remote updates never overwrite dirty local content.
- Failed saves are not marked in-sync; errors are actionable.
- Multiple roots/accounts, revocation, sleep/reboot and root removal preserve isolation and data.

## Sources

[Microsoft Cloud Files](https://learn.microsoft.com/en-us/windows/win32/cfapi/build-a-cloud-file-sync-engine), [ProjFS](https://learn.microsoft.com/en-us/windows/win32/projfs/projected-file-system), [Windows drive sample](https://www.webdavsystem.com/client/examples/webdav_drive/) and [architecture comparison](https://www.webdavsystem.com/ajax/next_version/comparison/).
