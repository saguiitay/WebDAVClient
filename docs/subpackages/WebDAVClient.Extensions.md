# Package plan: WebDAVClient.Extensions

Status: proposed optional NuGet package or internal modules initially.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md) and the E-01 through E-08 portfolio in the [protocol plan](../PROTOCOL-COMPLETENESS-PLAN.md).

## Purpose and boundaries

Provide capability-gated clients for quota, SEARCH/DASL, synchronization REPORT, ACL/principals and DeltaV. Start with one extension boundary unless implementation dependencies justify separate packages.

CalDAV/CardDAV require separate domain decisions and parsers; a generic REPORT escape hatch is not full support. Ordinary progress, validators and HTTP ranges stay in the core client.

## Dependencies and delivery

Depends on P-02/P-03/P-04/P-07 results, properties, conditions and discovery.

1. Deliver quota, search and RFC 6578 synchronization with explicit negotiation and typed results.
2. Add ACL/principal discovery and individually named DeltaV feature packages.
3. Track later domain/collection/binding profiles independently and document unsupported features.
4. Publish a versioned per-RFC interoperability matrix instead of an "all extensions" claim.

Use shared XML and error models. Do not implement raw SQL search, treat ACL as dead-property text, or equate ETags with stable identity.

## Acceptance

- Missing quota differs from zero space; unauthorized search results never leak.
- Sync handles deletions, invalid tokens, retention and continuation without losing changes.
- ACL tests include protected/inherited rules and denied access paths.
- Version histories are immutable; core versioning is not advertised as full DeltaV.
- Each advertised profile passes positive/negative tests against an implementation offering it.

## Sources

The site's [search](https://www.webdavsystem.com/client/programming/search/), [sync](https://www.webdavsystem.com/client/programming/sync/) and [DeltaV](https://www.webdavsystem.com/client/programming/deltav/) guides supply scenarios. The individual RFCs and acceptance cases are enumerated in the protocol plan.
