# Interoperability gaps and priorities

This backlog separates **confirmed defects**, **server restrictions** and
**missing evidence**. A missing test is not proof of a missing library feature.
PR #92 adds tooling and evidence, not new production APIs. The protocol fixes
exposed by the original investigation already shipped in **3.2.0 through #90**.
Durable collection synchronization remains separate work in **#91**.

## Current evidence

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

**Action:** document the deployment restriction and require a server/module
configuration that actually enforces the application's write preconditions.
Keep this assertion and negative evidence. Do not retry unconditionally, treat
the response as a successful guarded write, or substitute HEAD-before-PUT:
the intervening write race remains.

**Done when:** a separately versioned candidate configuration rejects both
existing-resource `If-None-Match: *` and stale `If-Match` writes, with distinct
rejected payloads and unchanged stored bytes/validator. Until then, do not claim
safe conditional writes for this profile. No library workaround is established.

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
rejected writes. Separate run-owned files prevent an unsafe server's overwrite
from contaminating unrelated baseline operations. Missing strong validators or
unconfirmed state block dependent work instead of triggering a repair/replay.

**Done when:** both frameworks prove correct success/rejection statuses and
byte/validator integrity after every rejected write on supporting servers.
Unsupported features must stay distinct from violated advertised semantics.
Add production changes only if a concrete client defect is reproduced.

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

No additional production-code defect has been established by the synchronized
reruns. Keep the PR draft until its maintainers review the explicit nginx
failure and evidence limits; neither merging nor a code push publishes a
package. Publication requires a separate manual workflow dispatch.
