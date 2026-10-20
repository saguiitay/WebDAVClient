# Package plan: WebDAV.Protocol

Status: proposed shared project; publish as NuGet only after its contracts stabilize.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md), [protocol completion](../PROTOCOL-COMPLETENESS-PLAN.md), [server architecture](../SERVER-SUPPORT-PLAN.md).

## Purpose and boundaries

Share qualified names, namespace-aware XML values, multistatus, DAV errors and condition/header models between the outbound client and inbound server. Do not include ASP.NET Core, storage, native APIs, business authorization or retry policy.

Extract only when both client and server consume the code. A reusable internal project is sufficient initially; avoid publishing speculative abstractions or breaking the existing client's lightweight dependency footprint.

## Dependencies and delivery

Depends on P-01/P-02 requirements and wire fixtures. Coordinate conditions with P-03 and provider-independent server contracts with S-01. Retain existing public client types where possible and review compatibility before exposing shared replacements.

1. Identify concrete shared wire requirements in the two consumers.
2. Extract hardened XML/status/error and property representations with original fixtures.
3. Add full DAV If parsing/formatting without embedding authorization decisions.
4. Document ownership, size/depth limits and versioning; publish only after a real external consumer needs the package.

## Acceptance

- Both consumers use the same tested representations without depending on one another.
- Namespaces, mixed XML content, resource/propstat statuses and opaque tokens retain their meaning.
- Malformed/oversized XML fails explicitly; DTDs and external resolution remain disabled.
- Dependency inspection confirms no hosting, cloud, native or database dependency.

## Sources

[RFC 4918](https://www.rfc-editor.org/rfc/rfc4918.html) and [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html) define the wire semantics. The protocol plan defines the detailed P-01/P-02/P-03 acceptance fixtures.
