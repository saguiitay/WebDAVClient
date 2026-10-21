# Independent WebDAV interoperability suite

This is an **opt-in mutating compatibility suite**, not a complete RFC 4918
conformance certification. **WebDAVClient 3.2.0 is published on NuGet**; this
checkout adds separate, nonpackable compatibility tooling.
[Checked-in evidence](results/compatibility-matrix.json) records
actual observations, including missing server evidence. Unit tests and loopback
test doubles are never counted as independent-server results.
Historical source revisions, server versions and raw fixtures remain unchanged;
they are not reruns or certification of the published 3.2.0 package.

## Run an isolated fixture

Requires both .NET 8 and .NET 10 SDKs, PowerShell 7, and either Python 3.12 or a
working Linux Docker daemon. From the repository root on Windows:

```powershell
py -m venv interoperability\.venv
interoperability\.venv\Scripts\python.exe -m pip install -r interoperability\fixtures\wsgidav\requirements.txt
.\interoperability\Run-Fixture.ps1 -Profile wsgidav
.\interoperability\Run-Fixture.ps1 -Profile wsgidav -Authentication anonymous
```

On Linux, use `python -m venv interoperability/.venv` and
`interoperability/.venv/bin/python -m pip install -r interoperability/fixtures/wsgidav/requirements.txt`,
then invoke the same PowerShell script. The workflow does this automatically.

`Run-Fixture.ps1` starts an independent server with ephemeral credentials, waits
for authenticated readiness, runs **both TFMs**, and tears down in `finally`.
Readiness uses OPTIONS=200 for DAV profiles, not a directory GET (a valid
filesystem DAV collection need not implement GET). Built-in nginx instead uses
GET=200 because it does not implement OPTIONS. The sanitized
`<profile>-fixture-startup.json` artifact records method/status/attempts without
credentials, endpoints or response bodies, including when startup fails.
WsgiDAV also verifies its backing share is empty. Results default to
`artifacts\interop\*.json`. No package publication is performed.

For Docker: `.\interoperability\Run-Fixture.ps1 -Profile apache`
(or `nginx`, `sabredav`, `nextcloud`, `owncloud`). The Compose project name is
unique per invocation, ports bind **only** `127.0.0.1`, storage is fixture-owned,
and `down --volumes` removes only that Compose project's resources. A busy fixture
port fails before startup. No privileged containers, host networking, host
document-directory mounts, or public destructive endpoints are used.

The dedicated **Opt-in independent WebDAV interoperability** GitHub workflow
supports manual `workflow_dispatch` for one selected fixture, or applying the
exact PR label **`run-interoperability`** to run WsgiDAV, Apache, nginx and
SabreDAV as independent jobs. Each job exercises both TFMs, and `fail-fast: false`
retains evidence from other fixtures when one fails. Nextcloud and ownCloud remain
manual-only choices.
The Apache job explicitly selects the path-plus compatibility option below;
this is not evidence that strict default URI target matching accepts aliases.

The PR trigger is unprivileged `pull_request` with `types: [labeled]`, gated to
that exact newly applied label. It uses read-only `contents` permission, no PR
secrets, and checkout credential persistence is disabled. Ordinary pushes, PR
updates, and unrelated labels do not run the suite; to rerun after an update,
remove/reapply the label explicitly. This workflow is separate from publish CI.
Artifacts are named
`interoperability-<server>-<run_id>-<run_attempt>` and contain
`artifacts/interop/*.json`, including a schema-validated compatibility summary.
Apache/SabreDAV jobs also retain `artifacts/interop/synthetic-wire/**`, the
explicit fixture-only diagnostic described below.
Choosing a candidate fixture does not establish that it starts successfully:
startup failures and missing result artifacts must not be reported as a green
server.

## Existing operator-owned servers

Use a disposable account/collection with permission to create and delete children.
Do not use public demo servers or a valuable document tree.

