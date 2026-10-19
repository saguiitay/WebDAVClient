# Bugs and correctness review

Reviewed 2026-10-19 at revision `ac0ef8a` by a dedicated correctness agent.
This is the original review, before subsequent security remediation.

## Remediation progress

- BUG-01: fixed by security remediation in `a3c2415`; authenticated parser regressions cover the original identity-poisoning cases.
- BUG-02: fixed; exclusive upload end offsets are translated to inclusive wire ranges and validated against remaining stream bytes.
- BUG-09: fixed; reader advancement preserves siblings after empty and nonempty versioning properties.
- BUG-11: fixed; empty-namespace properties serialize without an illegal namespace prefix.
- BUG-12: fixed; hidden values are parsed rather than inferred from property presence, consistently across modes.
- BUG-07: fixed; text and CDATA decode correctly, structured XML is preserved, and metadata text round trips through PROPPATCH/PROPFIND.
- BUG-03: fixed; changed connection settings invalidate discovery, and version-checked publication prevents stale in-flight roots from being cached.

## Outcome

**12 confirmed defects: 3 high, 8 medium, 1 low. No critical defect established.**
Resource identity, partial-upload byte ranges, and stale destination configuration
are the highest priorities.

| ID | Severity | Finding | Primary evidence | Confidence |
|---|---|---|---|---|
| BUG-01 | High | Nested property URLs overwrite resource identities | `WebDAVClient\Helpers\ResponseParser.cs:172-186` | 10/10 |
| BUG-02 | High | Partial uploads advertise one extra byte | `WebDAVClient\Client.cs:552-554,1207-1208` | 10/10 |
| BUG-03 | High | Configuration changes retain the old resolved root | `WebDAVClient\Client.cs:67-95,1233-1245` | 9/10 |
| BUG-04 | Medium | URL joining duplicates separators or the base path | `WebDAVClient\Client.cs:1302-1329` | 10/10 |
| BUG-05 | Medium | Sparse/property-name listings misclassify collections/include parent | `ResponseParser.cs:305-308,369-377`; `Client.cs:379-393` | 10/10 |
| BUG-06 | Medium | Partial downloads accept a full response silently | `WebDAVClient\Client.cs:490-501,1087-1096` | 10/10 |
| BUG-07 | Medium | Targeted property text remains XML-escaped | `ResponseParser.cs:354,385-387,405-415` | 10/10 |
| BUG-08 | Medium | Resource-level PROPFIND failures become successful items | `ResponseParser.cs:132-152,302-315` | 10/10 |
| BUG-09 | Medium | Skipping versioning properties skips the next start tag | `ResponseParser.cs:121-126,277-280` | 10/10 |
| BUG-10 | Medium | OPTIONS depends on PROPFIND and changes file targets | `WebDAVClient\Client.cs:254-256,1230-1243` | 10/10 |
| BUG-11 | Medium | Empty-namespace property names cannot serialize | `Helpers\PropFindRequestBuilder.cs:105-119` | 10/10 |
| BUG-12 | Low | Explicitly non-hidden resources become hidden | `ResponseParser.cs:272-275,491-494` | 10/10 |

Short helper references above and below are relative to `WebDAVClient\Helpers`;
`Client.cs` is under `WebDAVClient`.

## BUG-01: Scope resource identity to response structure

Allprop recognizes local names globally, without depth or namespace boundaries.
A lockdiscovery property containing lockroot and locktoken hrefs changed the
parsed resource href from `/dav/file.txt` to `opaquelocktoken:abc`. A custom
`X:displayname` also populated the standard field. These were runtime-reproduced.
Following such identities can address the wrong resource or fail.

**Recommendation:** Only direct `DAV:href` under the actual `DAV:response` sets
identity; only correctly positioned/namespaced properties set typed fields.
Treat arbitrary property subtrees as data.

**Acceptance:** Lock owner/root/token hrefs, nested custom href/response elements,
and custom local-name collisions leave identity and typed fields unchanged.
This root cause overlaps SEC-01 and is addressed by the subsequent security fix.

## BUG-02: Reconcile partial-upload endpoints and payload

Validation treats `endBytes` as exclusive, but HTTP Content-Range is inclusive.
A captured five-byte request emitted `bytes 0-5/*` and Content-Length 5, describing
six bytes. Passing inclusive endpoint 4 instead failed validation.
`ClientTests.cs:293-305` currently encodes the bad endpoint.

**Recommendation:** Preserve a clearly documented public convention and translate
to correct wire endpoints, accounting for the actual remaining stream bytes.
Strict servers otherwise reject updates; permissive handling risks wrong offsets.

**Acceptance:** One/multiple bytes, nonzero offsets, invalid intervals and
positioned streams; assert interval length, Content-Length and actual body length.

## BUG-03: Invalidate resolved roots on reconfiguration

The mutable Server/BasePath/Port setters never invalidate `m_encodedBasePath`.
After resolution to `/webdav/`, changing BasePath to `/other/` still produced
`http://example.com/webdav/file.txt` in a URL-builder probe. Cached absolute roots
can also ignore changed ports or conflict with changed hosts.

