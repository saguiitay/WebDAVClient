# Observed SabreDAV 4.7.0 synthetic responses

Captured on 2026-10-20 from an independent, loopback-only SabreDAV 4.7.0 server
running official PHP 8.3.25 Windows NTS with its built-in HTTP server. This is
**native Windows evidence, not execution of the Linux container fixture**.
Composer 2.8.12 installed the exact dependencies now frozen in
[`composer.lock`](../../sabredav/composer.lock). The server used the same
filesystem, Basic authentication, file-lock, SQLite property-storage and Sync
plugins as the container bootstrap, with paths relocated to its own local data
directory.

The ASCII and Unicode request/response files are exact PROPPATCH XML for two
fresh synthetic files inside one random, confirmed run-owned tree. They contain
only a generated `urn:webdavclient:interop:v1` marker and resource names.
No request/response headers, credentials, tokens or LOCK responses were captured.
The probe deleted the tree with HTTP 204 and confirmed an empty backing share.

For the Unicode case the client request path was:

```text
/synthetic-wire-e128a66b8cc34563b92e7af949b6f409/caf%C3%A9%20%E4%B8%AD%E6%96%87%20%2B%20%25%20%23.bin
```

The response uses lowercase percent-escape hex digits for the same octets and
reports the requested property with HTTP 200 inside HTTP 207. A direct
`System.Uri.Equals` target comparison rejected that response. Core correction
`ce37580eceda3ce0a5e54b6425f7d4585217a980` normalizes **only percent-escape hex
case**, not path case, delimiters or nested escaped literals. An actual native
retest on both frameworks confirmed PROPPATCH/readback passes with that core.
This is not Apache wire evidence; its original failure still needs its own
fixture capture.

`sabredav-report-response.xml` is the actual HTTP 415 error from a read-only
sync-collection REPORT against this filesystem backend. The Sync plugin is
installed, but `Sabre\DAV\FS\Directory` does not implement `ISyncCollection`.
Its successful `supported-report-set` property was empty. The body explicitly
identifies `Sabre\DAV\Exception\ReportNotSupported` and `DAV:supported-report`.
The harness uses all these observations, plus the exact configured profile and
version, to distinguish an unavailable optional extension from invalid media or
a client request failure. Generic 415 responses and failures of an advertised
sync-collection report remain failures. This fixture provides **no RFC 6578
coverage**.

The corresponding backend behavior is defined in upstream SabreDAV 4.7.0
[`Sync/Plugin.php`](https://github.com/sabre-io/dav/blob/4.7.0/lib/DAV/Sync/Plugin.php)
and
[`Exception/ReportNotSupported.php`](https://github.com/sabre-io/dav/blob/4.7.0/lib/DAV/Exception/ReportNotSupported.php).