```powershell
$env:WEBDAV_INTEROP_ENDPOINT = 'https://dav.example.test/isolated-test-collection/'
$env:WEBDAV_INTEROP_ALLOW_REMOTE = '1'
$env:WEBDAV_INTEROP_AUTH = 'basic'
$env:WEBDAV_INTEROP_USER = 'dedicated-test-user'
$env:WEBDAV_INTEROP_PASSWORD = '<supply securely at runtime>'
$env:WEBDAV_INTEROP_VERSION = '31.0.9'
.\interoperability\Invoke-Compatibility.ps1 -Profile nextcloud
```

The profiles file supplies localhost ports/paths and pinned labels only. An
operator override is a declaration, not automatic server version detection. Record
the actual server build/configuration separately. Endpoint URLs must end in `/`
and may not contain user-info, queries or fragments. Remote access requires both
HTTPS and explicit opt-in. Loopback HTTP Basic is allowed for disposable local
fixtures only. TLS verification remains enabled. Redirects, cookies, system
proxies and implicit/default credentials are disabled. `windows` explicitly opts
into process-identity credentials; see [IIS.md](IIS.md).

For the executable directly, set `WEBDAV_INTEROP_RUN=1`, `ENDPOINT`, `SERVER`,
`VERSION`, and optionally `AUTH`, `USER`, `PASSWORD`, `OUTPUT`, `REVISION`
(all with the `WEBDAV_INTEROP_` prefix), then:

```powershell
dotnet run --project tests\WebDAVClient.InteropTests -c Release -f net8.0 -- --run
dotnet run --project tests\WebDAVClient.InteropTests -c Release -f net10.0 -- --run
```

Without `--run` **and** the mutation opt-in the executable makes no requests.
`--help` and ordinary `dotnet test` are offline even when the integration
environment variables are present. The project is nonpackable; production
dependencies and the production-module coverage gate are unchanged.

## What is checked

* OPTIONS capabilities are recorded separately from verified operations.
* HEAD confirms the random run-tree path is absent, then MKCOL must confirm
  ownership. No mutation targets the configured collection itself.
* Conditional full PUT, byte-exact GET and HEAD length, inclusive byte-range GET,
  and a second conditional PUT rejected with 412 (followed by integrity GET).
* Unicode/spaces/plus/literal-percent/hash filenames are escaped once; full streamed
  PROPFIND must complete and return the child href.
* Named PROPFIND must preserve a successful content-length beside a missing
  property's 404. A 207 is not a blanket pass.
* Dead-property set/readback, COPY byte integrity and no-overwrite rejection,
  MOVE byte integrity and disappearance of its source, DELETE and verified absence.
* Exclusive LOCK, conflicting unconditioned DELETE=423, refresh, and UNLOCK.
  A failed intermediate check still attempts independent UNLOCK during cleanup.
  A successful acquisition with one absolute bare response-header token matching
  exactly one discovery token records only the static wire-deviation code
  `unbracketed-lock-token-confirmed`; the token itself is never persisted. This
  observation does not relax or replace the library's lock validation.
* For configured authentication, an unauthenticated HEAD of a run-owned file must
  return 401 with the configured scheme. No bad-password/lockout probing is done.
* RFC 6578 discovery is distinct from REPORT. An advertised REPORT method does
  not establish support for the sync-collection report. On supported servers the suite
  requires an initial token, incremental creation with ETag, and a 404 deletion
  member. This is one flat collection batch, not durable synchronization.

All requests have timeouts and a four-minute run deadline. Cleanup gets a separate
20-second deadline, only deletes a **confirmed** random run-owned tree, and checks
HEAD=404. Cleanup errors make the run nonpassing. Ambiguous MKCOL failures are not
deleted because ownership was not confirmed; inspect the matrix's `runTree`
manually if a network failure may have occurred after server-side creation.
Interrupting/killing the whole process can prevent cleanup; fixture teardown is a
second layer, not a substitute for checking remote run trees.

## Matrix interpretation

`result.schema.json` is the versioned machine-readable contract. JSON records
schema/suite versions, UTC time, client source revision, runtime/OS/architecture,
server/configured version, authentication, transport, advertisement, exact outer
and member/property status codes, outcomes, and confirmed cleanup.

