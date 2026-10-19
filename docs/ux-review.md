# UX and developer-experience review

Reviewed 2026-10-19 by a dedicated UX agent. For this library, UX means API
consumption, upgrades, documentation, configuration, errors, and sample usability.

## Outcome and priorities

## Remediation progress

- UX-07: Added named argument guards for null transports/configuration,
  malformed server/port values, upload inputs, required paths and empty names.
  Named-property bodies validate before discovery. Null/empty metadata paths
  still address the root. Focused cases assert zero requests and retained
  caller ownership on invalid upload input.

- UX-05: Replaced source-edited/destructive onboarding with anonymous read-only
  defaults, argument/environment configuration and offline validation. Explicit
  mutations use a unique owned directory, known content, scoped streams and
  independent bounded cleanup/unlock. Fake scenarios cover empty/missing data,
  failure, cancellation, failed creation, lock failure and cleanup failure.
  README onboarding is read-only; its lock example unlocks in `finally`.

- UX-04: The partial-transfer sample uses upload offsets `[2,5)` for three bytes
  and download offsets `[2,4]` on the uploaded target, with scoped streams.
  A fake-transport regression verifies the exact range headers and payload.

- UX-03: The multi-target sample is an executable and is not packable. Its async
  entry point supports offline help and rejects unknown arguments before I/O;
  contributor instructions select a framework explicitly.

- UX-02: Corrected qualified property construction and namespace-aware dictionary
  lookup in the targeted PROPFIND example; executable regression retrieves a
  custom XML-decoded value.
- UX-08 and UX-09 were already addressed by reliability/correctness remediation:
  upload ownership is documented/tested and OPTIONS no longer requires PROPFIND.

The original findings below describe the reviewed revision.

Repair onboarding examples and package documentation first, then address
API consumption and configuration friction. Recommendations are additive unless
an intentional breaking release is chosen.

P2 = material consumer friction; P3 = discoverability/docs.

| ID | Priority | Type | Finding | Confidence |
|---|---|---|---|---|
| UX-02 | P2 | Defect | Targeted PROPFIND example is unusable as written | 10/10 |
| UX-03 | P2 | Defect | Console sample configured as a library | 10/10 |
| UX-04 | P2 | Defect | Partial-upload sample violates its argument invariant | 10/10 |
| UX-05 | P2 | Defect | Examples assume content and leave artifacts/locks | 10/10 |
| UX-06 | P2 | Defect | XML API documentation missing from package | 10/10 |
| UX-07 | P2 | Defect | Invalid input causes null-reference or delayed errors | 10/10 |
| UX-08 | P2 | Defect | Upload stream ownership undocumented | 10/10 |
| UX-09 | P2 | Defect | Capability discovery requires PROPFIND before OPTIONS | 10/10 |
| UX-10 | P2 | Defect | "Not found" properties also represent denied/failed properties | 10/10 |
| UX-11 | P2 | Defect | Authentication overloads introduce null-call ambiguity | 10/10 |
| UX-12 | P2 | Defect | Delete named arguments differ by static type | 10/10 |
| UX-13 | P2 | Enhancement | Exceptions lack actionable request context | 9/10 |
| UX-14 | P2 | Enhancement | Missing compact consumption/ownership guide | 9/10 |
| UX-15 | P3 | Defect | Boolean docs imply false returns instead of exceptions | 10/10 |
| UX-16 | P3 | Defect | Contributor/release guidance contradicts implementation | 10/10 |
| UX-17 | P3 | Enhancement | Configuration discoverability varies by construction | 9/10 |

## UX-02: Repair targeted PROPFIND documentation

**Evidence:** `README.md:255-257` reverses `PropertyName` arguments; the actual
constructor is `(namespace, localName)` with NCName validation
(`Model\PropertyName.cs:37-54`). `README.md:265` uses `p.Name.LocalName`,
but `Item.cs:99` exposes a dictionary whose entries use `Key`.

The snippet fails compilation; fixing only the dictionary leaves runtime
validation failure because `DAV:` is treated as a local name.

**Recommendation:** Use `new PropertyName("https://example.com/ns", "author")`
and qualified dictionary lookup, with complete imports.

**Acceptance:** Compile the snippet and retrieve a custom value from fake XML.

## UX-03: Make the manual sample runnable

