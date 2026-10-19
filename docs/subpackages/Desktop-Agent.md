# Application plan: desktop agent and installers

Status: proposed signed application product, not a NuGet package. Integrates D-03/D-04 and deployment work in D-07.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [sync](WebDAVClient.Sync.md), [Windows provider](WebDAVClient.FileSystem.Windows.md), [macOS provider](WebDAVClient.FileSystem.MacOS.md), [bridge](WebDAVClient.DesktopBridge.md), [shell](WebDAVClient.Windows.Shell.md).

## Purpose and ownership

Host account/root enrollment, user-session synchronization, protected credentials, native activation and conflict/authentication UI. Support compatible third-party DAV servers; never require this project's server.

Run per user, not as an unrestricted system service holding all credentials. Any privileged installer/driver helper has a narrow administrative interface.

## Dependencies and delivery

Depends on D-01 plus the selected native provider. Bridge activation is optional until D-06; shell extras follow the provider/agent.

1. Implement account lifecycle, secure OS credential storage, multiple roots and recovery UI.
2. Host synchronization and platform adapters with cancellation, bounded concurrency and sleep/network/reboot recovery.
3. Add versioned authenticated IPC and optional activation/working-copy editing.
4. Ship signed installers, schema migrations, component compatibility and rollback policy.

Native callbacks must not wait on interactive sign-in. Local edits survive agent termination; an expired remote lock is never presented as still protecting offline content.

## Distribution

| Platform | Deliverables |
| --- | --- |
| Windows Cloud Files | Supported signed packaged/desktop distribution, identity/COM strategy, per-user roots, activation and auto-start. |
| Windows driver backend | Separate prerequisite/version detection, administrative consent, signed-driver policy and rollback. Normal mounts may run unprivileged. |
| macOS | Signed/notarized container and File Provider extension, entitlements, app-group/Keychain setup and compatible update lifecycle. |
| Linux, if offered | Native dependency checks, distribution packages, mount configuration, activation and per-user background lifecycle. |

NuGet cannot install/register these products. Enterprise delivery may need Intune/MSIX, signed MSI alternatives, managed roots/account policy, proxies and certificate support. Keep secrets out of deployment files.

Version persistent configuration and negotiate IPC compatibility; never send an unknown destructive command to an older component.

## Uninstall and acceptance

Stop new work, warn about pending uploads, offer export/recovery, unregister roots/providers/handlers and remove credentials with consent. Delete only explicitly selected clean cache; never recursively delete the working root.

Validate signed install/update/rollback/uninstall, concurrent users/accounts, sleep/reboot, revoked credentials, stalled callbacks, pending writes and corrupted/migrating state. No failed save may appear in-sync, and users must retain access to unsynchronized content.

## Sources

[Enterprise configuration](https://www.webdavsystem.com/ajax/programming/open-doc-webpage/install/webdav-config-file/) and [Intune deployment](https://www.webdavsystem.com/ajax/programming/open-doc-webpage/install/deployment-via-intune/).
