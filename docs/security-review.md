# Critical security review

Reviewed 2026-10-19 at revision `ac0ef8a7b637b03114cf18a16a1e60d9a53396a1`
by a dedicated security-review agent.

## Remediation status

**SEC-01 is fixed in the 2.8.0 source changes.** The findings below describe the
reviewed revision, not the remediated implementation.

- Absolute resource URLs must match the configured scheme, hostname, and effective
  port, including `Port` overrides. Discovered absolute roots are checked before
  caching; absolute operation targets are checked before follow-up requests.
- PROPFIND response boundaries are anchored to `DAV:multistatus`, and resource
  hrefs must be direct `DAV:href` children of the actual `DAV:response`.
  Nested property structures and foreign-namespace lookalikes cannot replace them.
- Regression coverage uses the real bearer handler with an in-memory transport,
  covering poisoned discovery, unsafe child download/upload targets, all parser
  modes, default ports, port overrides, and legitimate same-origin URLs.
- Explicitly configured HTTP and the standalone bearer handler retain their
  existing contract. Applications should configure HTTPS for credentials; the
  optional blanket HTTPS-only policy is not needed to close this vulnerability.

Validation: the expanded Release suite passes **264/264 tests on each of net8.0
and net10.0**, including 26 security regression cases. The initial 22-case
regression suite failed 16 cases per framework before remediation. No live
credentials or external requests were used.

## Outcome

**No critical vulnerability was confirmed. One high-impact credential-disclosure
chain was reproduced using an in-memory transport and a dummy token.**

| # | Severity | File | Lines | Vulnerability | Confidence |
|---|----------|------|-------|---------------|------------|
| 1 | 🟠 HIGH | `WebDAVClient\Client.cs`; `WebDAVClient\Helpers\ResponseParser.cs`; `WebDAVClient\Authentication\BearerTokenAuthenticationHandler.cs` | 1255-1263,1302-1307; 179-186; 55-61 | Untrusted XML can downgrade HTTPS and expose bearer tokens | 9/10 |

## SEC-01: Server-controlled hrefs can disclose bearer tokens

### Evidence and root cause

- `WebDAVClient\Helpers\ResponseParser.cs:179-186` assigns `Item.Href` for a
  nested element named `href` without requiring the DAV namespace or a direct
  child of the enclosing DAV response.
- `WebDAVClient\Client.cs:1243-1245` caches the parsed root href.
- `Client.cs:1255-1263` validates hostname only, not scheme or effective port.
- `Client.cs:1302-1307` adopts the server-derived scheme and port.
- `WebDAVClient\Authentication\BearerTokenAuthenticationHandler.cs:55-61`
  attaches the token to the resulting request.

### Prerequisites and impact

The victim uses bearer authentication. An attacker must influence returned
PROPFIND XML, through a malicious server or a writable structured dead property
returned by allprop. A plaintext endpoint must be reachable on the same hostname,
and the attacker must observe that traffic or control an accepted alternate port.

Poisoning initial discovery requires control over the base-resource response.
Poisoning a listed child requires the consumer to subsequently use its href.
The impact is a reusable bearer-token disclosure, bounded by token permissions
and expiry. Uploads may also be redirected to plaintext transport.

### Reproduction

A legitimate `/dav/` href followed by this custom property overwrites the parsed
resource URL:

```xml
<X:payload xmlns:X="urn:attacker-property" xmlns:D="DAV:">
  <D:href>http://example.com:8123/stolen/</D:href>
</X:payload>
```

The agent compiled current tracked sources in memory and exercised `Client.List`
through the real bearer handler and an in-memory recording transport:

```text
PROPFIND https://example.com/dav/ AuthPresent=True
PROPFIND http://example.com:8123/stolen/ AuthPresent=True
```

Direct URL-builder checks accepted same-host HTTP and alternate-port URLs,
while rejecting a different hostname. No credentials were sent externally.

### Recommendations

1. Validate server-derived URLs against the configured origin: scheme, hostname,
   and effective port, accounting for the public `Port` override.
2. Reject HTTPS-to-HTTP transitions.
3. Parse resource hrefs only from correctly namespaced `DAV:href` directly under
   the corresponding `DAV:response`, never from arbitrary property contents.
4. Consider requiring HTTPS for bearer authentication unless insecure transport
   is explicitly enabled.

### Regression acceptance

- Nested property hrefs cannot replace the resource href.
- HTTPS clients reject same-host HTTP and unauthorized alternate-port hrefs.
- Cover poisoned initial discovery and poisoned child hrefs.
- Rejected URLs never reach transport with authorization or upload content.
- Preserve legitimate same-origin absolute hrefs and case-insensitive hostnames.

## Validation and limitations

The reproduction verified URL selection and authorization attachment, not a live
server or packet capture. The shared-resource scenario requires a server returning
attacker-writable structured dead properties; individual server compatibility was
not tested. Parent baseline: 238 tests passed on each of net8.0 and net10.0 after
restoring a missing dependency. Those tests do not cover or disprove this finding.
No other standalone hardening recommendation was elevated to a vulnerability.
