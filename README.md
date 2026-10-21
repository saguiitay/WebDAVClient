# WebDAVClient

Browse WebDAV storage and transfer files from C#/.NET applications. Upload,
download, copy, move and delete files without hand-writing DAV requests or
parsing XML. Supports **.NET 8 and .NET 10**.

```pwsh
dotnet add package WebDAVClient
```

The v3 API, `DavClient`, is available from NuGet starting with **3.0.0**, released
October 20, 2026. No local library build is required.

## List a folder

Start with an anonymous endpoint:

<!-- compile-example: anonymous-listing -->
```csharp
using System;
using WebDAVClient;
using WebDAVClient.Protocol;
using var client = new DavClient(new Uri("https://dav.example.com/dav/"));
var result = await client.PropFindAsync(DavPath.Root);
result.EnsureSucceeded();
foreach (var resource in result.Resources)
{
    Console.WriteLine(resource.Hrefs[0]);
}
```

The listing includes the collection itself and its immediate children.
See [listing and properties](https://github.com/saguiitay/WebDAVClient/blob/master/docs/listing-and-properties.md)
for streaming, typed properties and result handling.

## Authenticate, list files and download one

Set `WEBDAV_ENDPOINT` to your HTTPS collection URL, ending in `/`, and configure
`WEBDAV_USERNAME` and `WEBDAV_PASSWORD` outside source control. Set
`WEBDAV_REMOTE_FILE` to the name of an existing file in that collection.
`WEBDAV_OUTPUT` optionally selects a new local output path (default `download.bin`).

Paste this complete program into a console application after installing the package:

<!-- compile-example: authenticated-download -->
```csharp
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using WebDAVClient;
using WebDAVClient.Helpers;

try
{
    string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new ArgumentException("Set " + name + ".");

    var endpoint = new Uri(Required("WEBDAV_ENDPOINT"));
    if (endpoint.Scheme != Uri.UriSchemeHttps)
    {
        throw new ArgumentException("Use HTTPS for credentials.");
    }

    using var http = new HttpClient(new HttpClientHandler
    {
        Credentials = new NetworkCredential(
            Required("WEBDAV_USERNAME"), Required("WEBDAV_PASSWORD")),
        UseDefaultCredentials = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false
    });
    using var client = new DavClient(endpoint, http);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));

    var listing = await client.PropFindAsync(
        DavPath.Root, cancellationToken: deadline.Token);
    listing.EnsureSucceeded();
    foreach (var resource in listing.Resources)
    {
        Console.WriteLine(resource.Hrefs[0]);
    }

    using var download = await client.DownloadAsync(
        DavPath.FromSegments(Required("WEBDAV_REMOTE_FILE")),
        cancellationToken: deadline.Token);
    download.EnsureHttpSuccess();
    var content = download.Content
        ?? throw new IOException("The server returned no downloadable content.");
    var output = Environment.GetEnvironmentVariable("WEBDAV_OUTPUT") ?? "download.bin";
    await using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
    await content.CopyToAsync(file, deadline.Token);
    Console.WriteLine("Download complete.");
    return 0;
}
catch (WebDAVException ex)
{
    Console.Error.WriteLine($"WebDAV failed: HTTP {ex.ResponseStatusCode ?? ex.GetHttpCode()}.");
    Console.Error.WriteLine("Check the endpoint, credentials, permissions and remote filename.");
    return 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("The operation timed out or was cancelled.");
    return 130;
}
catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is UnauthorizedAccessException
    || ex is ArgumentException || ex is UriFormatException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
```

The listing includes the collection itself and its immediate children.
`FileMode.CreateNew` prevents overwriting an existing local file; a failed copy
can leave a partial file. The [getting-started guide](https://github.com/saguiitay/WebDAVClient/blob/master/docs/getting-started.md)
explains configuration, errors, partial-file handling and the runnable equivalent.
Use an app password, not a bearer token, when your server requires one.

## Find the right guide

| Task | Guide and matching runnable example |
|---|---|
| Connect to Nextcloud with an app password and read file metadata | [Nextcloud integration](https://github.com/saguiitay/WebDAVClient/blob/master/docs/nextcloud.md) |
| Connect to ownCloud Server and read file metadata | [ownCloud integration](https://github.com/saguiitay/WebDAVClient/blob/master/docs/owncloud.md) |
| Choose anonymous, credentials, Windows or bearer authentication | [Authentication](https://github.com/saguiitay/WebDAVClient/blob/master/docs/authentication.md) |
| List folders, stream large listings and read typed/custom properties | [Listing and properties](https://github.com/saguiitay/WebDAVClient/blob/master/docs/listing-and-properties.md) |
| Upload, download and request byte ranges | [File transfers](https://github.com/saguiitay/WebDAVClient/blob/master/docs/transfers.md) |
| Resume interrupted file downloads with validated checkpoints (3.1+) | [Resumable downloads](https://github.com/saguiitay/WebDAVClient/blob/master/docs/resumable-downloads.md) |
| Create folders, copy, move and delete safely | [File management](https://github.com/saguiitay/WebDAVClient/blob/master/docs/file-management.md) |
| Acquire, refresh and release a lock; make conditional writes | [Locking](https://github.com/saguiitay/WebDAVClient/blob/master/docs/locking.md) |
| Request an RFC 6578 change batch | [Collection synchronization](https://github.com/saguiitay/WebDAVClient/blob/master/docs/collection-sync.md) |
| Diagnose failures, cancellation and resource ownership | [Errors and lifetimes](https://github.com/saguiitay/WebDAVClient/blob/master/docs/errors-and-lifetimes.md) |
| Check Nextcloud, ownCloud, IIS, Apache and other servers | [Server compatibility](https://github.com/saguiitay/WebDAVClient/blob/master/docs/compatibility.md) |
| Inspect version-specific server evidence or run isolated compatibility tests | [Independent-server testing](https://github.com/saguiitay/WebDAVClient/blob/master/interoperability/README.md) |
| Check supported methods, RFCs and extensions | [Protocol support](https://github.com/saguiitay/WebDAVClient/blob/master/docs/protocol-support.md) |
| Migrate from `Client` / `IClient` or find advanced configuration | [Detailed API and legacy reference](https://github.com/saguiitay/WebDAVClient/blob/master/docs/reference.md) |

Nextcloud and ownCloud compatibility is **not verified** by this project.
Implemented protocol features and unit tests are not server compatibility
certification. Neither API automatically retries mutations.

## Run the examples

Library source is under `src`, automated tests under `tests`, and both runnable
sample applications under `examples`.

```pwsh
git clone https://github.com/saguiitay/WebDAVClient.git
Set-Location WebDAVClient
dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- --help
```

Examples are read-only unless you explicitly enable writes. Each has a guide
covering prerequisites, expected results and failure cases.
See the [documentation index](https://github.com/saguiitay/WebDAVClient/blob/master/docs/README.md).

## Contribute and license

See [development and testing](https://github.com/saguiitay/WebDAVClient/blob/master/docs/development.md)
and [GitHub releases](https://github.com/saguiitay/WebDAVClient/releases)
for short changelogs and versioned NuGet links. Detailed historical notes remain
in the [API reference](https://github.com/saguiitay/WebDAVClient/blob/master/docs/reference.md#release-notes).
WebDAVClient is distributed under [MIT](https://github.com/saguiitay/WebDAVClient/blob/master/LICENSE.md).
The [provenance notice](https://github.com/saguiitay/WebDAVClient/blob/master/NOTICE.md)
retains attribution for the original [kvdb/WebDAVClient](https://github.com/kvdb/WebDAVClient).