**Evidence:** `TestWebDAVClient\TestWebDAVClient.csproj:1-16` evaluates to
`OutputType=Library`, `IsPackable=true`. It is multi-targeted; contributor
instructions at `:18` incorrectly recommend running it without a framework.

**Recommendation:** Set `OutputType=Exe`, `IsPackable=false`, and document
`dotnet run --project TestWebDAVClient -f net10.0`, or intentionally single-target.

**Acceptance:** Evaluate executable properties and run an offline help/config
validation path without contacting a server.

## UX-04: Correct partial-upload example

**Evidence:** `TestWebDAVClient\Program.cs:49-50` sends three bytes with offsets
2 and 6; `Client.cs:550-555` requires `2 + 3 == 6`, which is false.

**Recommendation:** Align the example with the corrected range contract, making
inclusive/exclusive semantics explicit. Coordinate with correctness findings;
do not merely change offsets to satisfy faulty wire semantics.

**Acceptance:** Run this fragment through fake transport and verify validation,
wire range, and byte count agree.

## UX-05: Safe, self-contained examples

**Evidence:** `Program.cs:20-22` requires source edits; `:27-34` assumes existing
folders/files; `:36-63` leaves temporary/upload artifacts; `:49-54` leaves streams
unscoped. `README.md:159-195` repeats assumptions/incomplete cleanup, and
`:225-234` unlocks only on success.

**Recommendation:** Read-only onboarding first; explicit opt-in mutations,
arguments/environment configuration, meaningful missing-content messages,
unique run-owned directories, scoped streams, and `finally` cleanup/unlock.
Never delete pre-existing resources.

**Acceptance:** Empty server, missing targets, failed upload, cancellation, and
failure after lock acquisition produce clear messages and bounded cleanup.

## UX-06: Ship XML documentation

**Evidence:** `WebDAVClient.csproj:3-30` does not enable documentation generation;
the evaluated property is unset. The inspected local 2.7.0 package has no XML
beside its assemblies despite extensive docs such as `IClient.cs:146-175`.

**Recommendation:** Generate and pack XML for both TFMs; resolve relevant warnings.

**Acceptance:** Inspect a newly packed artifact and verify package-reference
IntelliSense displays parameter/ownership descriptions.

## UX-07: Validate before I/O

**Evidence:** Null setters dereference (`Client.cs:71,84`); injected constructors
accept null (`:235-243`; `HttpClientWrapper.cs:26-29`); uploads/path handling
dereference inputs (`Client.cs:515,552,558,592`). Empty property validation occurs
after discovery (`:422-427`; `ClientPropFindTests.cs:154-157`).

**Recommendation:** Consistent argument exceptions with parameter names, before
network access. Preserve intentional root/null-path semantics.

**Acceptance:** Null transport/config/content/name/path and invalid property-list
cases produce documented argument errors with zero outbound requests.

## UX-08: Explain upload stream ownership

**Evidence:** `IClient.cs:169-175,181-189` omits ownership transfer;
`Client.cs:1176-1204` wraps caller streams in content disposed with the request.

Consumers lose the ability to seek/retry/hash the same stream unexpectedly.

**Recommendation:** Document current behavior immediately; any leave-open support
must be additive and explicit. Document partial-upload length requirements.

**Acceptance:** Disposal-tracking tests for success, error, cancellation and,
if supported, leave-open/non-seekable inputs.

## UX-09: OPTIONS should work before PROPFIND

**Evidence:** `IClient.cs:43-48` and `README.md:214-219` recommend first-contact
capability discovery, but `Client.cs:254-256,1230-1245` first performs PROPFIND.

Endpoints rejecting PROPFIND fail before OPTIONS can diagnose their capabilities.

**Recommendation:** OPTIONS against the configured endpoint independently of
successful discovery, or an explicit diagnostic mode.

**Acceptance:** First request is OPTIONS; endpoints accepting OPTIONS but rejecting
PROPFIND still return capability data, including absence of DAV headers.

## UX-10: Preserve property failure distinctions

**Evidence:** `Item.cs:101-106` and `IClient.cs:74-76` describe genuine 404 absence,
but `ResponseParser.cs:380-398` puts all non-200 statuses into `NotFoundProperties`.

**Recommendation:** Correct docs and add qualified-property results retaining
status codes. Preserve legacy collection behavior unless intentionally breaking.

**Acceptance:** Distinguish 200/404/403/424 and malformed/missing status while
pinning documented legacy behavior.

## UX-11: Authentication overload ambiguity

