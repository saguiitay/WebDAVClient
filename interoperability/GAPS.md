# Interoperability gaps and priorities

This backlog separates **confirmed defects**, **server restrictions** and
**missing evidence**. A missing test is not proof of a missing library feature.
PR #92 added tooling and evidence, not new production APIs. The protocol fixes
exposed by the original investigation already shipped in **3.2.0 through #90**.
Durable collection synchronization remains separate work in **#91**.

## Current evidence

The 3.3.0 source at `4fe7029aeea78c26c71c43515420b28cc973bf31` adds
`DavIfConditions.TaggedAbsolutePaths(...)`, an explicit RFC 4918 resource-tag
choice, **not** a retry or relaxed condition. Existing `Tagged(...)` keeps
absolute URIs. New suite 1.1 runs compare both forms against the same pinned
servers, on .NET 8 and .NET 10:

| Profile / resource tags | Passed | Optional unsupported/unavailable | Failed / blocked |
| --- | --- | --- | --- |
| WsgiDAV 4.3.5 / Cheroot 11.1.2, default absolute URI | 49 | 2 | 4 / 1 |
| WsgiDAV 4.3.5 / Cheroot 11.1.2, explicit absolute path | 54 | 2 | 0 / 0 |
| SabreDAV 4.7.0 / PHP 8.3.25, default absolute URI | 55 | 1 | 0 / 0 |
| SabreDAV 4.7.0 / PHP 8.3.25, explicit absolute path | 55 | 1 | 0 / 0 |

All eight framework results verify run-tree deletion and absence. With explicit
path tags, WsgiDAV rejects wrong-token and valid-token/false-ETag MOVE with 412,
preserves source bytes/validator and destination absence, then accepts the
matching MOVE with 201 and exact destination bytes. The source and destination
names include Unicode, spaces, plus and literal percent. Default WsgiDAV
failures remain unchanged and retained. The
[source/environment companion](results/local-4fe7029-environment.json) records
both configurations, exact execution source, fixture identities and limits.
This API is **unpublished**; these are source-build results, not a downloaded
NuGet-package test or a blanket server guarantee.

Apache and nginx were not rerun for this resource-tag-only correction. Their
restrictions below remain open. No historical evidence was removed or relabeled.

### Earlier guarded-mutation baseline

Suite **1.1**, executed from `b3e2e5e80e38876767ac9e82c3a9bdca01e4e5b4`,
adds independently checked false-ETag conditions with valid lock tokens, not
just wrong-token rejections. The
[new run companion](results/local-b3e2e5e-environment.json) records all three
probe-development revisions, unchanged historical evidence and exact provenance.
Those baseline results are identical on .NET 8 and .NET 10:

| Profile | Passed | Optional unsupported/unavailable | Failed / blocked | Interpretation |
| --- | --- | --- | --- | --- |
| SabreDAV 4.7.0 / PHP 8.3.25, Basic | 55 | 1 | 0 / 0 | Strong guarded writes, token/ETag mutations and cleanup pass; no RFC 6578 claim |
| WsgiDAV 4.3.5 / Cheroot 11.1.2, Basic | 49 | 2 | 4 / 1 | Tagged MOVE ignores a false ETag with a valid token; see P0 |
| Apache 2.4.65, Basic, explicit path-plus alias | 17 | 2 | 5 / 9 | Weak HEAD ETags block strong-validator probes; see P1 |
| nginx 1.28.0 built-in DAV, anonymous | 16 | 8 | 4 / 0 | Both existing-resource and stale-ETag PUT overwrite stored bytes; see P0 |

All 24 new framework results confirm run-tree deletion and absence. WsgiDAV's
unexpectedly accepted MOVE also causes its source-lock cleanup to return 404;
that failed observation remains visible. A wrong-token MOVE returns 423 without
the required qualified lock condition and fails this conservative profile; that
rejection alone is **not** evidence of a data-integrity failure. In contrast,
the valid-token/false-ETag MOVE creates a destination and is unsafe.

Sabre's wrong-token 423 responses include `DAV:lock-token-submitted` and are
accepted as lock rejections, with the actual status retained. This does not
waive separate 412, byte/validator and destination-absence assertions for a
valid token combined with a false ETag. Offline regressions reject bare 423,
the wrong XML namespace and unrelated conditions.

### Historical suite 1.0 evidence

