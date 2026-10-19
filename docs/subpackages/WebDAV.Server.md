# Package plan: WebDAV.Server

Status: proposed host-independent NuGet engine and initial provider contracts.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [detailed server plan](../SERVER-SUPPORT-PLAN.md), [shared protocol](WebDAV.Protocol.md).

## Purpose and boundaries

Implement inbound DAV semantics, provider contracts, authorization/condition coordination and operation recovery. Do not turn the outbound Client into a server mode or introduce host/native dependencies.

Keep provider abstractions with the engine initially; split them only when independent implementers need a stable separate dependency.

## Dependencies and delivery

S-01 uses P-01/P-02 wire contracts; S-02/S-03 align with property/condition/lock design. Hosting and storage adapters consume the engine independently.

1. Establish resource identity, hierarchy/content/property/lock/transaction contracts and a read-only vertical slice.
2. Coordinate ordered atomic PROPPATCH, staged writes and recursive namespace outcomes.
3. Add durable overlapping-lock semantics and multi-instance commit conditions.
4. Extend providers for quota/journal, search, versions and ACL only behind accurate capabilities.

Stable IDs differ from paths. Check authorization, tenant boundaries and conditions on every access path; lock tokens alone do not authorize a write. Recheck conditions at commit rather than only before streaming.

## Acceptance

Use the S-01 through S-05 contract/recovery matrix: exact multistatus, safe namespace operations, expired/overlapping locks, crash points, shared-store multi-process contention and accurate class advertisement.

Provider limitations must reduce the advertised profile or reject the operation, never silently emulate missing durability. No browser, desktop or ASP.NET dependency is allowed in the engine.

## Sources

[Class 1 guide](https://www.webdavsystem.com/server/documentation/creating_class_1_webdav_server/), [class 2 guide](https://www.webdavsystem.com/server/documentation/creating_class_2_webdav_server/) and [authorization guidance](https://www.webdavsystem.com/server/documentation/choosing_authentication/programming-auth/). The server plan retains the authoritative implementation details.
