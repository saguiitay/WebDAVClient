# IIS operator-owned profile (not executed)

IIS cannot be represented by a Linux container. No IIS/Windows-authentication
compatibility result is currently claimed.

Use a disposable Windows VM with the IIS Web Server, WebDAV Publishing, and
Windows Authentication role services installed. In IIS Manager:

1. Create a new dedicated site on a loopback binding, backed by an empty directory.
   Do **not** point the test suite at an existing document library.
2. Enable WebDAV for that site, add an authoring rule granting the intended test
   identity Read, Write and Source, and grant that identity filesystem Modify
   permission **only** to the site's dedicated directory.
3. Disable Anonymous Authentication and enable Windows Authentication for that site.
   For Basic instead, enable Basic Authentication and use HTTPS unless loopback.
   Never disable certificate validation to make this test work.
4. Ensure request filtering allows the DAV methods and the Unicode filename used by
   the suite. Record the Windows build, IIS version, authoring/request-filter rules,
   and authentication provider order with the sanitized results.
5. Run as the intended test identity. `windows` is an explicit opt-in to sending the
   process identity; it is never the suite default.

```powershell
$env:WEBDAV_INTEROP_ENDPOINT = 'http://localhost:19087/'
$env:WEBDAV_INTEROP_VERSION = '10.0-windows-build-REPLACE'
$env:WEBDAV_INTEROP_AUTH = 'windows'
.\interoperability\Invoke-Compatibility.ps1 -Profile iis
```

Supply a verified numeric/build label rather than leaving the placeholder.
Remote testing requires `WEBDAV_INTEROP_ALLOW_REMOTE=1` and a trusted HTTPS
endpoint. The profile does not install roles, change machine-wide settings, create
Windows users, or grant permissions. Remove the VM/site after collecting results.
Unauthenticated requests must be challenged with 401/Negotiate or NTLM for the
authentication-boundary check. An anonymous-readable but write-protected deployment
is a different configuration, not a passing instance of this profile.
