# Package plan: WebDAVClient.Extensions.DependencyInjection

Status: proposed optional NuGet package.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md) and P-08 in the [protocol plan](../PROTOCOL-COMPLETENESS-PLAN.md).

## Purpose and boundaries

Provide client factories, fluent configuration, DI/options registration and opt-in diagnostics/resilience integration using standard .NET abstractions. Ordinary client users must not acquire hosting dependencies.

Retain existing constructors and bearer providers. Do not assume the roadmap's builder APIs already exist or introduce an independent authentication system.

## Dependencies and delivery

Depends on the P-08 design and P-03/P-06/P-07 request conditions, replay and origin policy.

1. Define factory/builder ownership and named-client/account configuration.
2. Implement options validation and HttpClient lifetime integration.
3. Add redacted structured diagnostics and explicitly selected retry policies.
4. Document registration, cancellation, disposal and migration from constructors.

Never retry ambiguous MOVE/LOCK or unsafe uploads indiscriminately. A replay needs a fresh request and a replayable body; caller-owned clients/wrappers remain caller-owned.

## Acceptance

- Concurrent clients do not share credentials or mutable request headers accidentally.
- Initialization and retries respect cancellation; invalid configuration fails clearly.
- Non-seekable bodies and lost responses exercise explicit replay/reconciliation behavior.
- Diagnostics omit credentials, cookies, reusable lock tokens and user content by default.
- Removing the package leaves the base client usable with no hosting dependency.
