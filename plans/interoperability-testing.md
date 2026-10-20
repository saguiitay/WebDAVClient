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
Nextcloud/ownCloud/SabreDAV container execution and IIS/Windows authentication
remain explicit evidence gaps. Optional production diagnostics were deferred;
the harness writes only narrow sanitized outcome/capability data, not headers,
credentials, endpoint URLs, bodies or tokens.

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
