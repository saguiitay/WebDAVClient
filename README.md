# WebDAVClient

## Overview

WebDAV Client for .Net Core, strongly typed, async and open-sourced, implemented in C#.

WebDAVClient is based originally on <https://github.com/kvdb/WebDAVClient>. I've added Async support (instead of Callback), as well strong-types responses.

## NuGet

WebDAVClient is available as a [NuGet package](https://www.nuget.org/packages/WebDAVClient/)

## Quickstart

```pwsh
dotnet add package WebDAVClient
```

Current source targets **.NET 8 and .NET 10**. Version 2.7.0 is already released;
2.8.0 additions described here are upcoming, not a claim of feed publication.
The install command selects the published package. This read-only anonymous
example uses the existing injected-transport API:

```csharp
using System;
using System.Net.Http;
using System.Threading;
using WebDAVClient;

using var http = new HttpClient(new HttpClientHandler { UseDefaultCredentials = false });
using IClient client = new Client(http)
{
    Server = "https://dav.example.com",
    BasePath = "/dav/"
};
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var count = 0;
foreach (var item in await client.List(cancellationToken: cancellation.Token))
{
    Console.WriteLine($"{(item.IsCollection ? "Folder" : "File")}: {item.DisplayName}");
    count++;
}
if (count == 0)
    Console.WriteLine("This directory is empty.");
```

### Paths

`Server` is the HTTP(S) server URL without a trailing slash; `BasePath` is the
directory used as root and normalizes to `/.../` (empty means `/`). Prefer
origin-only `Server` and put directory components in `BasePath`. Legacy
hostname server values retain `UriBuilder`'s HTTP default; prefer an
explicit HTTPS server URL for credentials. Resource paths
are resolved against the server-discovered root, which may differ after server
canonicalization. Assuming that root is `/dav/`:

| Input | Requested resource |
|---|---|
| `"reports/file.txt"` or `"/reports/file.txt"` | `/dav/reports/file.txt` |
| `"/dav/reports/file.txt"` (already rooted) | `/dav/reports/file.txt`, not `/dav/dav/...` |
| A returned `Item.Href` | Reusable directly; collections retain their trailing `/` |
| `"https://dav.example.com/other/file.txt"` | Same-origin absolute URL, unchanged; BasePath is not an access-control boundary |
| `null`, `""`, or `"/"` for metadata reads | Configured root |

Absolute URLs must match scheme, hostname and effective port. Required mutation
and download paths reject null; use an explicit resource path. Folder operations
normalize trailing slashes, while file hrefs do not end in `/`.

### Authentication

| Mode | Construction and behavior |
|---|---|
| Anonymous | Inject `HttpClient` with `UseDefaultCredentials = false` and no credentials/auth handler, as above |
| Windows-default credentials | `new Client()` or `new Client(credential: null)`; null is not anonymous |
| Basic/Digest/Windows explicit credentials | `new Client(new System.Net.NetworkCredential(user, password))`; HTTPS and server challenge policy apply |
| Static bearer | `new Client(bearerToken: token)` |
| Refreshable bearer | `new Client(bearerTokenProvider: provider)`; provider receives cancellation and must handle concurrent calls |

Version 2.8.0 also adds unambiguous `CreateWithCredentials`,
`CreateWithBearerToken` and `CreateWithBearerTokenProvider` factories. Do not
remove shipped constructor overloads to resolve null ambiguity. Use HTTPS for
credentials. See [configuration recipes](#configuration-recipes) for proxy,
certificate validation, provider integration and distinct timeout budgets.

### Ownership

| Resource | Who disposes it? |
|---|---|
| Client-created HTTP clients/handler | `Client.Dispose()`; scope the client |
| Injected `HttpClient` | Caller; disposing Client leaves it usable |
| Injected `IHttpClientWrapper` | Caller, according to its policy; disposing concrete `HttpClientWrapper` disposes the HTTP clients supplied to that wrapper |
| Upload input stream | Request owns it once created, including failure/cancellation; retain a caller `using` for pre-request failures and reopen for retry |
| Download result stream | Caller; disposing it also disposes its HTTP response/content |

No automatic mutation retries are performed. Boolean operations return true on
confirmed success and throw on rejection; handle `WebDAVException` rather than
checking for false.

## Features

* Available as a NuGet package
* Fully support Async/Await
* Strong-typed
* Implemented using HttpClient, which means support for extendibility such as throttling and monitoring
* Authentication: unauthenticated, Windows / Basic / Digest (via `ICredentials`), and Bearer / OAuth 2.0 (via static token or async refreshable provider)
* Supports custom Certificate validation
* Supports these WebDAV operations:
  * Retrieving files & folders
  * Listing items in a folder (`<allprop/>`, targeted `<prop>`, and `<propname/>` PROPFIND variants)
  * Creating & deleting folders
  * Downloading & uploading files
  * Downloading & uploading partial content
  * Moving & copying files and folders (with the `Overwrite` header and optional destination lock-token)
  * Locking & unlocking resources (LOCK / UNLOCK / RefreshLock) with a strongly-typed `LockInfo`
  * Setting & removing custom (dead) properties via PROPPATCH
  * Discovering server capabilities via OPTIONS (DAV compliance classes + allowed methods)
  * Submitting WebDAV lock tokens through the `If` header on PUT / DELETE / MOVE / COPY

## Tests and coverage

The MSTest suite runs on both .NET 8 and .NET 10 without an external WebDAV
server. It covers protocol outcomes, XML structure, cancellation and deadlines,
stream ownership, authentication, configuration changes, and sample CLI behavior.
Bounded loopback tests also exercise actual HTTP transport and authentication.

```pwsh
dotnet test WebDAVClient.UnitTests\WebDAVClient.UnitTests.csproj -c Release -p:CollectCoverage=true
```

Coverage collection enforces **98% line and 95% branch coverage in each production
module**, independently for each framework, including the sample executable.
Cobertura reports are written under
`WebDAVClient.UnitTests\TestResults\coverage\<framework>\`.
CI runs both frameworks and uploads these reports even when a test or coverage
gate fails; a failed gate prevents publication. No production-code exclusions
are added to reach the thresholds.

Measured on both frameworks: **533 passing cases per framework**, with library
coverage of **99.53% lines / 97.18% branches** and sample coverage of
**100% lines / 98.78% branches**. Both modules have **100% method coverage**.
Coverage is a regression signal, not proof of correctness; unexecuted defensive
branches are not forced through reflection just to reach 100%.

## Release Notes

+ **2.8.0** _(upcoming)_ Security, correctness, and reliability fixes:
  - PROPPATCH outcomes and descriptions come only from direct `propstat` children; nested property values cannot overwrite success or failure.
  - Sample transfer-integrity failures produce an explicit failure exit and retain run-owned cleanup instead of escaping unhandled.
  - Configuration recipes cover owned/injected authentication, proxy, TLS and distinct deadlines; ineffective TLS callback settings on injected transports now fail explicitly before I/O.
  - Packages include XML API documentation beside both framework assemblies, including parameter, stream-ownership and exception contracts.
  - WebDAV exceptions include request-local operation, method and sanitized resource context without changing existing status codes or conflict specialization.
  - Targeted/property-name results expose qualified `PropertyStatuses`, distinguishing absent (404), denied (403), dependency failure (424), and omitted statuses without changing legacy bags.
  - Invalid transports, configuration, required paths, upload inputs and named-property lists fail with parameter-specific argument errors before discovery; null/empty metadata paths retain root behavior.
  - Manual examples are runnable, read-only by default, configurable without source edits, and isolate opt-in mutations with scoped streams and bounded cleanup.
  - Concurrent cold operations coordinate one root discovery per connection configuration. Cancelling a waiter does not cancel discovery; cancelled or failed owners let another caller retry, and reconfiguration is not blocked by obsolete discovery.
  - Listings filter their parent in place, property-name responses skip unused values without materializing XML, and single-item lookups retain only the selected item while still validating the complete response.
  - Uploads complete after response headers instead of buffering unused PUT response bodies; success/error responses are still disposed.
  - DELETE/MOVE/COPY still reject partial `207` results, but `WebDAVException.ResourceStatuses` now preserves member-level recovery outcomes instead of discarding them.
  - Disposing a download stream (including `DisposeAsync`) now explicitly disposes its HTTP response and custom content resources exactly once.
  - Metadata (PROPFIND, LOCK/refresh, PROPPATCH) is parsed asynchronously with caller cancellation and an independent, configurable `Client.ResponseBodyTimeout` (100 seconds by default). Stalled bodies cancel and release responses rather than blocking indefinitely.
  - Initial root discovery observes caller cancellation, sends nothing for an already-cancelled operation, and does not cache cancelled discovery.
  - Every operation consistently maps HTTP 409 to `WebDAVConflictException`; other failure codes remain available through `WebDAVException.GetHttpCode()`.
  - MOVE and COPY dispose response messages and content on both success and failure.
  - PROPFIND surfaces embedded resource errors as status-bearing exceptions (including conflicts), fails mixed listings rather than silently returning partial success, and ignores failed property values in every mode. Legacy allprop responses omitting propstat status remain supported.
  - OPTIONS capability discovery sends OPTIONS directly to the configured resource without requiring PROPFIND, and no longer adds a slash to file targets.
  - Partial downloads require `206` with a matching byte `Content-Range` (including valid EOF clipping). Ignored or mismatched ranges now throw instead of silently returning a complete file.
  - Sparse and property-name listings exclude their parent by resource identity and preserve slash-terminated collection hrefs when type values are unavailable.
  - Path-only and absolute discovery roots now share consistent URL joining: no duplicated boundary separators or base paths, while interior separators remain unchanged.
  - Changing `Server`, `BasePath`, or `Port` invalidates the resolved root; obsolete in-flight discovery results cannot overwrite the new configuration.
  - Targeted PROPFIND text values are XML-decoded (including CDATA) rather than returned escaped; structured properties retain their inner XML.
  - Hidden metadata honors `0`/`1` and `false`/`true` values in both allprop and targeted responses; empty or unrecognized values remain false.
  - Targeted PROPFIND requests now support properties without an XML namespace, including mixed-namespace requests.
  - Versioning properties no longer cause the parser to skip metadata immediately following their XML subtrees.
  - Partial uploads preserve their exclusive-end API contract but now send the correct inclusive HTTP endpoint and validate the bytes remaining from the stream's current position.
  - Absolute resource URLs must match the configured origin (scheme, hostname, and effective port, including `Port` overrides). Server-returned metadata can no longer downgrade HTTPS to HTTP or redirect requests to another port on the same host.
  - PROPFIND resource identities are read only from direct `DAV:href` children of `DAV:response` under `DAV:multistatus`. Nested property URLs, response elements, and foreign-namespace lookalikes cannot replace resource identities or typed DAV metadata.
  - Same-origin absolute URLs, case-insensitive hostnames, and explicit default ports remain supported. Cross-origin absolute URLs now throw `InvalidOperationException` before follow-up I/O.
+ **2.7.0** Security & hardening:
  - **WebDAV LOCK / UNLOCK** (RFC 4918 §9.10–9.11): new `LockFile` / `LockFolder` / `UnlockFile` / `UnlockFolder` / `RefreshLock` methods returning a strongly-typed `LockInfo`.
  - **WebDAV PROPPATCH** (RFC 4918 §9.2): new `SetProperty` / `RemoveProperty` methods for managing custom (dead) properties.
  - **Bug fix — `Overwrite` header on COPY / MOVE** (RFC 4918 §9.8.3 / §9.9.3): always sent (default `T`); new optional `overwrite` parameter to opt out. `204 No Content` accepted as success.
  - **Bug fix — `Depth: infinity` on DELETE** (RFC 4918 §9.6.1): now sent as required by the spec for collection deletes.
  - **`If` lock-token submission on PUT / DELETE / MOVE / COPY** (RFC 4918 §10.4): new optional `lockToken` (and source / destination variants) parameters so locked-resource servers no longer reject modifications with `423 Locked`.
  - **Internal refactor**: extracted the static helpers in `Client.cs` into focused, unit-testable helper classes under `WebDAVClient.Helpers`. No public-API change.
  - **PROPFIND request-body variants** (RFC 4918 §9.1): new overloads to request a targeted `<prop>` set or discover property names via `<propname/>`, in addition to the existing `<allprop/>` behaviour.
  - **HTTP `OPTIONS` support** (RFC 4918 §9.1, RFC 9110 §9.3.7): new `GetServerOptions` method returning a strongly-typed `ServerOptions` (DAV compliance classes + allowed methods).
  - **Bearer token / OAuth 2.0 authentication**: new `Client` constructor overloads — static token or async refreshable provider — backed by a public `WebDAVClient.Authentication.BearerTokenAuthenticationHandler`.
  - **XXE hardening**: `ResponseParser` now sets `DtdProcessing = Prohibit` and `XmlResolver = null` explicitly.
  - **Certificate validation wired**: `ServerCertificateValidationCallback` is now actually plumbed into the underlying `HttpClientHandler`.
  - **SSRF protection**: `BuildServerUrl` validates that any absolute URI it accepts belongs to the configured `Server` host.
  - **Header injection protection**: `CustomHeaders` entries are validated for CR/LF in both name and value before being sent.
+ **2.5.x**  Performance & bug-fix rollup:
  - `List()` no longer issues an `await GetServerUrl` per returned item — the encoded base path is resolved once and reused, dramatically reducing async overhead on large listings.
  - `CustomHeaders` and the internal headers dictionary are no longer copied or double-looked-up per request; they are iterated in place.
  - Cached static byte arrays for the `MOVE`/`COPY` request bodies and `PROPFIND` body to eliminate per-call allocations.
  - `ResponseParser` avoids a per-node string allocation when reading element local names.
  - **Bug fix**: parent-folder URL comparison in `List()` now uses `OrdinalIgnoreCase` instead of `CurrentCultureIgnoreCase` — avoids the Turkish dotted/dotless `I` issue and the slower culture-aware path.
  - **Bug fix**: when `uploadTimeout` is set, the upload `HttpClient` no longer disposes the `HttpClientHandler` it shares with the main client.
  - Added a `WebDAVClient.UnitTests` project covering the public surface.
+ **2.4.0**   Framework support update:
  - Added support for .NET 10
  - Dropped support for .NET 9 (STS); supported targets are now .NET 8 (LTS) and .NET 10 (LTS)
+ **2.3.0**   Framework support update:
  - Updated to .NET Core 8.0 and 9.0; dropped older targets
  - Packaging cleanup
+ **2.2.1**   Minor packaging improvements
+ **2.2.0**   Improvement: 
  - Implement `IDisposable` to avoid `HttpClient` leak
  - Added support for `CancellationToken`
  - Various performance improvements to reduce memory allocations
  - Minor code cleanup
+ **2.1.0**   Bug fixes: Fixed handling of port
+ **2.0.0**   BREAKING CHANGES!!!
  - Added support for .Net Core 3.0 & .Net Standard 2.0
+ **1.1.3**   Improvement: 
  - Ignore WhiteSpaces while parsing response XML
  - Enable Windows Authentication
  - Support curstom certificate validation
  - Add download partial content
  - Improved testability by using a wrapper for HttpClient
+ **1.1.2**   Improvements: 
  - Make WebDAVClient work on Mono and make NuGet compatible with Xamarin projects
  - Provide a IWebProxy parameter for HttpClient
  - Change type of ContentLength from int to long
  - Improved compatibility to SVN
+ **1.1.1**   Improvements: 
  - Improved parsing of server responses
  - Improved the way we calculate URLs
+ **1.1.0**   Improvement: Improved parsing of server values
+ **1.0.23**   Improvement: Improve the way we handle path with "invalid" characters
+ **1.0.22**   Bug fixes and improvements:
  - Improved the way we identify the root folder 
  - Fixed calculation of URLs
+ **1.0.20**   Improvements:
  - Added support for default/custom UserAgent
  - Improved ToString method of WebDAVException
+ **1.0.19**   Improvement: Added support for uploads timeout
+ **1.0.18**   Improvement: Added MoveFolder and MoveFile methods
+ **1.0.17**   Improvement: Added DeleteFolder and DeleteFile methods
+ **1.0.16**   Improvements:
  - Improved filtering of listed folder
  - Disable ExpectContinue header from requests
+ **1.0.15**   Bug fixes: Trim trailing slashes from HRef of non-collection (files) items
+ **1.0.14**   BREAKING  CHANGES: Replaced Get() method with separated GetFolder() and GetFile() methods.
+ **1.0.13**   Improvement: Replaced deserialization mechanism with a more fault-tolerant one.
+ **1.0.12**   Bug fixes: Removed disposing of HttpMessageResponse when returning content as Stream
+ **1.0.11**   Improvements: 
  - Introduced new IClient interface
  - Added new WebDAVConflictException
  - Dispose HttpRequestMessage and HttpResponseMessage after use
+ **1.0.10**   Bug fixes: Correctly handle NoContent server responses as valid responses
+ **1.0.9**   Bug fixes: Improved handling of BasePath
+ **1.0.8**   Bug fixes: Handle cases when CreationDate is null
+ **1.0.7**   Async improvements: Added ConfigureAwait(false) to all async calls
+ **1.0.5**   Bug fixes and improvements: 
  - Decode Href property of files/folders
  - Complete Http send operation as soon as headers are read
+ **1.0.3**   Various bug fixes.
+ **1.0.1**   Improved error handling and authentication
+ **1.0.0**   Initial release.


# Usage

**Delete call compatibility:** `Client.DeleteFile(href: "...")` and
`Client.DeleteFolder(href: "...")` require `href`; through `IClient` the shipped
parameter is `path`, with default `"/"`. Prefer explicit positional resource paths
with either type. Omitting the interface argument targets the configured root;
never omit it unless root deletion is intended. This version preserves both
surfaces. The proposed next-major normalization is a required `path` on both
types, not a silent parameter rename or overload change in a patch release.

For new code, use `Client.CreateWithCredentials`,
`Client.CreateWithBearerToken`, or `Client.CreateWithBearerTokenProvider` to
select authentication without overloaded-null ambiguity. All shipped constructors
remain available. Legacy calls with a null credential should name that argument:
`new Client(credential: null, uploadTimeout: timeout, proxy: proxy)`.
Unqualified `new Client(null, uploadTimeout: timeout)` is ambiguous; null credentials
mean Windows-default credentials, **not anonymous** authentication.

Use HTTPS for bearer tokens and other credentials. Absolute resource URLs must
match `Server`'s scheme, hostname, and effective port (`Port` overrides the server
URL's port). Explicitly configured HTTP endpoints remain supported for backward
compatibility; this is not an HTTPS-only policy for the standalone bearer handler.

`Client` implements `IDisposable` — wrap it in a `using` to make sure the underlying `HttpClient`/`HttpClientHandler` are released. Most async methods accept an optional `CancellationToken` so requests can be cancelled cleanly.

Boolean mutations return `true` on confirmed success; they do **not** return
`false` for HTTP rejection. Handle `WebDAVException` and use `GetHttpCode()`;
HTTP 409 is the derived `WebDAVConflictException`. Caller cancellation throws
`OperationCanceledException`; transport/XML failures can retain their original
exception types. Existing return types are unchanged.

Client-generated `WebDAVException` failures expose read-only `Operation`, `Method`
and `ResourceUri` context. The URI omits user information, query and fragment;
headers and bodies are not added to this context. Resource paths can still contain
sensitive application names, so apply your logging policy. Manually constructed
exceptions can have null context. No shared "last request" state is used.

``` csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using WebDAVClient;

// Pick one of the constructors below.

// (1) Basic authentication
using IClient client = new Client(
    new NetworkCredential { UserName = "USERNAME", Password = "PASSWORD" });

// (2) ...or explicit anonymous transport (retain/dispose the HttpClient yourself)
// using IClient client = new Client(myAnonymousHttpClient);

// (3) ...or supply your own HttpClient (e.g. for IHttpClientFactory / DI scenarios)
// using IClient client = new Client(myHttpClient);

// (4) ...or Bearer / OAuth 2.0 — see "What's new in 2.7.0" below for refresh-aware variant
// using IClient client = new Client("eyJ0eXAiOiJKV1Qi...");

// Set basic information for the WebDAV provider
client.Server = "https://dav.example.com/";
client.BasePath = "/dav/";

// Optional configuration
client.Port = 8443;                                   // override the default port
client.UserAgent = "MyApp";                           // override the default User-Agent
client.CustomHeaders = new[]                          // sent with every request
{
    new KeyValuePair<string, string>("X-Tenant", "acme"),
};

// Most operations accept a CancellationToken
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var token = cts.Token;

var count = 0;
foreach (var item in await client.List(cancellationToken: token))
{
    Console.WriteLine($"{(item.IsCollection ? "Folder" : "File")}: {item.DisplayName}");
    count++;
}
if (count == 0)
    Console.WriteLine("This directory is empty.");
```

This example is read-only and requires no pre-existing files or folders. The
manual sample accepts configuration without source edits:

```pwsh
dotnet run --project TestWebDAVClient -f net10.0 -- --help
dotnet run --project TestWebDAVClient -f net10.0 -- --server https://dav.example.com --base-path /dav/ --validate
dotnet run --project TestWebDAVClient -f net10.0 -- --server https://dav.example.com --base-path /dav/
```

Use `WEBDAV_USERNAME`/`WEBDAV_PASSWORD` or `WEBDAV_BEARER_TOKEN` for authentication;
without either it is anonymous, not Windows-default authentication. `--target`
optionally reads a known file. Only `--mutate` enables writes, exclusively inside
a unique run-owned directory. The sample scopes streams, unlocks before deletion,
never deletes a directory whose creation was not confirmed, and reports cleanup
failures. Ctrl+C uses a separate five-second budget for each cleanup operation;
server/network failures can still require manual cleanup of the printed directory.

## Configuration recipes

Configure the concrete `Client` before exposing it as `IClient`; no new required
interface members or builder are needed. Replace example endpoints with your
server/proxy and keep credentials out of source. Reuse transports rather than
creating one per request.

### Owned transport: credentials, proxy and TLS

```csharp
using System;
using System.Net;
using System.Net.Security;
using System.Threading;
using WebDAVClient;

var user = Environment.GetEnvironmentVariable("WEBDAV_USERNAME")
    ?? throw new InvalidOperationException("Set WEBDAV_USERNAME.");
var password = Environment.GetEnvironmentVariable("WEBDAV_PASSWORD")
    ?? throw new InvalidOperationException("Set WEBDAV_PASSWORD.");
using var client = Client.CreateWithCredentials(
    new NetworkCredential(user, password),
    uploadTimeout: TimeSpan.FromMinutes(10),
    proxy: new WebProxy("http://proxy.example.com:8080"));
client.Server = "https://dav.example.com";
client.BasePath = "/dav/";
client.ResponseBodyTimeout = TimeSpan.FromSeconds(30);
client.ServerCertificateValidationCallback = (_, _, _, errors) => errors == SslPolicyErrors.None;
IClient api = client;
using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
Console.WriteLine((await api.GetServerOptions(cancellationToken: budget.Token)).IsWebDavServer);
```

The TLS callback above retains platform validation. Extend it with your approved
trust/pinning policy if necessary; never accept every certificate unconditionally.
Owned credential **and bearer** transports wire this concrete-client callback
lazily; without one, platform trust is used. Disposal releases the owned transport.
Use `CreateWithCredentials(null, ...)` only for Windows-default authentication.

### Owned transport: per-request bearer rotation

```csharp
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using WebDAVClient;

Task<string> CurrentToken(CancellationToken cancellationToken)
{
    cancellationToken.ThrowIfCancellationRequested();
    var value = Environment.GetEnvironmentVariable("WEBDAV_BEARER_TOKEN");
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException("Set WEBDAV_BEARER_TOKEN.");
    return Task.FromResult(value);
}

using var client = Client.CreateWithBearerTokenProvider(
    CurrentToken,
    uploadTimeout: TimeSpan.FromMinutes(10),
    proxy: new WebProxy("http://proxy.example.com:8080"));
client.Server = "https://dav.example.com";
client.BasePath = "/dav/";
client.ResponseBodyTimeout = TimeSpan.FromSeconds(30);
using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
Console.WriteLine((await client.GetServerOptions(cancellationToken: budget.Token)).IsWebDavServer);
```

This runnable provider reads the current process environment on every request;
your application can rotate that value. For actual OAuth refresh, replace its
body with your identity SDK's cancellation-aware token retrieval. The library
does not perform refresh or caching for you. Providers may run concurrently;
use the single-flight/caching recipe below if refresh must be coordinated.
For a fixed token, use `CreateWithBearerToken(token, uploadTimeout: ..., proxy: ...)`.

### Injected transport: distinct HTTP timeouts and handler configuration

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using WebDAVClient;
using WebDAVClient.Authentication;
using WebDAVClient.HttpClient;

var pipeline = new BearerTokenAuthenticationHandler(cancellationToken =>
{
    cancellationToken.ThrowIfCancellationRequested();
    var value = Environment.GetEnvironmentVariable("WEBDAV_BEARER_TOKEN");
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException("Set WEBDAV_BEARER_TOKEN.");
    return Task.FromResult(value);
})
{
    InnerHandler = new HttpClientHandler
    {
        UseDefaultCredentials = false,
        Proxy = new WebProxy("http://proxy.example.com:8080"),
        ServerCertificateCustomValidationCallback = (_, _, _, errors) => errors == SslPolicyErrors.None
    }
};
using var normal = new HttpClient(pipeline) { Timeout = TimeSpan.FromSeconds(20) };
using var uploads = new HttpClient(pipeline, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(10) };
using var transport = new HttpClientWrapper(normal, uploads);
using var client = new Client(transport)
{
    Server = "https://dav.example.com",
    BasePath = "/dav/",
    ResponseBodyTimeout = TimeSpan.FromSeconds(30)
};
using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
Console.WriteLine((await client.GetServerOptions(cancellationToken: budget.Token)).IsWebDavServer);
```

The concrete wrapper disposes both supplied HTTP clients; use dedicated instances
or a custom wrapper with an explicit shared-client lifetime policy. The ordinary
HTTP client owns the shared handler pipeline; the upload client does not dispose
that pipeline. Dispose only after in-flight calls and returned streams finish.
For injected credential authentication, omit the bearer handler and configure
`HttpClientHandler.Credentials`; for anonymous access omit both auth mechanisms
and keep `UseDefaultCredentials = false`.

**Unsupported combinations:** constructor authentication/proxy/upload-timeout
options configure owned transports only. With injection, configure these on your
own handlers/HTTP clients before use. A non-null
`Client.ServerCertificateValidationCallback` on an injected transport now causes
`InvalidOperationException` before any outbound request, instead of being silently
ignored. Set the callback on the injected handler, as above. Invalid owned upload
timeouts raise `ArgumentOutOfRangeException` for `uploadTimeout` before allocating
the owned transport. Do not combine competing bearer/credential handlers.

| Budget | Configuration and scope |
|---|---|
| Ordinary HTTP | Owned default is 100 seconds; inject `HttpClient.Timeout` to customize. Covers request send/headers, not subsequent body reads |
| Upload HTTP | Owned `uploadTimeout`, or a separate injected upload client's `Timeout`; covers upload send/headers |
| Metadata body | Concrete `Client.ResponseBodyTimeout`, default 100 seconds after headers, separately for each parsed response |
| Whole operation | Caller cancellation token spans discovery and the following operation; cancellation is not rollback |
| Download body | Caller passes its token to `CopyToAsync`/`ReadAsync` and disposes the returned stream; metadata/HTTP header deadlines do not limit these reads |

HTTP timeouts accept positive durations up to `Int32.MaxValue` milliseconds or
`Timeout.InfiniteTimeSpan`; metadata deadlines also support infinite timeout.
Different budgets race: the first expiration/cancellation wins. A returned
download stream does not automatically retain the method's cancellation token.

## Recovery and retry policy

The library intentionally does not automatically retry WebDAV operations.
A timeout or disconnect during a mutation can occur after the server committed
the change; cancellation is not a rollback. Reconcile remote state before
replaying PUT, DELETE, MOVE, COPY, LOCK, or PROPPATCH.

DELETE/MOVE/COPY `207 Multi-Status` remains a failure. The thrown
`WebDAVException` keeps HTTP 207 and exposes `ResourceStatuses` with each reported
member's `Href`, `StatusCode`, and optional `ResponseDescription`, including
successful members. These identifiers are untrusted diagnostics, not URLs to
automatically follow. Missing/malformed recovery XML keeps HTTP 207 and records
the parsing error in `InnerException`; do not infer complete failure or success
from an empty collection.

If adding an application-level retry policy, bound attempts and the total
operation deadline, respect `Retry-After`, and use jittered backoff for transient
failures. Recreate requests/content and reopen upload streams for every attempt.
Do not treat every exception/status as transient, replay partial failures as a
whole, or resend an ambiguously committed mutation without reconciliation and
appropriate conditional/lock protections. No automatic retry layer is added by
these reliability fixes.

## Transfer stream ownership

Always dispose streams returned by `Download` / `DownloadPartial`, using `using`
or `await using`; they keep the HTTP response alive and close it with its content.
Cancellation of the method covers request/stream acquisition; pass a token to
subsequent asynchronous stream reads to cancel the transfer itself.

`Upload` / `UploadPartial` retain their existing ownership behavior: once an
upload request is created, disposing that request closes the supplied content
stream on success, failure, or cancellation. There is no leave-open option.
Keep the input in a caller-side `using` scope too, because validation or discovery
can fail before a request takes ownership. Do not reuse that stream for another
attempt; reopen it, and for partial uploads set the correct source position and
range again.

## Concurrent root discovery

Concurrent first operations share a resolved root through an asynchronous gate
and a second cache check, rather than sending duplicate discovery requests.
Each caller can cancel its own wait. The caller performing discovery owns its
request token; if it fails, times out, or is cancelled, another waiter can retry
discovery with its own token. There are no automatic retries within the failed
call itself. Changing `Server`, `BasePath`, or `Port` starts a new configuration
generation: new callers can discover it without waiting for obsolete discovery,
and stale results cannot repopulate the cache. Normal resource requests are not
serialized by this gate.

## Metadata deadlines and cancellation

Configure `ResponseBodyTimeout` on the concrete `Client` before assigning it to
an `IClient` variable:

```csharp
using var client = new Client(credentials)
{
    Server = "https://dav.example.com",
    BasePath = "/webdav/",
    ResponseBodyTimeout = TimeSpan.FromSeconds(30)
};
var items = await client.List(cancellationToken: cancellationToken);
```

Each PROPFIND (including root discovery), LOCK/refresh, PROPPATCH, and mutation
multi-status recovery body has a
100-second total deadline by default, starting after response headers arrive.
This is independent of `HttpClient.Timeout`, which covers sending the request
and receiving headers when using `ResponseHeadersRead`. Expiration and caller
cancellation propagate as `OperationCanceledException` and release the response.
Set `ResponseBodyTimeout = Timeout.InfiniteTimeSpan` to disable that deadline;
caller cancellation remains effective. Configure an overall operation deadline
with the supplied cancellation token when discovery and the following request
must share one budget. Download streams are not subject to this metadata deadline;
pass cancellation to their asynchronous reads and dispose them when finished.

## What's new in 2.7.0

### Bearer token / OAuth 2.0 authentication

```csharp
// Static token (Nextcloud app-password, long-lived service token, …)
using IClient client = new Client("eyJ0eXAiOiJKV1Qi...");

// Async, refreshable token provider (Azure AD / MSAL / IdentityModel / custom)
using IClient client = new Client(async ct => await tokenSource.GetTokenAsync(ct));
```

Returning `null` / empty from the provider omits the `Authorization` header (the server then naturally returns `401`). The handler is also exposed publicly as `WebDAVClient.Authentication.BearerTokenAuthenticationHandler` if you need to compose it into your own `HttpClient` pipeline.

#### Concurrent token refresh

The provider runs once per request, including discovery, and can run concurrently.
It must be thread-safe and honor its cancellation token. Provider exceptions and
cancellation propagate without sending an unauthenticated fallback; return
`null`/empty only when anonymous access is intentional.

If your identity SDK does not already cache and coordinate refresh, this pattern
allows only one refresh at a time without serializing WebDAV HTTP operations.
`tokenSource.RefreshAsync(ct)` is application-supplied and returns `AccessToken`
and its UTC `ExpiresAt`:

```csharp
using var refreshGate = new SemaphoreSlim(1, 1);
string cachedToken = null;
DateTimeOffset expiresAt = DateTimeOffset.MinValue;

async Task<string> GetTokenAsync(CancellationToken ct)
{
    await refreshGate.WaitAsync(ct).ConfigureAwait(false);
    try
    {
        if (cachedToken != null && expiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
            return cachedToken;

        var fresh = await tokenSource.RefreshAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(fresh.AccessToken) || fresh.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Refresh returned an empty or expired token.");

        cachedToken = fresh.AccessToken;
        expiresAt = fresh.ExpiresAt;
        return cachedToken;
    }
    finally
    {
        refreshGate.Release();
    }
}

using IClient client = new Client(GetTokenAsync);
```

The 30-second margin should suit your token lifetime. Cancelling a waiting
request does not cancel another request's refresh. If the request performing
refresh fails or is cancelled, it releases the gate without publishing a new
cached token; another caller can retry. Dispose the client and finish pending
operations before disposing the gate. For cancellation-independent shared refresh,
use an identity SDK with its own refresh lifetime rather than sharing one
request's token with other callers.

### Discover server capabilities (OPTIONS)

```csharp
var options = await client.GetServerOptions("/dav/", cancellationToken: token);
if (!options.IsWebDavServer)
    throw new InvalidOperationException("Endpoint is not a WebDAV server");

bool supportsLock = options.IsClass2 && options.SupportsMethod("LOCK");
```

### Lock / unlock and refresh

```csharp
// Take an exclusive write lock on a file (default timeout: 600 seconds)
var info = await client.LockFile("/dav/report.docx", owner: "alice", cancellationToken: token);

try
{
    using (var fs = File.OpenRead(localPath))
        await client.Upload("/dav/", fs, "report.docx", lockToken: info.Token, cancellationToken: token);
    await client.RefreshLock("/dav/report.docx", info.Token, timeoutSeconds: 600, cancellationToken: token);
}
finally
{
    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await client.UnlockFile("/dav/report.docx", info.Token, cancellationToken: cleanup.Token);
}
```

`LockInfo.Token` is accepted in either bare (`opaquelocktoken:abc`) or `<opaquelocktoken:abc>` form everywhere it's used.

### Set / remove custom properties (PROPPATCH)

```csharp
// Set a custom dead property in your own namespace (the DAV: namespace is reserved for live properties and is rejected client-side)
await client.SetProperty("/dav/report.docx", "author", "https://example.com/ns", "Alice", token);

// Remove it later
await client.RemoveProperty("/dav/report.docx", "author", "https://example.com/ns", token);
```

### Targeted PROPFIND — `<prop>` and `<propname/>`

Use the configured `client` and cancellation `token` from the usage example:

```csharp
using System;
using WebDAVClient.Model;

// Ask only for the properties you need (saves bandwidth on large directories)
var props = new[]
{
    new PropertyName("DAV:", "getetag"),
    new PropertyName("DAV:", "getcontentlength"),
    new PropertyName("https://example.com/ns", "author"),
};
var items = await client.List("/dav/", depth: 1, properties: props, cancellationToken: token);

foreach (var item in items)
{
    // Standard DAV: live properties light up on Item directly (Etag, ContentLength, …)
    // Custom properties land in FoundProperties / NotFoundProperties.
    if (item.FoundProperties != null &&
        item.FoundProperties.TryGetValue(new PropertyName("https://example.com/ns", "author"), out var author))
        Console.WriteLine(author);
}

// Discover what properties a resource exposes
var names = await client.GetFilePropertyNames("/dav/report.docx", cancellationToken: token);
foreach (var n in names.AvailablePropertyNames)
    Console.WriteLine($"{n.Namespace}:{n.LocalName}");
```

### Lock-token-aware writes (If header)

```csharp
// PUT / DELETE / MOVE / COPY now accept the relevant lock tokens, so locked-resource servers stop rejecting the request with 423 Locked.
await client.DeleteFile("/dav/report.docx", lockToken: info.Token, cancellationToken: token);
await client.MoveFile("/dav/old.txt", "/dav/new.txt",
    sourceLockToken: srcToken, destinationLockToken: dstToken, cancellationToken: token);

// Opt out of clobbering an existing destination (sends Overwrite: F → server returns 412 Precondition Failed)
await client.CopyFile("/dav/a.txt", "/dav/b.txt", overwrite: false, cancellationToken: token);
```

## Contact

You can contact me on twitter [@saguiitay](https://twitter.com/saguiitay) or on my [website](https://www.saguiitay.com/)