**Recommendation:** Invalidate on effective configuration changes and prevent
in-flight discovery from publishing an obsolete result, or explicitly prohibit
post-initialization changes. Mutations must not silently target the old root.

**Acceptance:** Warm, change each setting, assert the next destination; test both
path-only and absolute root hrefs. No live reconfiguration test was performed.

## BUG-04: Consistent path joining

With `/webdav/` as root, `/file.txt` becomes `/webdav//file.txt`. With an absolute
root, `/webdav/file.txt` becomes `/webdav/webdav/file.txt`. Actual builder output
confirmed both; uploads construct leading-slash paths internally.

**Recommendation:** Centralize joining and detect already-prefixed paths without
collapsing separators inside resource names.

**Acceptance:** Exact URI matrix for both root formats, relative/full paths,
boundary slashes, uploads, directory creation, COPY/MOVE destinations.

## BUG-05: Preserve sparse-list identity and exclude parents

PropName does not apply property values, leaving IsCollection false; trailing
slashes are then stripped and List includes the parent as a non-collection.
Targeted requests omitting resourcetype exhibit the same risk.

A public-operation reproduction returned `/webdav` and `/webdav/sub`, both
non-collections, for parent `/webdav/` and child `/webdav/sub/`.

**Recommendation:** Exclude parent by normalized identity independently of type.
Preserve href shape when type is unknown; obtain reliable collection information.

**Acceptance:** Exact membership/hrefs/type for propname and getetag-only or
displayname-only listings.

## BUG-06: Validate partial-download responses

The shared download path accepts 200 and 206 without validating the requested
interval. Requesting bytes 2-4 of `0123456789` returned the complete string when
the stub ignored Range, instead of `234`. Chunk assembly/resumption can corrupt.

**Recommendation:** Require matching 206/Content-Range or implement a deliberate,
validated full-response slicing fallback.

**Acceptance:** Ignored Range, wrong/missing interval, and correct partial body;
assert returned bytes, not just the request header.

## BUG-07: Decode text, preserve structured properties deliberately

ReadInnerXml values are returned unchanged. Targeted displayname `A &amp; B` and
custom text `x &lt; y` remained escaped in a runtime probe. Read/write round trips
can double-escape metadata and differ from allprop.

**Recommendation:** Decode text using XML parsing, with a distinct policy for
structured XML; no ad hoc entity replacement.

**Acceptance:** Entities, Unicode, CDATA, empty text, structured properties and
SetProperty/targeted-Get round trips.

## BUG-08: Honor embedded resource failure

A 207 body with a response-level 403 produced an ordinary item. Get selects that
first item; default allprop also ignores failed propstat groups. Consumers can
mistake incomplete/denied metadata for a successful lookup.

**Recommendation:** Track resource and property statuses independently; expose or
throw failures, and apply typed values only from successful propstats.

**Acceptance:** Mixed success/failure and depth-zero failures. Coordinate with
REL-03, which describes the same root cause's recovery consequences.

## BUG-09: Correct reader advancement after Skip

Skip already positions the reader on the next sibling, then the loop reads again.
`checked-in` immediately followed by a collection resourcetype caused a runtime
probe to miss collection classification and strip the folder slash. The existing
test at `Parser\ResponseParserTests.cs:169-193` only checks noncrashing behavior.

**Recommendation:** Process the current node after Skip rather than advancing twice.

**Acceptance:** Relevant sibling properties immediately after each skipped
versioning-property type retain their values.

## BUG-10: Independent OPTIONS and exact targets

A stub returning 405 for PROPFIND but successful OPTIONS received only PROPFIND;
discovery threw before OPTIONS was attempted. The URL call also appends a slash
to file targets (`file.txt/`).

**Recommendation:** OPTIONS must not depend on discovery and must preserve the
requested resource shape. Coordinate with UX-09.

**Acceptance:** Fresh client, rejected PROPFIND/successful OPTIONS, file targets,
and non-WebDAV capability responses.

## BUG-11: Serialize supported no-namespace properties

`Model\PropertyName.cs:30-37` permits an empty namespace, but the builder gives it
a prefix. Invoking BuildPropBody for `new PropertyName("", "author")` threw
"Cannot use a prefix with an empty namespace."

**Recommendation:** Emit an unprefixed element and reset the default namespace
where necessary.

**Acceptance:** Pure no-namespace and mixed DAV/custom/empty-namespace requests;
parse generated XML and assert exact qualified names.

## BUG-12: Parse hidden values rather than property presence

Both parser paths set IsHidden true when the property exists. An allprop probe
with `ishidden` value `0` returned true, hiding ordinary files in consumer filters.

**Recommendation:** Parse recognized numeric/boolean forms consistently.

**Acceptance:** 0/1/false/true/empty/malformed values across both modes.

## Validation and limitations

The agent reviewed production code, configuration, docs, workflows and relevant
tests. Focused in-memory reflection/public-operation probes used existing net8.0
Release assemblies under PowerShell's .NET 10 runtime and the repository stub.
No live network or manual smoke app was used, and the agent edited no files.
Parent baseline before remediation: 238/238 passed on each supported framework.
Those tests omit these scenarios or sometimes encode the faulty behavior.
