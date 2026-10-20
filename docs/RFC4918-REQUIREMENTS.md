# RFC 4918 client requirement ledger

Review date: 2026-10-21. Scope: upcoming 3.0.0 source, principally `DavClient`,
`Protocol`, and the explicit `DavLockLease` lifecycle. This is a durable
implementation/evidence reference, **not** a claim of complete RFC conformance,
a server certification, or a statement that 3.0.0 has been published.
Core implementation anchors: `dd9b7f0` (managed lifecycle) and `a632d04`
(core wire/XML/timeout corrections).

Normative source: [RFC 4918](https://www.rfc-editor.org/rfc/rfc4918.html),
especially sections 4–18. Requirements below are paraphrased and grouped by
normative clause. Example subsections illustrate the enclosing requirement rather
than introducing independent completion claims. Current HTTP transport semantics
also depend on .NET `HttpClient` and RFC 9110; this ledger does not certify every
HTTP feature or injected handler configuration.

## Status and evidence rules

- **Implemented**: the client can express/interpret the stated requirement;
  the cited deterministic tests cover that behavior.
- **Partial**: useful support exists, but the limitation in the row remains.
- **Server responsibility**: the protocol delegates that invariant to the server.
  Sending a correct request is not evidence that a server enforces it.
- **Unsupported**: no dedicated public operation/policy is provided.

All implemented rows still need the relevant independent-server interoperability
evidence before an end-to-end conformance claim. A successful method call,
advertisement, parser test, or coverage percentage cannot establish server
atomicity, persistence, authorization, or lock compatibility.

### Evidence anchors

All files below are in `WebDAVClient.UnitTests`. Tests run against both `net8.0`
and `net10.0`. Handler/loopback tests are not independent server implementations.

| ID | Deterministic evidence |
| --- | --- |
| E-XML | `DavProtocolTests`: `Xml_values_retain_mixed_content_namespace_language_attributes_and_defensive_copies`, `Malformed_or_wrong_structure_is_not_a_successful_empty_snapshot`, `Inner_lookalikes_remain_property_content_not_resource_identity_or_outcomes`, `Configured_limits_fail_explicitly`. |
| E-CONTEXT | `DavProtocolEdgeTests`: `Streamed_and_buffered_properties_preserve_inherited_xml_context`, `Structured_property_roundtrip_survives_proppatch_serialization_without_flattening`, `Unicode_values_and_declared_encodings_survive_both_xml_read_paths`, `Snapshots_are_read_only_and_xml_edits_do_not_mutate_sibling_results`. |
| E-PROP | `DavClientTests`: `Propfind_selects_properties_and_retains_root_and_qualified_outcomes`, `Proppatch_keeps_repeated_ordered_instructions_and_supports_writable_dav_properties`; `DavProtocolTests`: `Property_statuses_are_exact_regardless_of_wire_order`, `Typed_codecs_distinguish_invalid_missing_empty_and_denied_values`. |
| E-STRUCTURE | `DavFailureContractTests`: `Ambiguous_structural_outcomes_fail_in_buffered_and_streaming_paths`, `Empty_listing_can_complete_but_empty_mutation_cannot_confirm_a_write`; `DavProtocolEdgeTests`: `Malformed_status_line_cannot_confirm_a_successful_resource`, `Root_and_resource_extension_errors_and_multiple_hrefs_are_retained`. |
| E-IF | `DavProtocolTests`: `Conditions_preserve_tagging_alternatives_negation_and_validate_every_origin`, `Invalid_arguments_fail_before_network_io`; `DavCoreCompletenessTests`: `If_conditions_always_prevent_non_dav_proxy_cache_hits_without_losing_custom_directives`. |
| E-NAMESPACE | `DavClientTests`: `Namespace_operations_have_no_spurious_body_and_preserve_rejections`, `Recursive_mutation_failure_retains_successful_and_denied_resources`; `DavFailureContractTests` covers operation-specific success confirmation, missing patch outcomes, and invalid namespace multistatus shapes. |
| E-LOCK | `DavClientTests`: `Lock_observations_preserve_granted_timeout_and_structured_owner`, `Refresh_has_no_request_body_and_rejects_missing_response_confirmation`, `Recursive_lock_failure_is_not_a_granted_tree_lock`; `DavFailureContractTests` covers malformed lock XML and omitted metadata. |
| E-LEASE | `DavLockLeaseTests`: acquisition header/token/scope/depth checks; finite/infinite/omitted timeouts; conservative monotonic deadlines; explicit renewals; lost/changed confirmation; cancellation; serialization; at-most-one release attempt. See [lifecycle reference](LOCK-LIFECYCLE.md). |
| E-CORE | `DavCoreCompletenessTests`: `Lock_timeout_supports_the_entire_unsigned_seconds_range`, `Empty_locked_resources_accept_204_without_acquiring_a_body_and_keep_response_owned`, `No_content_cannot_be_used_as_appendable_or_contradictory_content`, `Capabilities_keep_compliance_classes_and_coded_extension_uris_with_embedded_commas`, `Lock_kind_decoding_ignores_extensions_without_promoting_lookalikes_or_ambiguous_kinds`, `Every_core_condition_preserves_qualified_details_without_following_error_hrefs`. |
| E-HTTP | `DavClientTests`: `Downloads_keep_metadata_and_conditional_outcomes_without_fake_streams`, `Download_validates_closed_open_and_suffix_ranges`, `Unsafe_range_responses_are_never_returned_as_appendable_content`, `Upload_ownership_is_explicit_and_nonseekable_streams_work`, `Seekable_upload_uses_remaining_length_and_does_not_replay_after_transport_failure`. |
| E-LIFETIME | `DavLifetimeTests`, `DavLoopbackTests`, and `DavResourceProjectionTests`; `DavClientTests`: `Streaming_completion_requires_full_enumeration_and_error_tail_cannot_certify_a_snapshot`, `Early_break_releases_listing_but_does_not_certify_completion`. |
| E-PATH | `DavProtocolTests`: `Paths_keep_identity_and_explicit_resolution_rules`, `Conditions_preserve_tagging_alternatives_negation_and_validate_every_origin`; `DavClientTests`: `Direct_requests_do_not_discover_or_mutate_connection_and_keep_transport_caller_owned`. |
| E-OPTIONS | `DavClientTests`: `Options_are_evidence_and_preserve_unknown_tokens`; E-CORE coded-URI test; `DavFailureContractTests` missing-advertisement tests. |
| E-VALIDATION | `DavCoreValidationTests`: positive/negative resource URI grammar, combined escaped Unicode/reserved octets, origin and dot-segment fixtures; duplicate/mixed href forms; core singleton and element-only-content checks in buffered and streamed paths; REPORT/legacy boundary checks; mandatory LOCK response confirmation; lock URI/cardinality checks, header OWS, confirmed bare-token compatibility and contradictory/ambiguous token rejection. |

### Core receive-validation boundary

Core `DavClient` multistatus operations and streamed listings validate structural
resource hrefs as escaped same-origin HTTP(S) absolute URIs or absolute paths
(with optional query), reject fragments/dot segments, require one format within
the response, and reject repeated resource href wire values. Escaped separators
and escaped Unicode remain opaque and are not decoded into different paths.
Wire path/query grammar is checked directly: .NET URI IRI normalization can
misclassify valid combined escaped Unicode, percent and hash octets, so
`Uri.IsWellFormedOriginalString()` is not used to reject those response identities.
PROPPATCH target confirmation also treats uppercase/lowercase escape hex digits
as equivalent under RFC 3986 section 6.2.2.1. It does not case-fold path/query
text, decode escaped separators, or recursively decode percent-encoded percent
signs; the original returned href remains available unchanged.
The comparison retains .NET's safe unreserved-octet canonicalization, such as
`%41` versus `A` and `%7E` versus `~`, in both path and query.
Distinct status-level aliases remain valid. A listing cannot become complete if
a later member, singleton or document tail is invalid. Validation retains href
identity strings for uniqueness, not prior resource/property DOMs, and remains
bounded by the existing total XML limit.

Known core response/propstat/location, error and lock singleton constraints are
enforced after ignoring unexpected extension elements. Element-only containers
reject non-whitespace text. Required URI-container hrefs are singular and scalar.
Arbitrary property/owner XML and error-condition detail are not recursively
treated as structural resource links. Redirect locations can identify another
origin or URI scheme and are retained, never followed automatically.

These extra core checks are deliberately not imposed on generic REPORT
cardinalities or legacy projection. REPORT profiles still interpret their own
responses. An empty PROPFIND multistatus remains permitted by the core
`response*` container; namespace mutations and recursive LOCK failures require
their operation-specific nonempty outcomes.

Successful acquisition requires one valid Lock-Token header matching exactly
one lockdiscovery observation. Servers must bracket that header under section
10.5; the client tolerates a single bare absolute token only with that exact
body confirmation, preserving the raw wire header separately from the canonical
bracketed result token. This interoperability tolerance is not a claim that the
server's header conforms. Successful refresh requires HTTP 200 and its
lockdiscovery body, while the refresh request remains bodyless. Missing refresh
confirmation is no longer tolerated. Optional discovery fields remain optional;
in particular, omission of lockroot is tolerated because section 14.12 makes its
inclusion a SHOULD, not an unconditional MUST. When present, lockroot has exactly
one scalar href. Its raw text is metadata, not a request target or ownership
condition: unescaped filenames returned by servers such as SabreDAV 4.7.0 are
retained rather than invalidating an otherwise confirmed lock. Correct escaping
of that server-produced metadata remains a server obligation. Managed leases
additionally confirm token/scope/depth identity.

## Resource/property model: sections 4–5

| Ledger ID / clause | Client obligation or server invariant | Status / implementation | Evidence and remaining boundary |
| --- | --- | --- | --- |
| R-4.1 / 4.1 | Identify properties by expanded XML name; distinguish live/dead server behavior. | Implemented: `PropertyName`, arbitrary `DavXmlValue`, qualified lookup. | E-XML, E-PROP. Whether a property is live/protected is server-defined; no client namespace-wide protection assumption. |
| R-4.2 / 4.2 | Do not equate property outcomes with HTTP response headers. | Implemented: exact property results and detached HTTP headers remain separate. | E-PROP, E-HTTP. No inferred equality or automatic reconciliation. |
| R-4.3 / 4.3 | Preserve property XML, mixed content, attributes, namespace and language context. | Implemented: defensive element copies and structured PROPPATCH serialization. | E-XML, E-CONTEXT. Lexical prefix/attribute order and byte-for-byte serialization are not promised. |
| R-4.4 / 4.4 | Use namespace-aware property identities, not prefix strings. | Implemented: expanded names and namespace-aware reader/writer. | E-XML, E-PROP. Application-defined codecs remain application policy. |
| R-4.5 / 4.5 | Do not invent an authoring-source discovery mechanism for dynamically generated representations. | Unsupported: no source-resource inference. | A GET URL is not assumed to identify an editable source; server/application knowledge is required. |
| R-5.1 / 5.1 | Preserve URI identity and distinguish hierarchy/bindings from local filesystem paths. | Implemented request construction; server namespace consistency remains a server responsibility. | E-PATH, E-NAMESPACE. No local path interpretation, binding alias inference, or same-origin escape. |
| R-5.2 / 5.2 | Discover collections through successful `DAV:resourcetype`, not just trailing slashes. | Implemented: typed resource-type list; missing/failed/empty outcomes remain distinguishable. | E-PROP, `DavResourceProjectionTests`. No proof that server collection membership is consistent. |

## Locks and write-lock interactions: sections 6–7

| Ledger ID / clause | Client obligation or server invariant | Status / implementation | Evidence and remaining boundary |
| --- | --- | --- | --- |
| R-6.1 / 6.1 | Treat locks as server state rather than local ownership guarantees. | Implemented: detached observations and conservative explicit lease state. | E-LOCK, E-LEASE. No distributed-mutex or persistence guarantee. |
| R-6.2 / 6.2 | Express exclusive and shared write locks without assuming shared support. | Implemented: `LockOptions.Scope`; supportedlock codec. | E-LOCK, E-CORE, E-LEASE. Shared-lock conflict enforcement is server responsibility. |
| R-6.3 / 6.3 | Do not require locking support for every DAV resource. | Implemented: non-locking operations do not perform preliminary lock discovery. | E-PATH, E-OPTIONS. A class-1/class-3 advertisement is not proof of class 2. |
| R-6.4 / 6.4 | Keep lock token possession distinct from authentication/privileges. | Implemented separation: request conditions versus transport authentication. | E-IF, E-LEASE. Authorization decisions and lock-creator privileges are server responsibility. |
| R-6.5 / 6.5 | Submit opaque absolute lock-token URIs without deriving meaning from their scheme/value. | Implemented: structured state token and bracketed UNLOCK; managed acquisition cross-checks token identity. | E-IF, E-LOCK, E-LEASE. Server-generated uniqueness cannot be established locally. |
| R-6.6 / 6.6 | Treat requested timeouts as preferences, not confirmed grants or guarantees. | Implemented: finite/infinite/unspecified are distinct; lease time is monotonic and conservative. | E-LOCK, E-LEASE, E-CORE. `Second-0` is represented as an immediately expired managed observation. |
| R-6.7 / 6.7 | Discover offered scope/type through `DAV:supportedlock`. | Implemented core write-lock codec; raw XML remains available. | E-PROP, E-CORE. No automatic acquisition fallback or unknown lock-type implementation. |
| R-6.8 / 6.8 | Read all active locks; do not assume discovery reveals every owner's token. | Implemented: ordered `LockDiscovery` observations, optional token/owner/timeout/root. | E-LOCK, E-PROP. Hidden server metadata is not fabricated. |
| R-7.1 / 7.1 | Permit lock conditions on property writes. | Implemented: `UpdatePropertiesAsync` accepts structured conditions. | E-PROP, E-IF. Server enforcement on protected/live properties remains server responsibility. |
| R-7.2 / 7.2 | Combine validators and lock assertions to avoid blind lost updates. | Implemented: HTTP validators plus DAV token/ETag AND-lists; lease helper optionally adds ETag. | E-IF, E-LEASE. No implied representation version merely from lock acquisition. |
| R-7.3 / 7.3 | Support LOCK on an unmapped resource, without assuming unlock deletes the created empty resource. | Implemented wire request and 200/201 outcomes; empty-resource GET accepts 204. | E-LOCK, E-CORE. Server creation/persistence behavior needs interoperability evidence; lease release does not DELETE. |
| R-7.4 / 7.4 | Submit tokens for affected collection bindings and depth-infinity descendants as needed. | Implemented: tagged multi-resource conditions, LOCK depth 0/infinity. | E-IF, E-NAMESPACE. No automatic discovery of all affected locks or bindings. |
| R-7.5 / 7.5 | Submit lock tokens through DAV `If`, not ordinary HTTP validators. | Implemented: tagged/untagged alternatives, conjunction and negation. | E-IF, E-LEASE. Callers remain responsible for the complete token set for an operation. |
| R-7.6 / 7.6 | Express source/destination/parent assertions for COPY/MOVE; do not assume locks are copied/moved. | Implemented: same-origin destinations and tagged conditions. | E-IF, E-NAMESPACE. Binding changes, inherited locks and dead-property preservation are server responsibilities. |
| R-7.7 / 7.7 | Refresh explicitly; do not treat arbitrary requests as guaranteed renewal. | Implemented: one-token bodyless `RefreshLockAsync`, explicit `DavLockLease.RenewAsync`. | E-LOCK, E-LEASE. No background renewal or multi-token refresh (the latter is forbidden by 9.10.2). |

## General handling: section 8

| Ledger ID / clause | Requirement | Status / implementation | Evidence and remaining boundary |
| --- | --- | --- | --- |
| R-8.1 / 8.1 | Preserve the server's actual error outcome rather than guessing precedence or replaying. | Implemented result/exception distinction. | E-NAMESPACE, E-HTTP. Authentication/authorization/precondition evaluation order is server responsibility. |
| R-8.2 / 8.2 | Process XML namespaces and supported encodings without unsafe entity resolution. | Implemented async XML reader, UTF-8 requests, UTF-8/UTF-16 response tests, DTD prohibition. | E-XML, E-CONTEXT. No DTD validation or external entity retrieval. |
| R-8.3 / 8.3 | Escape request URLs correctly; validate structural response hrefs without following them implicitly. | Implemented core href grammar, percent escaping, origin, dot-segment, uniform-format and repeated-value checks. | E-PATH, E-VALIDATION. Property/owner/error-detail hrefs are data, not implicit request targets; redirected locations are not followed. |
| R-8.4 / 8.4 | Provide required method bodies and avoid inventing bodies for bodyless operations. | Implemented PROPFIND/PROPPATCH/LOCK XML; bodyless MKCOL/DELETE/MOVE/COPY/refresh/UNLOCK. | E-PROP, E-NAMESPACE, E-LOCK. Optional empty-body PROPFIND shorthand is not necessary; explicit allprop is sent. |
| R-8.5 / 8.5 | Keep HTTP validators/dates/representation metadata available independently from DAV XML. | Implemented typed request validators and detached result headers. | E-HTTP, E-IF. Injected HTTP transport remains responsible for general HTTP behavior. |
| R-8.6 / 8.6 | Preserve entity-tag opacity and weak/strong distinctions. | Implemented headers and typed property text; range validators reject weak ETags. | E-PROP, E-HTTP, E-IF. No ETag generation, content hash inference, or conversion of weak tags to strong. |
| R-8.7 / 8.7 | Interpret DAV XML error bodies even on failed HTTP responses. | Implemented bounded optional XML error reading and qualified condition retention. | E-CORE, E-LIFETIME. Non-XML response bodies are not exposed as structured DAV errors. |
| R-8.8 / 8.8 | Do not reuse validators as if namespace changes preserve representation identity. | Implemented low-level policy: no validator cache or implicit replay. | E-HTTP, E-PATH. Actual validator changes across namespace operations are server responsibility. |

## Operations: section 9

| Ledger ID / clause | Requirement | Status / implementation | Evidence and remaining boundary |
| --- | --- | --- | --- |
| R-9.1a / 9.1 | Express allprop, named prop, propname and allprop+include with Depth 0/1/infinity. | Implemented `PropFindOptions`, including explicit finite-depth choice. | E-PROP. No claim that allprop returns every live property. |
| R-9.1b / 9.1.1–9.1.2 | Distinguish per-property missing/denied/error outcomes from an empty successful observation. | Implemented exact ordered propstat status groups and missing-property distinction. | E-PROP, E-STRUCTURE. Typed decoding errors are distinct from server rejection. |
| R-9.2a / 9.2 | Preserve document order and repeated set/remove instructions in one PROPPATCH request. | Implemented `DavPropertyChange` and XML values; writable DAV properties permitted. | E-PROP, E-CONTEXT. No blanket rejection of the DAV namespace. |
| R-9.2b / 9.2–9.2.1 | Require outcomes for every requested property and expose failure/dependency codes. | Implemented target/outcome checks and exact statuses. | E-STRUCTURE, E-NAMESPACE. Server rollback atomicity cannot be proven from a response; mixed contradictory server outcomes are retained, not automatically repaired. |
| R-9.3 / 9.3 | Create collections using MKCOL, not PUT; do not synthesize missing ancestors. | Implemented bodyless `CreateCollectionAsync`; 201 is completion. | E-NAMESPACE. Extended MKCOL bodies are unsupported; 409/415/507 are inspectable outcomes. |
| R-9.4 / 9.4 | Keep collection representation GET/HEAD distinct from membership PROPFIND. | Implemented separate operations; no HTML listing inference. | E-HTTP, E-PROP, E-PATH. Server-defined collection GET content is not parsed as membership. |
| R-9.5 / 9.5 | POST semantics for collections are server-defined. | Unsupported dedicated operation. | Caller-owned HttpClient can be used separately; no claim of a generic DAV collection POST contract. |
| R-9.6 / 9.6–9.6.1 | DELETE collections with infinite-depth semantics and retain recursive failures. | Implemented bodyless DELETE, no conflicting Depth option, resource-level 207 outcomes. | E-NAMESPACE. No automatic retries or cleanup mutation after partial failure. |
| R-9.7 / 9.7.1–9.7.2 | PUT full representations with media type and conditions; do not assume collection PUT semantics. | Implemented `UploadAsync`, explicit stream ownership and conditional headers. | E-HTTP, E-IF. No standard resumable/partial PUT is claimed; parent creation remains server responsibility. |
| R-9.8a / 9.8, 9.8.3 | COPY requires Destination; collection depth is only 0/infinity. | Implemented `CopyAsync` and depth validation. | E-NAMESPACE, E-IF. Cross-origin COPY is intentionally unsupported by the origin policy. |
| R-9.8b / 9.8.1–9.8.2 | COPY representation/dead properties and maintain live-property behavior where possible. | Server responsibility; one COPY request is issued. | Client retains exact failures. No GET/PUT fallback that would silently drop metadata. |
| R-9.8c / 9.8.4–9.8.5 | Express overwrite policy and interpret 201/204/207/412 distinctly. | Implemented explicit Overwrite; safe default F. | E-NAMESPACE. Server default is T, but client always sends its explicit policy. |
| R-9.9a / 9.9, 9.9.2 | MOVE requires Destination and recursive collection semantics, without COPY/DELETE emulation. | Implemented `MoveAsync`; no non-infinite depth option. | E-NAMESPACE. Server consistency/atomicity remains unproven locally. |
| R-9.9b / 9.9.1, 9.9.3–9.9.4 | Preserve properties and replacement semantics while exposing affected member failures. | Implemented request/result support; property/binding preservation is server responsibility. | E-NAMESPACE. No automatic merging, compensating mutation, or foreign destination. |
| R-9.10a / 9.10.1 | Acquire with lockinfo XML and interpret lockdiscovery plus new Lock-Token. | Implemented required body and single valid acquisition header; managed leases additionally match token/scope/depth observations. | E-LOCK, E-LEASE, E-VALIDATION. Low-level results retain all advertised observations rather than assigning ownership to every shared lock. |
| R-9.10b / 9.10.2 | Refresh exactly one token with no request body and interpret returned lockdiscovery. | Implemented HTTP 200 and mandatory response lockdiscovery confirmation. | E-LOCK, E-LEASE, E-VALIDATION. A bodyless successful response is malformed, never an assumed renewal. |
| R-9.10c / 9.10.3 | LOCK depth only 0/infinity; recursive 207 failure is not a granted tree lock. | Implemented depth validation and lock-specific success check. | E-LOCK. Actual all-or-nothing server lock acquisition requires independent-server evidence. |
| R-9.10d / 9.10.4 | Newly locked unmapped resources can be empty and GET may return 204. | Implemented full GET 204 as an empty stream; range 204 and contradictory length/range metadata are rejected. | E-CORE. Response ownership is retained until result disposal. |
| R-9.10e / 9.10.5–9.10.6 | Distinguish lock compatibility failure and confirmed 200/201 acquisition. | Implemented result/status/error retention; compatibility matrix enforcement is server responsibility. | E-LOCK, E-CORE. No local exclusivity promise or reacquisition fallback. |
| R-9.11 / 9.11 | UNLOCK uses Lock-Token and confirms release with 204. | Implemented low-level operation; managed helper makes one release attempt. | E-NAMESPACE, E-LEASE. Resource deletion is not coupled to unlocking. |

## Headers and statuses: sections 10–12

| Ledger ID / clause | Requirement | Status / implementation | Evidence and remaining boundary |
| --- | --- | --- | --- |
| R-10.1 / 10.1 | Observe DAV compliance and extension advertisements without dropping unknown values. | Implemented resource-scoped `DavCapabilities.DavTokens`, preserving coded URL commas. | E-OPTIONS, E-CORE. No permanent/global support cache or inference of authorization. |
| R-10.2 / 10.2 | Restrict method-specific Depth values. | Implemented typed subset validation; MOVE/DELETE inherit their method semantics. | E-PROP, E-NAMESPACE, E-LOCK. REPORT depth is profile-defined, outside core. |
| R-10.3 / 10.3 | Send an absolute Destination with COPY/MOVE. | Implemented same-origin URI resolution and source/destination difference check. | E-PATH, E-NAMESPACE. Foreign-origin destinations intentionally unsupported. |
| R-10.4a / 10.4.1–10.4.4 | Encode tagged/no-tag lists, AND, OR, Not, state tokens and entity tags without mixing forms. | Implemented structured immutable condition model. | E-IF. Evaluation is server responsibility; DAV ETag comparison is not replaced with HTTP If-Match semantics. |
| R-10.4b / 10.4.5 | Prevent non-DAV-aware proxies from satisfying an If-conditioned request from cache. | Implemented mandatory `Cache-Control: no-cache` and `Pragma: no-cache` for DAV If requests, preserving other directives. | E-CORE. Caller-provided cache headers cannot remove no-cache. |
| R-10.5 / 10.5 | Bracket the bare token for UNLOCK; preserve acquisition header evidence. | Implemented request validation, canonical confirmed acquisition token and separate raw result header. | E-NAMESPACE, E-LEASE, E-VALIDATION. Single bare response-header compatibility requires exact lockdiscovery confirmation; tokens are never treated as credentials for another origin. |
| R-10.6 / 10.6 | Use T/F Overwrite independently of request-URI HTTP validators. | Implemented typed policy plus separate If-Match/If-None-Match and tagged destination conditions. | E-NAMESPACE, E-IF. No mistaken assumption that source If-Match protects the destination. |
| R-10.7 / 10.7 | Send ordered LOCK timeout preferences only on LOCK, bounded by the unsigned wire range. | Implemented finite/infinite preferences and protected raw Timeout header. | E-LOCK, E-IF, E-CORE. The full unsigned seconds range, including zero and 2^32-1, is covered. |
| R-11.1 / 11.1 | Interpret 207 body members instead of equating outer 2xx with operation success. | Implemented resource/property outcomes and operation-specific `EnsureSucceeded`. | E-STRUCTURE, E-NAMESPACE. Extension REPORT profiles supply their own member semantics. |
| R-11.2 / 11.2 | Retain 422 separately from XML syntax failures. | Implemented numeric server outcomes; malformed XML throws with response context. | E-PROP, E-STRUCTURE. No mutation replay on semantic rejection. |
| R-11.3 / 11.3 | Retain 423 and its lock conditions. | Implemented. | E-LOCK, E-CORE. No implicit lock acquisition. |
| R-11.4 / 11.4 | Retain 424 dependency failures rather than mapping them to missing properties. | Implemented exact statuses. | E-PROP, E-NAMESPACE. No fabricated descendant outcomes for error-minimized responses. |
| R-11.5 / 11.5 | Retain 507 storage failure. | Implemented exact status results. | E-PROP. No automatic repeated storage mutation. |
| R-12.1 / 12.1 | Distinguish 412 conditional failure from general conflict and transport failure. | Implemented exact response code, separate HTTP/DAV conditions. | E-HTTP, E-LEASE. No optimistic-validator removal on retry. |
| R-12.2 / 12.2 | Retain 414 instead of automatically splitting/changing the resource URL. | Implemented generic numeric status retention; no special recovery. | Generic result code path; no dedicated 414 conformance fixture yet. |

## Multistatus and XML definitions: sections 13–14

| Ledger ID / clause | Requirement | Status / implementation | Evidence and remaining boundary |
| --- | --- | --- | --- |
| R-13 / 13 | Interpret response-level status versus propstats, retaining each href/group/outcome. | Implemented strict core shape, singleton, href and element-only-content checks with ordered detached results. | E-STRUCTURE, E-PROP, E-VALIDATION. REPORT profile cardinalities and arbitrary property XML are not constrained as core resource structures. |
| R-13.1 / 13.1 | Do not treat Location header as a redirect target for every member. | Implemented evidence-only headers and member locations. | E-STRUCTURE. No implicit follow-up request. |
| R-13.2 / 13.2 | Retain per-resource redirect location and status without automatically following it. | Implemented `DavResourceResponse.Location`. | E-STRUCTURE. Automatic redirected-child traversal is unsupported. |
| R-13.3 / 13.3 | Preserve internal statuses even when they are not the expected success code. | Implemented numeric parsing and success validation. | E-PROP, E-STRUCTURE. Parser preserves informational codes rather than certifying them as valid server behavior. |
| R-14.1 / 14.1 activelock | Read structured active locks. | Implemented core write-lock kinds, singleton/cardinality and element-only-content validation. | E-LOCK, E-CORE, E-LEASE, E-VALIDATION. Unknown extensions are ignored; optional private metadata is not fabricated. |
| R-14.2 / 14.2 allprop | Generate the allprop selector. | Implemented. | E-PROP. |
| R-14.3 / 14.3 collection | Decode DAV collection marker while preserving other resource types. | Implemented. | E-PROP. |
| R-14.4 / 14.4 depth | Decode lock depth and validate requested method depth. | Implemented. | E-LOCK, E-CORE. |
| R-14.5 / 14.5 error | Retain qualified condition XML and nested detail. | Implemented. | E-CORE, E-STRUCTURE. |
| R-14.6 / 14.6 exclusive | Generate/decode exclusive scope. | Implemented; extension siblings ignored. | E-LOCK, E-CORE, E-LEASE. |
| R-14.7 / 14.7 href | Preserve href text and validate its structural resource context. | Implemented core HTTP(S)/absolute-path grammar, format/origin/escaping checks and resource uniqueness. | E-STRUCTURE, E-PATH, E-VALIDATION. General dead-property href text is not promoted to structural resource identity. |
| R-14.8 / 14.8 include | Generate allprop include. | Implemented. | E-PROP. |
| R-14.9 / 14.9 location | Retain one member location with one scalar href. | Implemented singleton/cardinality validation; locations remain evidence only. | E-STRUCTURE, E-VALIDATION. Other-origin and non-HTTP redirect URI schemes are not automatically followed. |
| R-14.10 / 14.10 lockentry | Decode supported core scope/type pairs. | Implemented; invalid entries yield typed decoding errors. | E-PROP, E-CORE. |
| R-14.11 / 14.11 lockinfo | Generate scope/type/optional owner acquisition XML. | Implemented. | E-LOCK. |
| R-14.12 / 14.12 lockroot | Retain lockroot without following it. | Implemented optional singleton with one scalar href when present; raw metadata text is retained without URI reinterpretation. | E-LOCK, E-LEASE, E-VALIDATION. Correct server URI escaping is not a prerequisite for confirming the independent token grant; omission is tolerated because inclusion is a SHOULD in this clause. |
| R-14.13 / 14.13 lockscope | Decode one known core scope, ignoring unexpected extensions. | Implemented. | E-CORE. Duplicate recognized kinds or foreign lookalikes cannot establish a core scope. |
| R-14.14 / 14.14 locktoken | Retain optional discovery token; require matching acquisition identity for managed lifecycle. | Implemented with low-level discovery tolerance. | E-LOCK, E-LEASE. |
| R-14.15 / 14.15 locktype | Generate/decode the core write type. | Implemented; unknown XML is not a core lock kind. | E-CORE. |
| R-14.16 / 14.16 multistatus | Require DAV root, bound parsing, retain root XML, complete streaming validation. | Implemented. | E-XML, E-STRUCTURE, E-LIFETIME. |
| R-14.17 / 14.17 owner | Preserve owner XML, not just an assumed owner URL. | Implemented. | E-LOCK, E-CONTEXT. No owner URL fetching. |
| R-14.18 / 14.18 prop | Keep property identities and full XML values. | Implemented. | E-XML, E-PROP. |
| R-14.19 / 14.19 propertyupdate | Generate ordered changes. | Implemented. | E-PROP. |
| R-14.20 / 14.20 propfind | Generate valid selector/include combinations. | Implemented and invalid options rejected before I/O. | E-PROP, E-IF. |
| R-14.21 / 14.21 propname | Request names without confusing absence with an empty value. | Implemented. | E-PROP. |
| R-14.22 / 14.22 propstat | Parse exactly one prop/status group and retain errors/description. | Implemented core shape checks. | E-STRUCTURE, E-CORE. |
| R-14.23 / 14.23 remove | Generate removal independently from an empty set value. | Implemented. | E-PROP. |
| R-14.24 / 14.24 response | Distinguish status/propstat alternatives and preserve multiple status hrefs. | Implemented core shape checks. | E-STRUCTURE. |
| R-14.25 / 14.25 responsedescription | Retain descriptions at resource/propstat/root levels. | Implemented detached fields/root XML/listing extensions. | E-STRUCTURE, E-LIFETIME. |
| R-14.26 / 14.26 set | Preserve XML while setting properties. | Implemented. | E-PROP, E-CONTEXT. |
| R-14.27 / 14.27 shared | Generate/decode shared scope without requiring its availability. | Implemented. | E-LOCK, E-CORE, E-LEASE. |
| R-14.28 / 14.28 status | Validate embedded HTTP status-line shape and retain exact code. | Implemented. | E-STRUCTURE. |
| R-14.29 / 14.29 timeout | Decode finite/infinite/missing without substituting a requested timeout. | Implemented, including zero seconds. | E-LOCK, E-LEASE, E-CORE. |
| R-14.30 / 14.30 write | Express/decode write lock type. | Implemented. | E-LOCK, E-CORE. |

## Live properties: section 15

The codecs below expose decoding failures separately and preserve original XML
and server status. A missing property is not fabricated as a successful default.
Server rules for mutability, creation, recomputation and protected status are not
client-side enforcement guarantees.

| ID / clause | Property / codec | Status | Evidence / boundary |
| --- | --- | --- | --- |
| R-15.1 / 15.1 | `creationdate` / `DavProperties.CreationDate` | Implemented XML date decoding. | E-PROP; server date generation is not verified. |
| R-15.2 / 15.2 | `displayname` / `DisplayName` | Implemented text decoding and writable DAV property requests. | E-PROP, E-CONTEXT; no blanket protected-namespace assumption. |
| R-15.3 / 15.3 | `getcontentlanguage` / `ContentLanguage` | Implemented text-preserving decoding. | E-PROP; no language negotiation policy. |
| R-15.4 / 15.4 | `getcontentlength` / `ContentLength` | Implemented non-negative integer decoding with explicit errors. | E-PROP; no conversion of malformed/negative sizes into unknown success. |
| R-15.5 / 15.5 | `getcontenttype` / `ContentType` | Implemented text-preserving decoding. | E-PROP; content sniffing is not performed. |
| R-15.6 / 15.6 | `getetag` / `ETag` | Implemented opaque text preservation. | E-PROP; server validity/uniqueness rules are not locally certified. |
| R-15.7 / 15.7 | `getlastmodified` / `LastModified` | Implemented date decoding with explicit errors. | E-PROP; decoder tolerates more than the strict HTTP-date lexical form. |
| R-15.8 / 15.8 | `lockdiscovery` / `LockDiscovery` | Implemented core active-lock singleton, content and URI validation. | E-LOCK, E-CORE, E-VALIDATION; omitted optional private metadata remains omitted. |
| R-15.9 / 15.9 | `resourcetype` / `ResourceTypes` | Implemented expanded-name list with empty/missing/denied distinctions. | E-PROP; no slash-based type guessing. |
| R-15.10 / 15.10 | `supportedlock` / `SupportedLocks` | Implemented core write-lock entries. | E-PROP, E-CORE; unsupported extension lock types remain available as raw XML, not typed core locks. |

## Conditions, extensibility and compliance: sections 16–18

| Ledger ID / clause | Requirement | Status / implementation | Evidence / boundary |
| --- | --- | --- | --- |
| R-16a / 16 | Retain `lock-token-matches-request-uri`, `lock-token-submitted`, `no-conflicting-lock`. | Implemented qualified XML and nested href/detail preservation. | E-CORE covers all three at outer error and property-group levels. No automatic token discovery, foreign-origin follow-up or reacquisition. |
| R-16b / 16 | Retain `no-external-entities`, `preserved-live-properties`, `propfind-finite-depth`, `cannot-modify-protected-property`. | Implemented qualified XML and numeric outcomes. | E-CORE covers all four. No automatic depth downgrade, property dropping or destructive fallback. |
| R-16c / 16 | Retain unknown custom conditions without treating an unknown QName as success. | Implemented raw `DavXmlValue` condition lists. | E-XML, E-STRUCTURE, E-LIFETIME. Complete condition-specific content-schema validation is unsupported. |
| R-17a / 17 | Ignore unexpected XML for protocol interpretation rather than searching arbitrary descendants for known fields. | Implemented direct-child interpretation; lock kind extensions are ignored while foreign lookalikes cannot establish kinds. | E-XML, E-CORE. Raw property XML remains available. |
| R-17b / 17 | Do not validate DAV against a hard-coded/dynamic DTD or retrieve external entities. | Implemented DTD prohibition, namespace-aware parsing and explicit core cardinality/content checks. | E-XML, E-VALIDATION. Unknown extensions are ignored rather than rejected as DTD violations. |
| R-17c / 17 | Preserve arbitrary dead-property XML during set/roundtrip. | Implemented structured XML copy/serialization. | E-CONTEXT. No normative behavior is assigned to processing instructions. |
| R-18.1 / 18.1 | Observe class 1 resource advertisement; do not assert remote conformance from it. | Implemented exact capability tokens. | E-OPTIONS, E-CORE. Server MUST obligations require independent execution evidence. |
| R-18.2 / 18.2 | Observe class 2 separately and expose its locking capabilities. | Implemented OPTIONS plus separate supportedlock/lockdiscovery queries. | E-OPTIONS, E-PROP, E-LOCK. No assumption that absent advertisement means permanent lack of support. |
| R-18.3 / 18.3 | Recognize class 3 independently of class 2; `1,3` need not mean locking. | Implemented token preservation without inference. | E-CORE, E-OPTIONS. No class-2 implication, certification, or global capability promise. |

## Explicit remaining scope

1. **Interoperability evidence:** this ledger's deterministic corpus is not a
   multi-server conformance matrix. Independently execute properties, lock
   compatibility/expiry, hierarchy failures and conditional operations; record
   server identity/version/configuration and complete expected outcomes.
2. **Validation scope:** core href/singleton rules and mandatory refresh response
   confirmation are implemented as described above. This does not certify every
   normative sentence in an extension, server behavior, or arbitrary dead-property
   schema. REPORT profiles retain their own validation responsibility. Optional
   discovery metadata remains optional; lockroot omission has the SHOULD
   applicability rationale recorded above.
3. **Server-only invariants:** property atomic rollback, binding consistency,
   lock conflict/privilege enforcement, empty-resource persistence, copy/move
   property preservation and validator generation cannot be certified by local
   parser/request tests.
4. **Lifecycle policy:** no automatic renewal scheduler, durable lease store,
   recovery after ambiguous acquisition, forced unlock/reacquisition, or
   automatic mutation replay. These are deliberate safety boundaries.
5. **Extensions:** ACL, DeltaV/versioning, DASL SEARCH execution, CalDAV/CardDAV,
   binding/ordering, extended MKCOL, and typed quota/report capability portfolios
   are not implemented by the core API. Arbitrary property XML and a REPORT seam
   do not constitute implementation of those specifications. The existing
   collection-sync REPORT profile is one batch, not a core RFC 4918 requirement.
6. **Adjacent layers:** reliable transfers, durable local synchronization and
   real-server tooling have their own contracts/evidence. Their presence must
   not be used to close the core gaps above without direct evidence.

The historical protocol plan remains broader than this implementation. This
ledger closes neither all of its milestones nor every normative sentence in
every referenced extension. It makes the core capabilities, safety choices and
known evidence gaps inspectable without presenting proposals as shipped APIs.
