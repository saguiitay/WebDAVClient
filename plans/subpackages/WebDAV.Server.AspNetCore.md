# Package plan: WebDAV.Server.AspNetCore

Status: proposed optional NuGet host adapter.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [server engine](WebDAV.Server.md), [detailed server plan](../SERVER-SUPPORT-PLAN.md).

## Purpose and dependencies

Adapt endpoint routing, DI/options, streaming HTTP requests/responses, host authentication, limits, diagnostics and health to the host-independent engine. Depends on S-01 contracts and progresses through S-04 hosting hardening.

Proposed `AddWebDav(...)` and `MapWebDav(...)` names require API review; they are not existing APIs. Storage rules and locks remain in the engine/providers.

## Implementation

1. Map non-root path bases, cancellation and host principals into request-scoped engine context.
2. Stream bodies/results with explicit body/XML/time limits and protocol errors.
3. Integrate ASP.NET Core authorization and supported bearer/legacy auth examples without inventing identity management.
4. Configure trusted proxy forwarding, origin handling, CORS/preflight, CSRF protection where cookies are used and redacted diagnostics.
5. Document Kestrel, IIS and reverse-proxy deployment, readiness, migrations and shutdown behavior.

Do not return an HTML sign-in page as successful DAV content. IIS modules/filtering must not intercept methods; test precise configuration rather than recommending globally disabled protections.

## Acceptance

Verify path base, escaping, HTTP methods, stream cancellation, proxy size/time limits, exact-origin CORS and denied/expired authentication. Passing preflight must not authorize a mutation.

Run S-04 deployment/native-client cases. Readiness blocks writes when required durability stores are unavailable; liveness never mutates user data. No desktop dependency is introduced.

## Sources

[Filesystem ASP.NET Core sample](https://www.webdavsystem.com/server/server_examples/cross_platform_asp_net_core_file_system/) illustrates hosting; the server plan specifies the required security and operational behavior.
