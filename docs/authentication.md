# Authentication and transport configuration

**Example:** [Authentication.cs](../examples/WebDAVClient.Examples/Authentication.cs).
The complete handler construction for every mode is in
[Program.cs](../examples/WebDAVClient.Examples/Program.cs).
The command sends OPTIONS and prints capability advertisements, never secrets.

## Choose one mode

| `WEBDAV_AUTH` | Required configuration | Behavior |
|---|---|---|
| `anonymous` (default) | Endpoint only | No default credentials, bearer handler or cookies |
| `credentials` | `WEBDAV_USERNAME`, `WEBDAV_PASSWORD` | `HttpClientHandler.Credentials = new NetworkCredential(...)`; server challenge policy chooses Basic/Digest/Windows as available |
| `windows` | Process identity and supported platform/server setup | Explicit `UseDefaultCredentials = true`; not anonymous and not a promise of cross-platform domain authentication |
| `bearer` | `WEBDAV_BEARER_TOKEN` | Existing static-token `BearerTokenAuthenticationHandler` |
| `provider` | `WEBDAV_BEARER_TOKEN` | Async provider reads the current process environment once per request; demonstrates the provider seam, not OAuth refresh |

```pwsh
$env:WEBDAV_ENDPOINT = "https://dav.example.com/dav/"
$env:WEBDAV_AUTH = "credentials"
$env:WEBDAV_USERNAME = "your-user"
# Supply WEBDAV_PASSWORD outside source control.
dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- authentication
```

For static bearer authentication set `WEBDAV_AUTH` to `bearer` and supply a token
through your secret manager. For the provider example choose `provider`.
Mode selection is explicit: setting a username alone does not change the
executable's default anonymous mode. All authenticated modes require HTTPS.
The library itself also allows explicitly configured HTTP for compatibility;
that is not a recommendation to send credentials unencrypted.

## Modern `DavClient`

Configure authentication on a caller-owned `HttpClient`, then inject it.
The README has a complete credential example. For bearer authentication the
essential composition is:

```csharp
var auth = new BearerTokenAuthenticationHandler(token)
{
    InnerHandler = new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseDefaultCredentials = false,
        UseCookies = false
    }
};
using var http = new HttpClient(auth);
using var client = new DavClient(endpoint, http);
```

This fragment assumes `token` and `endpoint` come from validated application
configuration; import `System.Net`, `System.Net.Http`,
`WebDAVClient` and `WebDAVClient.Authentication`.
Do not set Authorization through request option dictionaries or combine
competing credential and bearer handlers. `DavClient` does not own injected
transports. Reuse the client/transport across operations and dispose only after
all outstanding operations and returned streams finish.

## App passwords are not bearer tokens

Nextcloud app passwords normally accompany a username through HTTP Basic
authentication. Select `credentials`, not `bearer`; see the
[vendor's authentication guidance](https://docs.nextcloud.com/server/latest/developer_manual/client_apis/WebDAV/basic.html).
An OAuth access token is a different credential. These configuration instructions
are not evidence that a server version has been compatibility-tested.

## Providers, refresh and secrets

The async provider receives cancellation and can run concurrently. Integrate your
identity SDK there; the library does not acquire, refresh, cache or serialize
tokens. Return a valid token or surface a refresh error. An empty/null provider
value omits Authorization, which can produce a 401 rather than a refresh.
The example's environment provider does not read future parent-shell changes;
rotation must occur inside the running process or via a real provider/secret store.

Use an SDK's supported caching/single-flight refresh if possible. The
[detailed reference](reference.md#concurrent-token-refresh) retains a bounded
coordination recipe and its cancellation/ownership caveats.

## TLS, proxies, redirects and timeouts

Configure proxy and TLS on the injected handler, **before** sending requests.
Keep platform certificate validation; install an approved CA or pin according
to policy instead of returning `true` for every certificate.
Disable redirects: handler redirects happen before the library can enforce its
origin policy. Disable automatic decompression for range-sensitive transfers.
An endpoint is not an access-control boundary within the same origin.

`HttpClient.Timeout` covers sending/headers. `DavClientOptions.MetadataTimeout`
defaults to 100 seconds after headers. Pass caller cancellation to operations
and stream reads for a whole-operation deadline.
See [errors and lifetimes](errors-and-lifetimes.md).

## Retained `Client` / `IClient`

Use `Client.CreateWithCredentials`, `CreateWithBearerToken`, or
`CreateWithBearerTokenProvider` for unambiguous construction. Existing overloads
remain shipped. `new Client()` / null credentials use Windows-default
credentials, unlike modern owned anonymous `DavClient`. Inject a no-credential
transport for explicit legacy anonymous access. Proxy/TLS/timeout options on an
owned legacy transport do not reconfigure injected handlers.
Complete compile-checked recipes remain in the
[legacy configuration reference](reference.md#configuration-recipes).
