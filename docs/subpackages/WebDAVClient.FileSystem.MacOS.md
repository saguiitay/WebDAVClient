# Package plan: WebDAVClient.FileSystem.MacOS

Status: proposed platform SDK and app-extension project. Owns D-04.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [sync](WebDAVClient.Sync.md), [desktop agent](Desktop-Agent.md).

## Purpose and dependencies

Provide Finder integration through replicated File Provider domains, enumeration, stable identifiers, versions, content fetching and change signaling.

Depends on D-01 and a macOS-specific binding/packaging spike. Supported .NET/macOS native binding feasibility is a prerequisite, not something established by older Xamarin samples.

## Implementation and ownership

1. Prove container/extension communication, domain registration and lifecycle with the chosen toolchain.
2. Implement enumeration, stable IDs, item/content versions and requested content fetch.
3. Adapt outgoing changes and remote reconciliation to Apple's ownership of local copies; do not create a competing independent cache.
4. Add native sign-in integration, Keychain-backed access, app-group sharing as required and explicit authentication/error state.
5. Package signed/notarized container and extension with entitlements and compatible upgrades.

Use persistent ID/href mapping if the server cannot resolve stable IDs directly. Document limited rename fidelity and conservative reconciliation when identity cannot be established.

The extension supplies filesystem-facing behavior; the container/agent owns account enrollment and lifecycle UI. No independent server package is required.

## Acceptance

Validate chosen supported macOS versions and architectures: Finder population, fetch/edit/save/rename/delete, remote updates, offline materialized files, conflicts, revoked authentication, multiple domains, extension termination/restart, container upgrade and clean removal.

Signing, entitlements, notarization and unsynced-data-safe removal are release gates, not follow-up installer tasks.

## Sources

[Apple replicated File Provider](https://developer.apple.com/documentation/fileprovider/replicated-file-provider-extension) and the site's [macOS native drive sample](https://www.webdavsystem.com/client/examples/webdav_drive_mac/).
