# Documentation

Start with [getting started](getting-started.md): configure authentication, list a
collection, download a file and handle failures in a complete application.

## Guides and examples

Run commands from the repository root. Building the multi-targeted checkout
requires the .NET 10 SDK. Replace `net10.0` with `net8.0` to run on an installed
.NET 8 runtime; NuGet-only consumers can use the .NET 8 SDK.
All commands use the same [configuration and authentication](authentication.md);
`--help` needs no configuration and `--validate` performs no network I/O.

| Guide | Example command | Source | Side effects |
|---|---|---|---|
| [Getting started](getting-started.md) | `getting-started` | [GettingStarted.cs](../examples/WebDAVClient.Examples/GettingStarted.cs) | Writes a new local file |
| [Nextcloud integration](nextcloud.md) | `nextcloud` | [CloudStorage.cs](../examples/WebDAVClient.Examples/CloudStorage.cs) | PROPFIND/GET; writes a new local file |
| [ownCloud Server integration](owncloud.md) | `owncloud` | [CloudStorage.cs](../examples/WebDAVClient.Examples/CloudStorage.cs) | PROPFIND/GET; writes a new local file |
| [Authentication](authentication.md) | `authentication` | [Authentication.cs](../examples/WebDAVClient.Examples/Authentication.cs), [transport setup](../examples/WebDAVClient.Examples/Program.cs) | OPTIONS only |
| [Listing and properties](listing-and-properties.md) | `listing` | [ListingAndProperties.cs](../examples/WebDAVClient.Examples/ListingAndProperties.cs) | PROPFIND only |
| [Transfers](transfers.md) | `transfers --allow-writes` | [Transfers.cs](../examples/WebDAVClient.Examples/Transfers.cs) | Run-owned remote collection and new local file |
| [Resumable downloads (3.1+)](resumable-downloads.md) | `resumable-downloads` | [ResumableDownloads.cs](../examples/WebDAVClient.Examples/ResumableDownloads.cs) | GET only; local staging/checkpoint files and new destination |
| [File management](file-management.md) | `file-management --allow-writes` | [FileManagement.cs](../examples/WebDAVClient.Examples/FileManagement.cs) | Run-owned remote collection |
| [Locking](locking.md) | `locking --allow-writes` | [Locking.cs](../examples/WebDAVClient.Examples/Locking.cs) | Run-owned remote collection and lock |
| [Collection synchronization](collection-sync.md) | `collection-sync` | [CollectionSynchronization.cs](../examples/WebDAVClient.Examples/CollectionSynchronization.cs) | REPORT only; no checkpoint saved |

```pwsh
dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- --help
dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- authentication --validate
```

The examples project references the current library source. Consumers can instead
use `dotnet new console` and `dotnet add package WebDAVClient` to run the complete
[README program](../README.md#authenticate-list-files-and-download-one).
Shared executable configuration/error handling is in
[Program.cs](../examples/WebDAVClient.Examples/Program.cs); write orchestration and
bounded cleanup are in [MutationExamples.cs](../examples/WebDAVClient.Examples/MutationExamples.cs).
No examples silently retry or overwrite local downloads.

## Reference

| Topic | What it answers |
|---|---|
| [Server compatibility](compatibility.md) | Which server products are relevant, what is verified, and how to test your deployment |
| [Protocol support](protocol-support.md) | Implemented methods versus partial or absent extension profiles |
| [Explicit lock lifecycle (3.2+)](lock-lifecycle.md) | Observed lease state, caller-driven renewal, conditional writes and one-attempt release |
| [Errors and lifetimes](errors-and-lifetimes.md) | Status handling, stream ownership, cancellation, retries and troubleshooting |
| [Detailed API and legacy reference](reference.md) | Migration, legacy `Client`/`IClient`, paths, proxy/TLS, provider concurrency, limits and historical release notes |
| [Development](development.md) | Build, tests, coverage, documentation validation and release process |
| [Independent-server testing](../interoperability/README.md) | Opt-in mutating fixtures, historical observations and known failures |

Server advertisements, client implementation and real-server verification are
different kinds of evidence. Check both compatibility tables before depending
on an operation.
