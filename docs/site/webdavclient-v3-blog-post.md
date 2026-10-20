---
title: "WebDAVClient 3: making WebDAV outcomes explicit in C#"
description: "A look at WebDAVClient 3: its new DavClient API, conditional writes, stream ownership, compatibility, and the tests behind the .NET 8 and .NET 10 release."
permalink: "/webdavclient-3-dotnet/"
draft: true
---

# WebDAVClient 3: making WebDAV outcomes explicit in C#

When I first wrote about my [WebDAV client in C#](https://saguiitay.com/webdav-client-in-c/), I wanted something practical: asynchronous operations, strongly typed responses, and an API built on `HttpClient`.

Those requirements have not changed. What has changed is how much of the protocol I want the API to expose.

WebDAV can report success for one resource and failure for another in the same response. A property can be missing, denied, or present as structured XML. A connection can fail after the server has already committed a write.

Version 3 adds a new `DavClient` API around those distinctions. It targets .NET 8 and .NET 10, while keeping the existing `Client` and `IClient` APIs available.

## Why HTTP success is not enough

Consider a collection operation that returns `207 Multi-Status`.

The outer HTTP response is successful, but the body might report `204` for one resource and `403` for another. Treating the outer status as the whole result would lose the information an application needs to recover.

The new API separates:

- The outer HTTP status.
- Resource-level outcomes.
- Property-level outcomes.
- Structured DAV error conditions.

Ordinary server rejections are returned as inspectable results. Cancellation, transport failures, and malformed protocol responses still throw.

For core operations, `EnsureSucceeded()` checks the operation's accepted status and its reported resource or property outcomes. `EnsureHttpSuccess()` checks only the outer status. That distinction matters when deciding whether a write actually completed.

REPORT responses need their own interpretation. For example, a resource-level `404` in a collection synchronization report can describe a deletion, rather than an operation to retry.

## An endpoint and a configured transport

Install version 3 with:

```shell
dotnet add package WebDAVClient --version 3.0.0
```

`DavClient` has an immutable collection endpoint. Individual requests carry their own options, and the client does not perform implicit root discovery before an operation.

For an authenticated server, I can configure the HTTP transport separately:

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using WebDAVClient;
using WebDAVClient.Protocol;

var username = Environment.GetEnvironmentVariable("WEBDAV_USERNAME")
    ?? throw new InvalidOperationException("Set WEBDAV_USERNAME.");
var password = Environment.GetEnvironmentVariable("WEBDAV_PASSWORD")
    ?? throw new InvalidOperationException("Set WEBDAV_PASSWORD.");

using var http = new HttpClient(new HttpClientHandler
{
    Credentials = new NetworkCredential(username, password),
    UseDefaultCredentials = false,
    AllowAutoRedirect = false,
    AutomaticDecompression = DecompressionMethods.None,
    UseCookies = false
});

using var client = new DavClient(
    new Uri("https://dav.example.com/files/"),
    http);
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

var result = await client.PropFindAsync(
    DavPath.Root,
    new PropFindOptions
    {
        Depth = DavDepth.One
    },
    cancellation.Token);

result.EnsureSucceeded();

foreach (var resource in result.Resources)
{
    Console.WriteLine(resource.Hrefs[0]);
}
```

Replace the example endpoint with the collection URL for your server. The injected `HttpClient` remains caller-owned, and automatic redirects must be disabled before injection. In an application, reuse the client and transport rather than constructing them for every request.

For Nextcloud, an app password belongs in the username/password credential flow over HTTPS. It is not an OAuth bearer token. Bearer authentication is available through an authentication handler when the server supports it.

Paths are explicit too. `DavPath.FromSegments(...)` escapes raw names once; `DavPath.Parse(...)` accepts an already-escaped reference. A leading `/` is origin-relative, not relative to the configured collection.

## Make write conditions part of the request

A filename existing on the server should be a decision, not an accidental overwrite.

This separate example creates a file only if it does not already exist, then reads its content. Use a writable test collection. It uses an anonymous transport; inject the authenticated transport shown above if your server requires one.

```csharp
using System;
using System.IO;
using System.Text;
using System.Threading;
using WebDAVClient;
using WebDAVClient.Protocol;

using var client = new DavClient(new Uri("https://dav.example.com/files/"));
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

var path = DavPath.FromSegments("draft #1.txt");
using var input = new MemoryStream(Encoding.UTF8.GetBytes("Hello from WebDAVClient 3."));

var upload = await client.UploadAsync(
    path,
    input,
    new UploadOptions
    {
        IfNoneMatch = "*",
        ContentType = "text/plain; charset=utf-8"
    },
    cancellation.Token);

upload.EnsureSucceeded();

await using var download = await client.DownloadAsync(
    path,
    cancellationToken: cancellation.Token);

download.EnsureHttpSuccess();

var content = download.Content
    ?? throw new InvalidDataException("The response did not contain downloadable content.");

await content.CopyToAsync(Stream.Null, cancellation.Token);
Console.WriteLine("Created the file and read its content.");
```

`IfNoneMatch = "*"` asks the server to reject the PUT if a representation already exists. For an update, `IfMatch` can carry an ETag obtained from the server. These conditions are not substitutes for reconciliation after a timeout: losing a response does not prove that a write failed.

Modern COPY and MOVE also default to no overwrite. Replacing an existing destination requires `Overwrite = true`.

There are no automatic retries. If an application decides to retry a mutation, it needs to account for partial or ambiguous completion and reopen its upload content.

## Be clear about who owns a stream

The upload above leaves `input` open after the request. That is the modern API's default; `DisposeContent = true` explicitly transfers ownership after argument validation. A caller-side `using` remains useful for failures before that transfer.

Downloads work differently. `DavDownloadResult` owns the HTTP response and its content, so dispose the result even when the request returns an error or `304 Not Modified`. Pass a cancellation token to subsequent reads as well as to `DownloadAsync`.

The legacy API keeps its existing upload ownership behavior. This is one reason `DavClient` is a separate API rather than a rename of `Client`.

## A streamed listing needs a completion signal

Large collections do not always need to be buffered before the application can process them. `OpenListingAsync` provides a single-use streamed listing.

Its `IsComplete` property becomes true only after the resources and the end of the XML document have been validated. Breaking out of enumeration early, cancelling, or encountering a malformed tail leaves it false.

That distinction is important for synchronization code. An incomplete listing is not evidence that unobserved files were deleted. Even a complete listing does not guarantee that every member succeeded or that the server provided an atomic snapshot.

For a rejected listing request, structured DAV error details are available before enumeration. Error-body reads still have metadata deadlines and XML limits.

## What the release checks caught

The release candidate passed 831 tests on each of .NET 8 and .NET 10, on both Windows and Ubuntu. Library coverage was 99.25% of lines and 96.35% of branches; the [Linux run records those results](https://github.com/saguiitay/WebDAVClient/actions/runs/39488709178).

Coverage alone was not enough. The review caught cases where the API could accept property-only mutation outcomes, flatten nested XML into a LOCK scalar value, or lose error details from a rejected streamed listing. Those cases now have fixes and regression tests.

The checks also compile the seven complete README examples. Separate smoke runs against WsgiDAV exercised basic transfer workflows and cleanup through both the modern and legacy APIs.

That testing has limits. It is not a conformance certification, and it does not establish that every server's authentication, locking, or extension behavior is identical.

I also kept performance work focused on measured allocations. In the six-property projection fixture, allocations dropped from roughly 10 KB to 3,328 bytes per operation. The [README documents the measurement and its limits](https://github.com/saguiitay/WebDAVClient#tests-and-coverage). That is a useful reduction, but it is not a claim that network transfers became three times faster.

## What is not included

Version 3 exposes a generic REPORT method and a helper for one sync-level 1 collection synchronization batch. It does not persist checkpoints or implement a durable synchronization engine.

Managed lock renewal, automatic retries, and complete CalDAV, CardDAV, or other extension support are not part of this release. The [protocol completeness plan](https://github.com/saguiitay/WebDAVClient/blob/master/docs/PROTOCOL-COMPLETENESS-PLAN.md) separates the current API from that future work.

## Moving from the older API

Existing applications can continue using `Client` and `IClient`. Their public signatures remain available; adopting `DavClient` is an explicit migration, not a search-and-replace operation.

Read the migration table before switching, especially the sections on paths, stream ownership, overwrite defaults, and server rejections. Rebuild consumers when upgrading: the assembly version is now `3.0.0.0`.

For new integrations, start with [the v3 quickstart and migration guide](https://github.com/saguiitay/WebDAVClient#v3-migration-and-contracts). The [project page](https://saguiitay.com/projects/webdavclient/) provides an overview, and the [NuGet package](https://www.nuget.org/packages/WebDAVClient/) contains the library for both target frameworks.

<!--
Editorial notes, not article copy:
- Suggested new post, not a replacement for /webdav-client-in-c/. Keep the older
  article as historical context and add a short update/link there once this post
  is published.
- The first-person voice follows the original article, while the examples use
  current contracts rather than its old hosted-service and credential assumptions.
- Publication verified on 2026-10-20: NuGet accepted the package and symbols in
  https://github.com/saguiitay/WebDAVClient/actions/runs/39490906354 .
  https://www.nuget.org/packages/WebDAVClient/3.0.0 is live. Test counts and
  coverage describe this release candidate, not every future build. The draft
  flag is editorial metadata, not a CMS import requirement.
- Alternative title: "WebDAVClient 3 for .NET 8 and .NET 10" (release-focused).
- Alternative title: "Beyond HTTP success: redesigning a C# WebDAV client"
  (design-focused).
- Alternative CTA: "Try the v3 quickstart" (hands-on entry point).
- Alternative CTA: "Review the migration guide" (existing-user entry point).
-->