**Evidence:** `Client.cs:131,145,163` has credentials, string, and provider
constructors with matching remaining argument shapes. History before `3a24e93`
confirms the new multi-argument null ambiguity.

`new Client(null, uploadTimeout: timeout)` was valid and becomes ambiguous.
Single-argument null was already ambiguous with transport constructors.

**Recommendation:** Explicit authentication factories/builder for future additions.
Document `new Client(credential: null, uploadTimeout: timeout)` as a workaround.
Assess release status before removing any shipped overload.

**Acceptance:** Compile legacy null/timeout/proxy calls and factory alternatives.

## UX-12: Delete parameter-name/default mismatch

**Evidence:** `IClient.cs:210,219` uses optional `path="/"`;
`Client.cs:622,632` uses required `href`.

Changing static type changes accepted named arguments and omitted-argument calls.

**Recommendation:** Document discrepancy; choose a consistent future surface.
Do not silently rename existing parameters, which is itself source-breaking.

**Acceptance:** Compile named/default calls through both types and add parameter
name/default checks to API compatibility coverage.

## UX-13: Actionable error context

**Evidence:** `Helpers\WebDAVException.cs:14-20,101-120` lacks request context;
`Client.cs:529,607,844,1096` uses generic messages. Roadmap `:302-319` already
proposes richer exceptions.

**Recommendation:** Add read-only operation, method and sanitized resource context,
preserving existing constructors and status access. Never expose tokens or
unrestricted headers/bodies.

**Acceptance:** Concurrent failures on distinct resources are distinguishable;
credentials/query secrets are redacted; existing catches remain compatible.

## UX-14: Compact package-consumption guide

**Evidence:** `README.md:9-11,33-120` links NuGet but leads with release history;
`:141-142,226-230,259` mixes path forms. TFM support is in
`WebDAVClient.csproj:4`. Default Windows credentials occur at `Client.cs:185-189`;
injected ownership at `:235-243,1359-1365`; wrapper disposal at
`HttpClientWrapper.cs:59-63`.

**Recommendation:** Quickstart before changelog: install command, current TFMs,
imports, path-form examples, authentication matrix (including true anonymous
transport versus default credentials), and ownership table.

**Acceptance:** Clean package-reference examples compile for both TFMs; fake
responses verify documented paths and disposal behavior.

## UX-15: Accurate boolean/error docs

**Evidence:** `IClient.cs:174,188,198,230,243` says false on failure; implementation
throws (`Client.cs:525-532,568-575,600-610`).

**Recommendation:** Document true-on-success/exception-on-failure; preserve return
types. Any nonthrowing API should be explicit and additive.

**Acceptance:** Examples handle HTTP rejection via documented exceptions.

## UX-16: Reconcile repository guidance

**Evidence:** `.github\copilot-instructions.md:16` says tests are absent, contrary
to `WebDAVClient.sln:10-11` and test csproj `:4-18`. Instructions `:29` describe
planned authentication/builder types as implemented; `ROADMAP.md:11` says planned.
`README.md:35` and `ROADMAP.md:11,158,189,473-505` conflict on release status.

**Recommendation:** Clearly distinguish existing APIs, release status and archival
plans; correct test instructions.

**Acceptance:** Every documented current type exists, test commands discover both
TFMs, and release wording is consistent without claiming feed verification.

## UX-17: Configuration discoverability

**Evidence:** TLS callback only on concrete client (`Client.cs:112-121`), unlike
interface settings (`IClient.cs:12-40`). README encourages interface variables
(`:128-145`). Constructor-specific timeout/proxy settings (`Client.cs:131-163`)
contrast with planned builder (`ROADMAP.md:345-374`).

**Recommendation:** Additive builder/options or complete recipes; explicitly reject
unsupported combinations before I/O. Avoid required new interface members that
break downstream implementations.

**Acceptance:** Recipes cover credentials, bearer refresh, injected transport,
proxy, certificate callbacks, and distinct timeouts with explicit ownership.

## Implementation order and limitations

Fix/compile examples, make sample mutations safe,
ship API docs, clarify ownership/errors, and add guards/status/config improvements.
The agent reviewed local source/history, evaluated project properties, and inspected
a local package. It did not fetch published packages, run the live sample, or
create a binary-compatibility fixture. Baseline reported by parent: 238 tests passed
on each target framework after restore. Existing tests do not establish example
compilation, package IntelliSense, or consumer compatibility.