| Outcome | Meaning |
| --- | --- |
| `passed` | The operation's semantic assertions succeeded, including expected rejection statuses. |
| `unsupported` | Optional operation returned 405/501 **and was not advertised**, a Range request was ignored with 200, or the narrowly evidenced Sabre filesystem rejection below applies. Not a pass. |
| `unavailable-property` | Requested `supported-report-set` property returned 404 inside 207. Does not establish REPORT support or lack thereof. |
| `denied` | 401/403/407; never silently called unsupported. |
| `redirected` | 3xx with following disabled; not a pass. |
| `failed` | Unexpected status, false advertisement, malformed/incomplete XML, wrong bytes, or failed semantic/member assertions. |
| `timed-out` / `blocked` | Incomplete observation; never supported/unsupported evidence. |

Exit 0 means all **required** operations passed and optional probes were either
verified, explicitly unsupported, or their discovery property unavailable. It
does **not** mean all capabilities are supported. Exit 1 includes failed/denied/
blocked/timed-out observations; exit 2 means configuration/argument rejection.
The suite preserves an absent Allow entry as unknown and still probes operations.
An advertised operation returning 405/501 is a failure, not unsupported.

The matrix intentionally excludes endpoint host/path, usernames, request/response
bodies, cookies, authentication headers, lock/sync tokens, and exception messages.
Only run-generated paths and static assertion/type codes are persisted. Capability
tokens are server-controlled data: use only a trusted operator-owned test server
and review JSON before publishing. There is no new production diagnostics API.
Protocol failures retain only allowlisted local error codes and exception types.
The property probe also records whether PROPPATCH or PROPFIND readback failed;
arbitrary exception/inner-exception messages remain redacted.

### Explicit synthetic wire diagnostic

For an exact raw reproduction of a fixture protocol error, opt in with:

```powershell
.\interoperability\Run-Fixture.ps1 -Profile apache -CaptureSyntheticPropertyWire
.\interoperability\Run-Fixture.ps1 -Profile sabredav -CaptureSyntheticPropertyWire
```

The switch is accepted only for these two profiles and only after this invocation
has started its own isolated Compose fixture and passed readiness. It never
applies to `Invoke-Compatibility.ps1` or an operator-owned endpoint. The probe
additionally requires the exact fixture `127.0.0.1` HTTP port/path, confirms
HEAD=404 then MKCOL=201, and creates fresh ASCII/Unicode files under a separate
random tree. It captures only synthetic PROPPATCH XML bodies and generated
request paths, never headers, credentials, cookies or lock tokens; it cannot
issue LOCK/UNLOCK. Responses are bounded to 65,536 bytes each, with a 30-second
overall deadline and an independent 10-second cleanup requiring DELETE=204 and
HEAD=404. An incomplete capture or cleanup fails explicitly. Raw diagnostics
are **not** a compatibility pass. The ordinary suite still runs if the diagnostic
fails; a diagnostic failure remains nonpassing even if the ordinary suite passes.

The PR-label workflow enables this probe for Apache/SabreDAV before the normal
suite, so another run yields exact wire evidence rather than just exception
types. Reviewed native Sabre examples live in
[`fixtures/regressions/sabredav-4.7.0`](fixtures/regressions/sabredav-4.7.0).
This explicitly scoped exception does not enable raw diagnostics for real
operator endpoints; their normal result JSON remains sanitized.

### SabreDAV filesystem sync rejection

The pinned 4.7.0/PHP 8.3.25 profile installs the Sync plugin, but its filesystem
directory does **not** implement `ISyncCollection`. Only an observed HTTP 415
with a successful **empty** supported-report-set, no advertised sync-collection,
and a DAV error body containing exactly the expected `DAV:supported-report`
marker and `Sabre\DAV\Exception\ReportNotSupported` is classified as optional
sync unavailability. The configured profile/version must also match. The 415 and
concrete cause remain in the matrix. Missing discovery/body evidence, other 415s,
and advertised sync failures are not accepted. No RFC 6578 behavior is covered
by this backend.

