# Package plan: WebDAV.Testing

Status: internal helpers first; optional developer NuGet package only with external consumers.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [protocol acceptance matrix](../PROTOCOL-COMPLETENESS-PLAN.md), [server contract testing](../SERVER-SUPPORT-PLAN.md).

## Purpose and dependencies

Share original wire fixtures, fake HTTP transport, deterministic time/failure injection and reusable provider-contract tests. Do not ship a test SDK as a runtime client/server dependency.

Start alongside P-01/P-02 and S-01. A public package waits for stable contracts and genuine third-party provider/test users.

## Delivery

1. Extract useful existing unit-test helpers without forcing a new public abstraction.
2. Add multistatus/XML/condition/lock/transfer fixtures and replayable faulting streams.
3. Supply provider contracts for identity, atomic properties, writes, locks, quota and journal.
4. Separate deterministic suites, isolated server integrations and platform/native-client release gates.

Add projects to the solution and test supported target frameworks. Keep vendor samples/code out of fixtures; use original scenarios and properly licensed external tools.

## Acceptance

Fixtures expose false success, mixed statuses, lost updates, resource leaks and crash/restart inconsistencies. External conformance tools complement rather than replace RFC-ledger coverage.

No destructive tests target public demos. Network/platform prerequisites are explicit; ordinary unit tests do not require Office, a mounted driver or a cloud account.
