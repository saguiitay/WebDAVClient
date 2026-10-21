# Observed native Apache PROPPATCH target spelling

These exact synthetic XML responses were captured on 2026-10-20 from Apache
Lounge **Apache 2.4.66 Win64**, built 2025-12-06, running as a single tracked
console process bound only to `127.0.0.1`. This is **not** the pinned Linux
Apache 2.4.65 container. Its older Windows archive was unavailable and the local
Docker/WSL daemon was stopped, so the separately identified native version was
used to obtain concrete independent-server evidence rather than invent a
container result.

The portable publisher archive was:

```text
https://www.apachelounge.com/download/VS17/binaries/httpd-2.4.66-251206-Win64-VS17.zip
SHA256: 2cd1f349b6705e43e784e876a233eeb1a859fa6b8abc693a91f87b35a368f7f9
```

Its SHA256 matched the publisher's HTTPS checksum document before extraction.
The fixture enabled mod_dav/mod_dav_fs, file-based Basic authentication and a
private DAV lock database, with a fresh fixture-owned directory aliased at
`/dav/`. No Windows service was installed. `fixtureProject` in `capture.json`
is only this native probe's generated correlation identifier, not a claim that
a Compose project ran.

The probe confirmed absent tree/collection ownership, uploaded two synthetic
files, and sent the same generated marker PROPPATCH for ASCII and Unicode
filenames. Both server responses are HTTP 207 containing property HTTP 200.
The Unicode response has two target-spelling differences:

* Percent-escape hex digits are lowercase.
* The request's `%2B` is a **literal `+`** in the response path.

The second difference remains after core ce37580's percent-hex correction.
With core 78c9e2a, the complete real-server suite reproduced **18 passed,
two optional unavailable/unsupported, one failed** on both frameworks; the
failure is specifically PROPPATCH target confirmation. All suite trees and
the synthetic probe tree were deleted and absence confirmed.

This is a concrete path-alias compatibility issue, **not** unsupported dead
properties, bad media type or a missing successful property outcome. RFC 3986
section 2.2 does not make reserved `+` and `%2B` universally interchangeable.
A generic decoded-URI comparison would be unsafe; an explicit server-profile
assumption must remain distinct from strict default target identity.

The exact request paths, statuses, response sizes and cleanup are in
`capture.json`. No headers, credentials, cookies, LOCK requests/responses or
tokens are included. Hosted Apache 2.4.65's same failure phase remains recorded
separately, including run 39511504385 after the percent-hex fix. Its exact XML
must still be captured by the new loopback-only fixture probe before claiming
byte-for-byte confirmation for that particular build.

Final native comparison with core 6dbc3dc and integrated harness af576a4:
strict default still records **18/2/1**, while explicit
`PropPatchOptions.AllowPathPlusEncodingAlias=true` records **19/2/0**, on both
frameworks. PROPPATCH/readback succeeds only in the disclosed configured mode;
neither the outbound target nor raw href is changed and no mutation is replayed.
All four run trees were deleted over HTTP with absence confirmed. The native
server was then stopped, its port confirmed closed, and its own generated site,
fixture account file and lock database removed. See the versioned result files
and `apache-native-core-6dbc3dc-environment.json` for provenance.
