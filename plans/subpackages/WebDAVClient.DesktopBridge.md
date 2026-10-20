# Package plan: WebDAVClient.DesktopBridge

Status: proposed small shared schema/SDK. Owns the activation contract in D-06.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [desktop agent](Desktop-Agent.md), [browser companion](Browser-Companion.md).

## Purpose and dependencies

Define versioned activation, authenticated per-user IPC and enrollment contracts for browser-to-desktop editing. Depend on D-03 or D-04 for sync-root editing; a separate working-copy mode needs P-03/P-05/P-06.

Do not include a filesystem implementation, broad credential bridge, unrestricted localhost API or arbitrary process execution.

## Activation and trust

Use a project-owned URI scheme rather than copying another application's `davX:` registration. RFC 4709 mount documents are optional; they neither install a handler nor implement a filesystem.

A bounded launch contains a command/resource reference and, where supported, a short-lived one-use ticket. Never include passwords, bearer tokens, session cookies, executable paths or shell commands.

| Enrollment mode | Required behavior |
| --- | --- |
| Managed portal/server | Bind enrollment to trusted origin/account; redeem an audience/device-bound ticket checking user, resource, action, expiry and replay. |
| Arbitrary DAV server | Ask the user to enroll/confirm the endpoint and authenticate natively; do not assume a ticket endpoint exists. |

Validate scheme/origin, containment, account and allowed command. A custom URI launch does not authenticate its originating page; an Origin header alone is not a credential.

Prefer OS activation and authenticated per-user IPC. If loopback HTTP is necessary, bind only to loopback, authenticate each request, validate Host/Origin and reject DNS rebinding/cross-origin misuse.

Never scrape browser credential stores or automatically export cookies. Prefer native OAuth/system-browser sign-in; any unavoidable browser-extension/native-messaging compatibility flow needs explicit consent and narrow permissions.

## Editing and delivery

1. Version the schema and validate bounded commands before implementing activation.
2. Resolve an enrolled sync-root item, hydrate it and launch through OS association.
3. Observe durable local changes instead of relying on process exit; applications may reuse an existing process.
4. If working-copy mode is offered, use a protected workspace, retain validator/lease state, track replace/rename saves, upload conditionally and preserve unsynced files.

Open/Edit, Reveal Folder, Lock/Unlock and Versions are bounded actions. Printing, custom application choice and externally linked assets require separate consent/policy.

## Acceptance

Agent absent/outdated, incompatible IPC, missing association, cancellation, expired/replayed ticket, untrusted origin, escaping, revoked account, offline edits, conflict and application crash all leave actionable state without data loss or credential disclosure.

## Sources

[RFC 4709](https://www.rfc-editor.org/rfc/rfc4709.html), [browser cookie handoff](https://www.webdavsystem.com/ajax/programming/open-doc-webpage/cookies-authentication/) and [OS file-manager launch](https://www.webdavsystem.com/ajax/programming/open-doc-webpage/opening_os_file_manager/).
