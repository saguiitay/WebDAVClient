# Package plan: WebDAVClient.Compatibility.ITHit

Status: proposed optional NuGet package, gated by concrete demand and recorded interoperability evidence.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md) and vendor profiles in the [protocol plan](../PROTOCOL-COMPLETENESS-PLAN.md).

## Purpose and boundaries

Isolate IT Hit-specific paging/sorting, resumable upload/progress/cancel and notification behavior from core WebDAV claims. This is an interoperability adapter, not an adoption of a commercial runtime or permission to copy proprietary code.

## Dependencies and delivery

Depends on P-02/P-03/P-06/P-07 and explicit server capability/configuration.

1. Capture original wire fixtures for the exact server versions/features being supported.
2. Implement `paging` discovery and continuation/sorting separately from streaming XML enumeration.
3. Implement negotiated resume, proprietary upload-progress REPORT and CANCELUPLOAD with validated offsets, integrity and failure recovery.
4. Treat push notifications as hints to reconcile authoritative state.

Do not infer partial PUT support from GET ranges. RFC 9110 partial PUT requires explicit agreement; an incompatible server might replace the whole file.

## Acceptance

- Unsupported servers fail or use an explicitly safe alternative, never a success-shaped fallback.
- Offset mismatch, interruption, cancellation and changed validators preserve data.
- Dropped/reordered notifications cannot directly delete or overwrite local content.
- Public documentation lists proprietary behavior separately from RFC conformance.

## Sources

[Paging](https://www.webdavsystem.com/server/documentation/paging/), [resumable-upload wire behavior](https://www.webdavsystem.com/javaserver/doc/resumable_upload/how_it_works/) and [RFC 9110 section 14.5](https://www.rfc-editor.org/rfc/rfc9110.html#section-14.5).
