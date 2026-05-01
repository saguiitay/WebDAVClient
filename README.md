# WebDAVClient

## Overview

WebDAV Client for .Net Core, strongly typed, async and open-sourced, implemented in C#.

WebDAVClient is based originally on <https://github.com/kvdb/WebDAVClient>. I've added Async support (instead of Callback), as well strong-types responses.

## NuGet

WebDAVClient is available as a [NuGet package](https://www.nuget.org/packages/WebDAVClient/)

## Features

* Available as a NuGet package
* Fully support Async/Await
* Strong-typed
* Implemented using HttpClient, which means support for extendibility such as throttling and monitoring
* Authentication: unauthenticated, Windows / Basic / Digest (via `ICredentials`), and Bearer / OAuth 2.0 (via static token or async refreshable provider)
* Supports custom Certificate validation
* Supports the full WebDAV API:
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

## Release Notes

+ **2.7.0** _(upcoming)_  Security & hardening:
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

`Client` implements `IDisposable` — wrap it in a `using` to make sure the underlying `HttpClient`/`HttpClientHandler` are released. Most async methods accept an optional `CancellationToken` so requests can be cancelled cleanly.

``` csharp
// Pick one of the constructors below.

// (1) Basic authentication
using IClient client = new Client(
    new NetworkCredential { UserName = "USERNAME", Password = "PASSWORD" });

// (2) ...or no authentication
// using IClient client = new Client(new NetworkCredential());

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

// List items in the root folder
var files = await client.List(cancellationToken: token);

// Find folder named 'Test'
var folder = files.FirstOrDefault(f => f.Href.EndsWith("/Test/"));
// Reload folder 'Test'
var folderReloaded = await client.GetFolder(folder.Href, cancellationToken: token);

// Retrieve list of items in 'Test' folder
var folderFiles = await client.List(folderReloaded.Href, cancellationToken: token);
// Find first file in 'Test' folder
var folderFile = folderFiles.FirstOrDefault(f => f.IsCollection == false);

var tempFileName = Path.GetTempFileName();

// Download item into a temporary file
using (var tempFile = File.OpenWrite(tempFileName))
using (var stream = await client.Download(folderFile.Href, cancellationToken: token))
    await stream.CopyToAsync(tempFile, token);

// Upload file back to webdav
var tempName = Path.GetRandomFileName();
using (var fileStream = File.OpenRead(tempFileName))
{
    var fileUploaded = await client.Upload(folder.Href, fileStream, tempName, cancellationToken: token);
}

// Create a folder
var tempFolderName = Path.GetRandomFileName();
var isfolderCreated = await client.CreateDir("/", tempFolderName, cancellationToken: token);

// Copy a file
await client.CopyFile(folderFile.Href, "/" + tempFolderName + "/copy.bin", cancellationToken: token);

// Copy a folder
await client.CopyFolder(folder.Href, "/" + tempFolderName + "-copy/", cancellationToken: token);

// Delete created folder
var folderCreated = await client.GetFolder("/" + tempFolderName, cancellationToken: token);
await client.DeleteFolder(folderCreated.Href, cancellationToken: token);
```

## What's new in 2.7.0

### Bearer token / OAuth 2.0 authentication

```csharp
// Static token (Nextcloud app-password, long-lived service token, …)
using IClient client = new Client("eyJ0eXAiOiJKV1Qi...");

// Async, refreshable token provider (Azure AD / MSAL / IdentityModel / custom)
using IClient client = new Client(async ct => await tokenSource.GetTokenAsync(ct));
```

Returning `null` / empty from the provider omits the `Authorization` header (the server then naturally returns `401`). The handler is also exposed publicly as `WebDAVClient.Authentication.BearerTokenAuthenticationHandler` if you need to compose it into your own `HttpClient` pipeline.

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

// Use the lock token on subsequent writes via the If header
using (var fs = File.OpenRead(localPath))
    await client.Upload("/dav/", fs, "report.docx", lockToken: info.Token, cancellationToken: token);

// Extend / release the lock
await client.RefreshLock("/dav/report.docx", info.Token, timeoutSeconds: 600, cancellationToken: token);
await client.UnlockFile("/dav/report.docx", info.Token, cancellationToken: token);
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

```csharp
// Ask only for the properties you need (saves bandwidth on large directories)
var props = new[]
{
    new PropertyName("getetag",          "DAV:"),
    new PropertyName("getcontentlength", "DAV:"),
    new PropertyName("author",           "https://example.com/ns"),
};
var items = await client.List("/dav/", depth: 1, properties: props, cancellationToken: token);

foreach (var item in items)
{
    // Standard DAV: live properties light up on Item directly (Etag, ContentLength, …)
    // Custom properties land in FoundProperties / NotFoundProperties.
    var author = item.FoundProperties?.FirstOrDefault(p => p.Name.LocalName == "author");
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