PR #92 was synchronized with `origin/master` at
`a5dc2431db258eaa99a156668d04f70ed9d5b9d4`. The
[hosted validation](https://github.com/saguiitay/WebDAVClient/actions/runs/39612916156)
passed. The explicitly opted-in
[independent-server rerun](https://github.com/saguiitay/WebDAVClient/actions/runs/39612921060)
exercised both .NET 8 and .NET 10 after that synchronization:

| Profile | Per-framework result | Interpretation |
| --- | --- | --- |
| WsgiDAV 4.3.5 / Cheroot 11.1.2, Basic | 19 passed; two optional checks unavailable; cleanup confirmed | Tested subset passes, not RFC 6578 support |
| Apache 2.4.65, Basic, explicit path-plus alias | 19 passed; two optional checks unavailable; cleanup confirmed | Tested subset passes only with the disclosed alias policy |
| SabreDAV 4.7.0 / PHP 8.3.25, Basic | 20 passed; one evidenced optional sync rejection; cleanup confirmed | Tested subset passes, not a sync-capable backend |
| nginx 1.28.0 built-in DAV, anonymous | 12 passed; seven optional methods unsupported; conditional PUT fails; cleanup confirmed | Unsafe for the tested conditional-create requirement |

Raw framework results retain execution merge revision
`963c8dbbb08131815fd15eaa8974c819cc26535f`, not the PR head or a published
package version. The [run companion](results/ci-39612921060-environment.json)
records provenance. [Local Windows-client reruns](results/local-5aaf542-environment.json)
at `5aaf542`, including actual Linux Docker containers, confirm the same
outcomes after the wrapper fix. Their initial startup failures and corrected
checkout/image identities remain separate evidence; see the
[fixture guide](README.md#fixture-status-and-limits).

The [matrix](results/compatibility-matrix.json) intentionally retains earlier
failed runs. Its all-history `observed-nonpassing` aggregate does not erase
later positive observations, nor do later passes erase earlier failures.

## Priority definitions

**P0** means a data-integrity guardrail for affected deployments, not an
unconfirmed emergency defect in every client. **P1** is the next validation
work before broadening relevant feature claims. **P2** expands deployment and
scale evidence. **P3** is an optional product enhancement or separate scope.

### P0: Do not use ignored write preconditions for concurrency control

**Type:** confirmed server restriction. nginx's built-in DAV fixture returns
204 to `PUT` with `If-None-Match: *` against an existing resource; the required
rejection is 412 on both frameworks. See
[the recorded failure](results/nginx-anonymous-ci-39612921060-net8.0.raw.json).
The failing live-workflow job is intentional evidence, not a green matrix.
Suite 1.1 also confirms stale `If-Match` returns 204 and that both rejected
payloads actually replace stored bytes; see the
[stronger result](results/nginx-anonymous-local-b3e2e5e-net8.0.raw.json).

**Action:** document the deployment restriction and require a server/module
configuration that actually enforces the application's write preconditions.
Keep this assertion and negative evidence. Do not retry unconditionally, treat
the response as a successful guarded write, or substitute HEAD-before-PUT:
the intervening write race remains.

**Done when:** a separately versioned candidate configuration rejects both
existing-resource `If-None-Match: *` and stale `If-Match` writes, with distinct
rejected payloads and unchanged stored bytes/validator. Until then, do not claim
safe conditional writes for this profile. No library workaround is established.

### P0: Do not rely on ignored tagged MOVE conditions in WsgiDAV

**Type:** newly reproduced server/profile restriction. WsgiDAV 4.3.5 accepts a
valid source lock token paired with a false ETag in an absolute-URI-tagged DAV
`If` list: MOVE returns 201 instead of 412 and creates the destination, on both
frameworks. See the
[independent false-condition run](results/wsgidav-basic-local-ee58c59-net8.0.raw.json).
Rejecting a wrong token is not proof that the ETag condition is enforced.
The absolute-URI resource-tag form is permitted by RFC 4918 sections 8.3 and
10.4.2; no malformed client request has been established.

**Root cause:** the pinned server retains resource-tag strings literally but
looks up a quoted resource path. It does not normalize full absolute URI keys,
so the valid token can authorize MOVE while the tagged false ETag is ignored.
Both full URI and absolute-path resource tags are allowed by
[RFC 4918 section 8.3](https://www.rfc-editor.org/rfc/rfc4918#section-8.3) and
[section 10.4.2](https://www.rfc-editor.org/rfc/rfc4918#section-10.4.2).
A separate [raw HTTP comparison](results/wsgidav-tagged-wire-local-e2f21e1.raw.json)
isolates the spelling difference; it is diagnostic evidence, not a library run.

**Implemented boundary:** upcoming 3.3.0 provides
`DavIfConditions.TaggedAbsolutePaths(...)`. The new typed-client runs satisfy
the acceptance criteria below for this explicit native Windows WsgiDAV profile.
All resources still undergo origin validation before the authority is omitted;
escaped paths/queries and condition lists are preserved. See the
[usage and limits](../docs/locking.md#explicit-absolute-path-resource-tags-upcoming-330).

**Remaining restriction:** default absolute-URI tags remain unsafe with the
pinned server. Select the explicit form before sending only after verifying
the actual deployment, or require an upstream correction. Do not replay a
failed/ambiguous mutation with a different spelling, strip the ETag, or add
HEAD-before-MOVE as a workaround. Untested versions and reverse proxies remain
unverified; this does not fix nginx's ignored HTTP preconditions.

**Done when:** a separately versioned candidate rejects a valid-token/false-ETag
tagged MOVE without changing source bytes/validator or creating its destination,
and accepts the matching condition on both frameworks. Preserve the negative
evidence and include the Unicode/plus/percent source path.

### P1: Establish strong-validator evidence for the Apache profile

**Type:** prerequisite/evidence gap, not a demonstrated client defect or a claim
that weak ETags violate HTTP. The pinned Apache 2.4.65 fixture returns weak ETags
on HEAD immediately after the probes create their files. Suite 1.1 records
`weak-etag`, fails five setup observations and blocks nine dependent observations;
it does not send a weak tag as a strong `If-Match` condition.

**Action:** establish an explicitly recorded fixture/configuration that provides
usable strong validators, then rerun matching/stale HTTP ETags and lock-conditioned
mutations. Do not silently strip `W/`, wait/retry until the profile turns green,
or reinterpret blocked checks as unsupported.

**Done when:** both frameworks have real strong-validator baselines, correct
positive/negative mutations, unchanged rejected-write state and confirmed cleanup.
Historical suite 1.0 passes do not supply this evidence.

### P1: Establish positive independent-server RFC 6578 evidence

**Type:** evidence gap, not proof that the shipped REPORT API is defective.
WsgiDAV returns 405 and Apache 501 with missing discovery. Sabre's filesystem
backend is not `ISyncCollection`; its narrowly evidenced 415
`ReportNotSupported` rejection is not a successful synchronization.

**Action:** provision an actual sync-capable backend and test initial tokens,
incremental additions/updates/deletions, empty changes, limited batches and
continuation, and invalid/expired tokens. Preserve response-level and
member-level outcomes; a 404 member means deletion only in the sync-report
profile.

**Done when:** both frameworks have reproducible positive and negative live
results, confirmed cleanup, and enough changes to prove continuation does not
lose members. Use that evidence before claiming independent-server support for
the durable-sync work in #91. Keep #91's implementation out of this PR.

### P1: Strengthen guarded writes and lock-conditioned mutations

**Type:** coverage gap around existing APIs, addressed by the suite **1.1**
[guarded-mutation probes](../tests/WebDAVClient.InteropTests/CompatibilitySuite.cs).
The retained suite 1.0 baseline above only sent `If-None-Match: *` with the same
bytes as the stored resource. Historical summaries calling that a stale
`If-Match` test are not evidence of that test.

**Implemented:** distinct rejected payloads; create-if-absent; matching/stale
strong ETags; valid/wrong-token PUT, DELETE and tagged MOVE; AND/OR DAV `If`
conditions; released-token rejection; and byte/validator integrity after
rejected writes. Valid-token/false-ETag pairs also check PUT, DELETE and tagged
MOVE: token authorization alone must not mask an ignored condition.
Separate run-owned files prevent an unsafe server's overwrite
from contaminating unrelated baseline operations. Missing strong validators or
unconfirmed state block dependent work instead of triggering a repair/replay.

**Done when:** both frameworks prove correct success/rejection statuses and
byte/validator integrity after every rejected write on supporting servers.
Unsupported features must stay distinct from violated advertised semantics.
Add production changes only if a concrete client defect is reproduced.

**Verified boundary:** Sabre's current profile passes all 55 observed checks on
both frameworks, with one evidenced optional sync rejection. The coverage work
is implemented and independently exercised; the newly exposed WsgiDAV and
Apache restrictions above are separate follow-ups, not erased failures.

**Still separate:** actual timeout-expiry behavior, shared locks and multiple
source/destination locks are not established by a released-token test. New
live evidence must identify suite 1.1; the suite 1.0 records above are unchanged.

### P2: Verify product endpoints and authentication boundaries

**Type:** missing independent deployment evidence. Nextcloud 31.0.9 and ownCloud
10.15.0 are pinned candidate fixtures, not executed profiles. IIS/Windows
authentication and HTTPS are also unverified; HTTP Basic loopback observations
do not establish any of them.

**Action:** run the candidates against disposable accounts, record actual
server versions/configuration, and exercise product-specific paths, properties,
locks and authentication. Add operator-owned IIS Windows/Basic runs and
controlled HTTPS certificate/trust failure cases. Keep certificate verification
enabled and verify redirects do not silently change the endpoint or leak auth.

**Done when:** each claimed product/auth/transport combination has retained
per-framework results, ownership/cleanup proof and explicit unsupported features.
A startup failure or missing result remains unverified, never compatible.

### P2: Exercise large transfers, trees and lease limits

**Type:** scale/profile coverage gap. Small exact-byte transfers, ranges and
exclusive locks already have live evidence; that is not proof of large/deep
trees, interrupted/resumed downloads or shared-lock interoperability.

**Action:** add bounded large-file and deep-listing scenarios, streaming and
cancellation/cleanup checks, resumed transfers with changed validators, and
shared-lock/timeout cases on servers that advertise them.

**Done when:** define byte/depth/time bounds up front and retain both-framework
integrity, response-ownership and cleanup outcomes without relaxing parser or
resource limits. Do not infer scalability from the small fixture.

### P2: Fail fast when an owned container exits before readiness

**Type:** tooling diagnostics gap, not a package defect. A reused Windows
worktree retained CRLF Docker entrypoints. The images built, but readiness failed; the inspected Sabre
container exited 127. Readiness reported only a request failure after its
bounded timeout. Normalizing the
checkout to the existing committed LF content changes no shell script content.
This PR now versions LF checkout rules and byte-preserving raw-evidence
attributes; the repository previously had no `.gitattributes`.

**Action:** check entrypoint prerequisites and monitor the exact run-owned
container's exit state during readiness, without dumping environment variables
or unbounded container logs. Retain startup failures separately from completed
per-framework compatibility results.

**Done when:** offline regressions distinguish an exited container, a still
starting container and healthy HTTP readiness, and a failed startup produces
bounded sanitized evidence promptly without claiming a server-protocol result.

### P3: Consider production diagnostics and separate protocol extensions

**Type:** optional enhancement, not a known correctness blocker. Existing
synthetic PROPPATCH capture is bounded, opt-in and fixture-only; it is not a
general production tracing API.

**Action:** design production diagnostics separately if support cases justify
it, with explicit redaction/ownership/body limits and no credentials or tokens.
ACLs, quotas, CalDAV/CardDAV, chunking extensions and nonstandard partial PUT
need separate requirements and server evidence; they are outside suite v1,
not automatically critical omissions from this package.

## Fixed, not remaining package gaps

| Finding | Resolution and remaining boundary |
| --- | --- |
| Unicode/resource identity and percent-escape hex case | Shipped in 3.2.0/#90; exact resource identity remains strict |
| Apache PROPPATCH returns path `+` for `%2B` | Shipped explicit, default-off `PropPatchOptions.AllowPathPlusEncodingAlias`; no generic URI decoding or replay |
| Sabre optional lockroot contains unescaped scalar text | Shipped metadata-only handling; never follow it as a URI, and retain token/grant validation |
| WsgiDAV bare Lock-Token spelling | Shipped acceptance only with one exactly matching validated discovery token |
| Wrapper converts absent environment variables to empty strings | Fixed in this PR with exact unset/empty/configured restoration, including failed builds, and offline regressions in both workflows |
| Prepared 3.2.1 missing from the new changelog | Fixed after the master merge; release-note validation passes without publishing |

No additional production-code defect has been established by these reruns.
PR #92 is merged; follow-up probe changes and newly exposed server restrictions
remain separately reviewable. Neither merging nor a code push publishes a
package. Publication requires a separate manual workflow dispatch.