### Explicit Apache path-plus profile

Apache's filesystem backend can spell a requested `%2B` as a literal `+` in a
PROPPATCH result href. Reserved characters are not universally interchangeable
under RFC 3986, so **strict default target identity remains unchanged**.
For a known Apache filesystem configuration, explicitly select:

```powershell
.\interoperability\Run-Fixture.ps1 -Profile apache -ApachePathPlusAlias -CaptureSyntheticPropertyWire
```

For an existing disposable Apache endpoint use
`Invoke-Compatibility.ps1 -Profile apache -ApachePathPlusAlias`; the same
endpoint/authentication safeguards still apply. The executable directly uses
`WEBDAV_INTEROP_APACHE_PATH_PLUS_ALIAS=1`. Only the exact `apache` profile accepts
the flag; absent/`0` remains strict and ambiguous values are rejected.
Wrappers require the switch rather than silently inheriting an environment flag.

This passes `PropPatchOptions.AllowPathPlusEncodingAlias=true` to the **single
initial** PROPPATCH call and records the explicit profile assumption in its
result detail. There is no automatic replay or fallback after a rejection.
It affects only path `+`/`%2B` target confirmation, never query data, encoded
slashes, nested percent escapes, other reserved characters, path case or origin.
Outbound URI/raw server hrefs are not rewritten. The workflow opts in only for
its known Apache fixture; no other server or operation inherits the policy.
Strict-default failure evidence is retained separately from configured-mode
results. This profile requires core commit 6dbc3dc or its integrated equivalent.

After reviewing new result files, retain them in `interoperability\results` and
run `.\interoperability\Update-Matrix.ps1`. This validates each run against the JSON
schema and regenerates the summary without turning missing server rows green.
The workflow applies the same validation to its artifact directory.
The summary is an all-history ledger: **any retained failed run** leaves that
server's summary `observed-nonpassing`, even when a later corrected/configured
run succeeds. Inspect individual run timestamps, versions and provenance rather
than deleting negative history to make the summary green.

## Fixture status and limits

| Profile | Configuration | Execution evidence |
| --- | --- | --- |
| WsgiDAV 4.3.5 / Cheroot 11.1.2 | Python 3.12, property manager and exclusive locks; anonymous and Basic profiles | Windows anonymous/Basic and hosted Linux Basic, both TFMs; checked-in JSON |
| Apache mod_dav 2.4.65 | Digest-pinned official httpd; DAV filesystem + lock DB, Basic, `/dav/` | Retained hosted strict-default runs: 18 passes/two optional unavailable/one target failure. Separately versioned native 2.4.66 with explicit path-plus option: 19 passes/two optional unavailable/zero nonpassing, both TFMs and cleanup; corrected Linux profile awaits execution |
| nginx 1.28.0 | Digest-pinned official nginx; built-in DAV module only, anonymous, `/` | Hosted Linux, both TFMs: 12 passes, seven unsupported probes, conditional PUT returned 204 instead of required 412; remains a failure |
| SabreDAV 4.7.0 / PHP 8.3.25 | Digest-pinned PHP/Composer, frozen Composer dependencies, Basic, filesystem, lock/property storage, Sync plugin | Hosted OPTIONS readiness confirmed; retained container runs fail. Native Windows with corrected core 78c9e2a: 20 passes/1 evidenced sync-unavailable/0 nonpassing, both TFMs and confirmed cleanup; not a corrected Linux-container run |
| Nextcloud 31.0.9 | Digest-pinned official image, disposable SQLite install, dedicated Basic account, `/remote.php/dav/files/{user}/` | Candidate, not run |
| ownCloud 10.15.0 | Digest-pinned official server image, disposable SQLite, Redis disabled, dedicated Basic account | Candidate, not run |
| IIS | Operator-owned Windows site, explicit Windows/Basic | Not run; no IIS/Windows-auth fixture available |

