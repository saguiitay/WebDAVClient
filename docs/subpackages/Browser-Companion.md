# Application/package plan: browser companion

Status: proposed small npm helper; any browser extension is separately installed and permissioned.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [bridge contract](WebDAVClient.DesktopBridge.md), [desktop agent](Desktop-Agent.md). Contributes browser UX to D-06.

## Purpose and boundaries

Provide framework-independent launch UX, installation/error guidance and capability/version handshake for the native agent. This is not initially a second complete WebDAV client or a web file browser.

A browser extension is justified only by a concrete capability; do not require broad extension permissions for ordinary protocol activation.

## Dependencies and delivery

Depends on an installed native editing path, versioned bridge/enrollment contract and explicit origin policy.

1. Build accessible Open/Edit/Reveal interactions and user-cancel/error states.
2. Integrate managed ticket issuance or native endpoint enrollment without credential-bearing launch URLs.
3. Detect/report absent or incompatible agent behavior through the approved handshake.
4. Publish helper assets and, only if needed, extension/store/native-messaging manifests independently.

Never scrape browser credential stores or assume browser cookies authorize the native process. Keep extension permissions narrow and consent explicit.

## Direct browser file management

Defer a full UI/client to a separate scoped product or ASP.NET backend-for-frontend. The async .NET package is not automatically browser-compatible.

Any Blazor/WASM or direct HTTP implementation must separately validate CORS, DAV methods/headers, exposed response headers, authentication and browser transport limitations.

## Acceptance

Exercise supported browsers with agent missing/outdated, blocked launch, absent association, cancellation, denied enrollment, expired/replayed ticket, untrusted origin and URI escaping. Errors must be accessible and actionable without declaring a failed launch successful.

Browser assets contain no native credentials and cannot request arbitrary shell execution. Version mismatch is handled explicitly.

## Sources

[Modern drive launch architecture](https://www.webdavsystem.com/ajax/next_version/), [browser cookie integration](https://www.webdavsystem.com/ajax/programming/open-doc-webpage/cookies-authentication/) and [OS file-manager launch](https://www.webdavsystem.com/ajax/programming/open-doc-webpage/opening_os_file_manager/).
