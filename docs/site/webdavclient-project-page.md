---
title: "WebDAVClient: an async WebDAV client for .NET"
description: "Use WebDAV from C# with async operations, typed results, conditional writes, locking, and streamed listings. Available on NuGet for .NET 8 and .NET 10."
permalink: "/projects/webdavclient/"
draft: true
---

# WebDAVClient

WebDAVClient is an open-source C# library for working with files, folders, and properties on a WebDAV server. It provides asynchronous operations and typed results, so you can integrate WebDAV into a .NET application without building every HTTP request and XML parser yourself.

Version 3 targets **.NET 8 and .NET 10**. Its new `DavClient` API makes request conditions, server outcomes, and stream ownership explicit. The existing `Client` and `IClient` APIs remain available for applications that already use them.

**[Install WebDAVClient from NuGet](https://www.nuget.org/packages/WebDAVClient/)** or browse the [source and documentation on GitHub](https://github.com/saguiitay/WebDAVClient).

## What can you do with it?

- **Browse resources and their properties.** List a collection, request specific properties, or stream a large listing. Keep resource-level and property-level outcomes separate.
- **Transfer files.** Upload full representations and download content through streams, with support for validated byte-range downloads.
- **Manage files and collections.** Create, delete, copy, and move resources. The modern API does not overwrite a copy or move destination unless you explicitly allow it.
- **Coordinate writes.** Use HTTP validators, WebDAV conditions, and shared or exclusive locks where the server supports them.
- **Work with WebDAV XML.** Read typed properties without discarding their underlying XML, and submit ordered property changes with PROPPATCH.

The library also exposes OPTIONS, HEAD, a generic REPORT method, and a single-batch collection synchronization helper. It does not provide a complete synchronization engine or claim full support for every WebDAV extension.

## Install the package

Using the .NET CLI:

```shell
dotnet add package WebDAVClient --version 3.0.0
```

Or in Visual Studio's Package Manager Console:

```powershell
Install-Package WebDAVClient -Version 3.0.0
```

## List a collection in C#

This example uses an anonymous connection. Replace the endpoint with your WebDAV collection URL, including its trailing slash.

```csharp
using System;
using System.Threading;
using WebDAVClient;
using WebDAVClient.Protocol;

using var client = new DavClient(new Uri("https://dav.example.com/files/"));
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

`PropFindAsync` returns the addressed collection as well as its reported children. Unlike legacy `List`, it does not filter out the collection itself.

A successful HTTP status does not necessarily mean every WebDAV operation succeeded. You can inspect the result's resource and property statuses, or use `EnsureSucceeded()` to require success for this core operation.

For an authenticated server, configure credentials or an authentication handler on a caller-owned `HttpClient`, disable automatic redirects, and pass that transport to `DavClient`. Reuse the client across operations and use HTTPS for credentials. The [README covers authentication, timeouts, conditions, and ownership](https://github.com/saguiitay/WebDAVClient#v3-migration-and-contracts).

## Upgrading an existing application

You do not need to rename every `Client` call to adopt version 3. The legacy surface is retained; moving to `DavClient` is an explicit migration.

Important differences include immutable endpoint configuration, request-local options, inspectable server rejections, and caller-owned upload streams by default. Modern COPY and MOVE also default to no overwrite.

The assembly version changes to `3.0.0.0`, so rebuild consumers when upgrading. See the [migration table](https://github.com/saguiitay/WebDAVClient#v3-migration-and-contracts) before changing APIs.

## Testing and scope

The v3 release candidate passed **831 tests on each target framework** on Windows and Ubuntu; the [Linux validation run is available on GitHub](https://github.com/saguiitay/WebDAVClient/actions/runs/39488709178). Tests cover protocol responses, cancellation, stream ownership, compatibility, and runnable examples. Independent WsgiDAV smoke runs also exercised basic transfers and cleanup through both APIs.

That is evidence for the tested behavior, not a guarantee that every server or extension behaves identically. There are no automatic retries, managed lock renewals, or durable synchronization built in. The library has no production NuGet dependencies.

## Source, documentation, and background

- [Read the quickstart and API guidance](https://github.com/saguiitay/WebDAVClient#readme).
- [Browse the source or report an issue](https://github.com/saguiitay/WebDAVClient).
- [Install the NuGet package](https://www.nuget.org/packages/WebDAVClient/).
- [Read the original WebDAV client in C# article](https://saguiitay.com/webdav-client-in-c/) for the project's background. Its older examples describe the legacy API.

The project grew from [kvdb/WebDAVClient](https://github.com/kvdb/WebDAVClient), with asynchronous operations and strongly typed responses added along the way.

<!--
Editorial notes, not page copy:
- Replace the existing project page at the same URL. The current page's GitHub
  link points to CardinalityEstimation despite displaying WebDAVClient; the links
  above use the correct repository.
- Lead with the integration task and installation, then explain the v3/legacy
  distinction. Avoid carrying forward the unsupported blanket "battle-tested at
  high scale" claim for the redesigned API.
- Publication verified on 2026-10-20: the package and symbol upload succeeded in
  workflow 39490906354, and https://www.nuget.org/packages/WebDAVClient/3.0.0
  is live. The draft flag is editorial metadata, not a CMS import requirement.
- Alternative headline: "Work with WebDAV from C#" (task-focused).
- Alternative headline: "WebDAVClient for .NET 8 and .NET 10" (platform-focused).
- Alternative CTA: "Install the .NET package" (direct action).
- Alternative CTA: "Read the C# quickstart" (lower commitment).
-->