All six image tags were resolved against the registry with
`docker buildx imagetools inspect` on 2026-10-20; the resulting immutable digests
are in the fixtures. Registry manifest inspection is **not container execution**.
WsgiDAV's transitive dependency versions are frozen in `requirements.txt`.
SabreDAV's Composer lockfile freezes its seven PHP dependencies; PHP and Composer
images are digest-pinned. Its image build
succeeded in hosted run 39505211029. These old disposable fixture versions are
not deployment advice.

Hosted runs **39505211029**, **39509017232** and **39511504385** are retained as evidence,
including negative outcomes. Apache's remaining failure is specifically
PROPPATCH target/property validation at HTTP 207; no raw Apache XML is yet
available from that exact Linux build. Native Sabre XML identifies a real client target-comparison
bug (percent-escape hex case), corrected separately in core ce37580 and verified
with actual PROPPATCH/readback on both TFMs. This is not yet proof of Apache's
exact cause or correction. A separately versioned, publisher-checksum-verified
**native Apache 2.4.66 Win64** now reproduces the same remaining failure on both
frameworks: its exact successful property XML spells the request's `%2B` as a
literal path `+`, in addition to lowercase percent escapes. This is a reserved
character alias, not universally equivalent URI syntax, and strict default
target matching still rejects it. The
[exact synthetic wire](fixtures/regressions/apache-2.4.66-native) makes the
additional mismatch concrete; a safe explicit profile decision belongs in core,
not a blanket decoded-URI comparison or an “unsupported” harness workaround.
The new fixture-only capture obtains the still-missing exact Linux 2.4.65 wire.
With core 6dbc3dc, actual native 2.4.66 retests preserve **18/2/1** under strict
defaults and verify **19/2/0** with the explicit path-plus profile on each
framework. All four run trees were deleted over HTTP and absence confirmed.
The wrapper's default stayed strict even with an inherited alias environment
flag. Source revision af576a4 identifies the integrated harness/core sources
used for these comparisons; the environment companion records DLL hashes.
Sabre LOCK still failed on ce37580 and caused DELETE/cleanup 423; those native
failures are retained. The server supplies a **raw decoded, unescaped** optional
lockroot (including spaces, Unicode, literal percent and hash), not a usable URI.
Core correction 78c9e2a preserves that scalar only as evidence and never follows
it; token/header/body identity, grant scope/depth/timeout and metadata cardinality
remain validated. With that core, native Sabre passes **20 checks, one evidenced
optional sync rejection, zero nonpassing**, on both TFMs. Exclusive LOCK,
conflicting DELETE=423, refresh, UNLOCK, final DELETE=204 and cleanup HEAD=404
were actually verified. Environment companions record exact core revisions and
DLL/source hashes, since these retests combined this harness with separately
built corrected core DLLs rather than claiming an integrated repository revision.
They do not replace the retained failed hosted results.
nginx's ignored
conditional PUT is a server compatibility restriction, not made green by changing
configuration or expectations. All completed Apache/nginx runs verified cleanup.

Do not rely on built-in nginx DAV for safe preconditioned creation/replacement
where conditional writes are ignored. A stale `If-Match` returning 204 is also a
server deviation, not success of the intended guard. Use a server/configuration
that enforces the required `If-Match`/`If-None-Match` behavior. A preliminary HEAD
or ETag comparison cannot close the race before an unconditional PUT, and this
suite does not retry without the condition or silently skip these assertions.

No passing Apache 2.4.65 Linux-container, nginx or corrected SabreDAV **Linux-container** profile, HTTPS
or Windows-authentication run is asserted here. The local Docker daemon remains unavailable; hosted evidence does
not imply local container execution.
Shared locks, lock-token conditioned writes, partial PUT, large/deep trees,
chunking extensions, ACLs, quotas, CalDAV/CardDAV and full RFC conformance remain
outside suite v1. Keep a failing result rather than widening “unsupported” to
hide a real rejection or removing a server from the evidence ledger.
Both native diagnostic server processes were stopped, their loopback ports
confirmed closed, and only their generated fixture data/accounts/lock databases
removed. Native passing runs retain their successful HTTP cleanup; earlier
Sabre cleanup failures remain failed despite subsequent fixture destruction.
