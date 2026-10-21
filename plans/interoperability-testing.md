## 5. A direction I'd prioritize even before adding major features: interoperability testing

WebDAV's biggest engineering challenge is often differences between implementations.

Your existing issues include compatibility problems involving ownCloud, Unicode handling, partial uploads, and MOVE operations.&#x20;

[image](https://www.google.com/s2/favicons?domain=https://github.com\&sz=32)

GitHub



I'd build an automated compatibility matrix using real WebDAV servers.

| Target server      | What to test                                       |
| ------------------ | -------------------------------------------------- |
| Nextcloud          | Authentication, metadata, locking, synchronization |
| ownCloud           | Collections, properties, path handling             |
| Apache mod_dav     | Core RFC 4918 compliance                           |
| nginx WebDAV       | Basic operations and unsupported-method behavior   |
| SabreDAV           | Extensions, properties, multistatus responses      |
| Windows IIS WebDAV | Windows authentication, locking, path handling     |

This might give you more differentiation than another ten API methods.

Publish a compatibility matrix in the README, including known limitations. Developers choosing a library frequently care more about knowing that it works against their server than the number of features implemented.

I'd also add an optional diagnostics facility that captures sanitized DAV request/response information, capability discovery, and structured failure reasons. That would reduce your support burden.

## Implementation update — upcoming v3 source, 2026-10-20

The original proposal above is preserved. An opt-in executable compatibility
suite now lives in `WebDAVClient.InteropTests`, with offline harness regressions
included in the solution. Ordinary `dotnet test` does not contact a server.
No production package dependencies or publication/version metadata were changed.

Run the pinned independent WsgiDAV fixture on both target frameworks:

```powershell
py -m venv interoperability\.venv
interoperability\.venv\Scripts\python.exe -m pip install -r interoperability\fixtures\wsgidav\requirements.txt
.\interoperability\Run-Fixture.ps1 -Profile wsgidav
```

`.\interoperability\Run-Fixture.ps1 -Profile apache` (and `nginx`, `sabredav`,
`nextcloud`, `owncloud`) selects isolated container fixtures. The dedicated
workflow supports explicit `workflow_dispatch` for one fixture or the
`run-interoperability` PR label for independent WsgiDAV/Apache/nginx/SabreDAV jobs.
Its unprivileged, read-only `pull_request: labeled` path uses no PR secrets;
ordinary PR updates do not trigger network tests. Both frameworks are exercised,
and JSON artifacts are retained even after a compatibility failure. IIS requires
an operator-owned Windows site. See [suite documentation](../interoperability/README.md) for safe
authentication/configuration, isolation, cleanup, status interpretation and
fixture caveats.

Actual checked-in results cover WsgiDAV 4.3.5 / Cheroot 11.1.2 on Windows with
anonymous and Basic authentication on .NET 8 and .NET 10. They include Unicode
paths, exact transfer/range bytes, conditional writes, properties/multistatus,
COPY/MOVE, exclusive locking/refresh and confirmed cleanup. RFC 6578 REPORT
returns an observed 405; missing discovery properties are not conflated with
unsupported methods.

The [versioned matrix](../interoperability/results/compatibility-matrix.json)
and per-run schema-v1 JSON files are evidence, not a promise of universal server
support. Docker CLI and registry inspection are available, but the local Linux
daemon is unavailable; **no container-server green results are claimed**.
Nextcloud/ownCloud execution, corrected Sabre Linux-container verification and
IIS/Windows authentication remain explicit evidence gaps. General production
diagnostics were deferred; normal result JSON remains sanitized. The explicitly
scoped synthetic fixture-only body capture is described below.

### Hosted fixture evidence — run 39505211029

WsgiDAV Basic passed on hosted Linux on both frameworks. Apache completed with
18 passing checks but a property-operation protocol exception at HTTP 207;
nginx completed with 12 passing checks but ignored conditional PUT
(`If-None-Match: *`, observed 204 rather than required 412). Both retained
confirmed cleanup. These failures remain failures, not “unsupported” successes.
SabreDAV's image built, but the directory-GET readiness assumption prevented suite
execution. Readiness now uses OPTIONS for DAV profiles (GET only for built-in
nginx), with offline regressions and sanitized startup-status artifacts. Narrow
property-phase/allowlisted protocol-cause diagnostics support the next Apache
rerun without capturing response bodies or exception messages. See the refreshed
matrix for exact preserved positive and negative evidence.

### Post-strict-core investigation and reproducible diagnostics

Hosted run 39509017232 confirms OPTIONS readiness and actual Sabre execution.
WsgiDAV remains passing; Apache PROPPATCH 207, nginx conditional PUT 204 and
Sabre property/lock/cleanup failures remain recorded, not silently unsupported.
An independent native SabreDAV 4.7.0/PHP 8.3.25 fixture produced exact synthetic
PROPPATCH XML: lowercase percent escapes identify the same target bytes as the
client's uppercase escapes. The protocol track corrected its target comparison
in ce37580; actual retests on both TFMs confirm property/readback now passes.
No Apache raw XML is yet available, so the same cause there remains unproven.

Sabre's Sync plugin is installed but its filesystem backend is not
`ISyncCollection`. Empty report discovery plus actual DAV:supported-report and
Sabre ReportNotSupported XML establish unavailable optional sync at HTTP 415,
not a malformed media/body request. Only this exact version/profile/evidence
combination is classified unsupported; the status/cause remain, advertised sync
failures still fail, and no RFC 6578 coverage is claimed. Actual response fixtures
and their environment are retained under `interoperability\fixtures\regressions`.
Composer dependencies are now frozen with a checked-in lockfile.

`Run-Fixture.ps1 -Profile apache -CaptureSyntheticPropertyWire` (or `sabredav`)
adds a narrowly bounded raw synthetic PROPPATCH capture only after starting its
own loopback Compose fixture. The opt-in workflow enables it for these jobs and
uploads `artifacts/interop/synthetic-wire/**`. No headers or LOCKs are captured;
fresh run-owned trees, a byte cap, deadlines and independently confirmed cleanup
bound the probe. Operator endpoint results remain sanitized. Offline script tests
cover endpoint guards, unconfirmed ownership, byte caps and cleanup.

The same native Sabre fixture with core 78c9e2a now verifies **20 passed, one
evidenced optional sync rejection, zero nonpassing**, on each framework.
That second core correction retains Sabre's unescaped optional lockroot scalar
as metadata only, never a URI to follow; strict acquired lock identity and grant
validation remain. LOCK/conflict/refresh/UNLOCK, DELETE and cleanup actually
pass. The preceding ce37580 native failures and all hosted negative results are
retained, not rewritten. Core DLL hashes and harness source hashes distinguish
these native retests from a future integrated Linux-container execution.

Run 39511504385 still fails Apache after the percent-hex correction. A
publisher-checksum-verified native Apache 2.4.66 Win64 fixture now provides
concrete additional wire evidence: successful PROPPATCH returns a literal `+`
where the request uses `%2B`. Core 78c9e2a reproduces 18 passes/two optional
unavailable/one target-confirmation failure on both TFMs, with clean run trees.
This reserved-character alias is not generic RFC URI equivalence; the protocol
track owns any explicit profile-only compatibility option. Exact no-header,
no-lock synthetic XML and version provenance are checked in separately from
hosted Apache 2.4.65. The new hosted fixture probe still captures the latter's
exact wire; no native result is relabeled as Linux/container evidence.

The harness now exposes the protocol track's explicit default-off policy as
`Run-Fixture.ps1 -Profile apache -ApachePathPlusAlias` (and the same
`Invoke-Compatibility.ps1` switch). It selects typed
`PropPatchOptions.AllowPathPlusEncodingAlias` before one PROPPATCH, never retries
or changes generic URI equality. Only the Apache workflow job selects it.
Result detail discloses the profile assumption, while default failures remain
in the evidence ledger. Configuration tests enforce default-off, explicit flag
values and Apache-only scope. Query, slash, percent nesting, case and origin
distinctions remain production-core validations, not harness relaxations.

Final native Apache 2.4.66 comparisons with core 6dbc3dc verify 19 passed/two
optional unavailable/zero nonpassing with explicit alias mode on both TFMs,
while fresh strict-default runs remain 18/two/one. All four trees have successful
HTTP cleanup/absence checks. Both portable native servers are stopped, ports
closed, and only their generated fixture data removed. Their raw fixtures and
versioned positive/negative JSON remain durable. Corrected hosted Apache
2.4.65/Sabre Linux execution awaits the final explicit opt-in rerun;
nginx's conditional-write deviation remains a real failure.
