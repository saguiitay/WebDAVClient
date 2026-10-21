# WebDAVClient — Copilot Instructions

Strongly-typed, async WebDAV client library for .NET. Published as the [`WebDAVClient`](https://www.nuget.org/packages/WebDAVClient/) NuGet package.

## Build, test, run

Targets `net8.0` and `net10.0` (multi-targeted, both LTS). The CI workflow (`.github/workflows/publish.yml`) installs both SDKs and tests both targets before any publication. PR runs never publish; manual publication uses the same workflow and concurrency group. Duplicate package versions fail instead of being reported as a new release.

```pwsh
dotnet restore
dotnet build -c Release --no-restore          # builds both TFMs
dotnet test -c Release --no-build             # runs both test TFMs after building
dotnet test tests\WebDAVClient.UnitTests\WebDAVClient.UnitTests.csproj -c Release -p:CollectCoverage=true
dotnet format style WebDAVClient.sln --diagnostics IDE0011 --severity error --no-restore --verify-no-changes
dotnet build src\WebDAVClient\WebDAVClient.csproj -c Release-Unsigned # library-only configuration
dotnet pack  -c Release -o Release/nuget      # produces .nupkg + .snupkg
```

`WebDAVClient.UnitTests` is an MSTest project included in `WebDAVClient.sln`. Solution-level `dotnet test` runs its tests for both target frameworks. For focused regressions use `dotnet test tests\WebDAVClient.UnitTests\WebDAVClient.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~TestClassName"`.

Coverage uses private `coverlet.msbuild` tooling. `CollectCoverage=true` enforces at least 98% line and 95% branch coverage separately for each production module (library and sample), on each framework. Reports go to `tests\WebDAVClient.UnitTests\TestResults\coverage\<framework>\`; CI uploads them even after test/threshold failures. Do not add production-code exclusions to satisfy the gate. Release builds use portable PDBs for cross-platform instrumentation.

`examples/TestWebDAVClient/Program.cs` is a nonpackable smoke-test executable; its offline CLI paths and bounded loopback workflows also have automated coverage. Select a target explicitly: `dotnet run --project examples\TestWebDAVClient -f net10.0 -- --help`. Help and `--validate` are offline. Configure server/base path via arguments or environment; reads are the default and only `--mutate` enables run-owned changes with bounded cleanup. Authentication is anonymous unless explicitly configured.

Use `--modern` to exercise `DavClient`. Legacy mutation samples use full PUT by
default; nonstandard partial PUT requires the additional `--partial-put` opt-in.
The test suite compiles marked complete examples in the README and docs on both frameworks
using a private test-only Roslyn dependency; the library still has no NuGet dependencies.

`GeneratePackageOnBuild=true` is set on the library, so every Release build produces a `.nupkg` in `src/WebDAVClient/bin/Release`. Don't add a separate pack step in local workflows unless you need a custom output path.

`GenerateDocumentationFile=true` ships XML beside both framework assemblies.
Resolve XML warnings, inherit interface contracts where appropriate, and keep
parameter/stream-ownership/exception descriptions consistent across both surfaces.
Version 3.0.0 was published on October 20, 2026 and includes the v3 API.
Version 3.2.0 is published and includes the explicit lock lifecycle and core
response validation. Current source version 3.2.1 integrates interoperability
tooling and documentation; it is not published yet. The library is under src/WebDAVClient, tests
under tests/WebDAVClient.UnitTests and both runnable projects under examples.
The short README links to docs/README.md;
docs/reference.md retains detailed modern/legacy contracts and release notes.
Every example command has a matching guide. Keep server compatibility evidence
separate from protocol implementation; do not promote vendor documentation to
tested-compatibility claims.
`DavFileTransfers.DownloadFileAsync` adds explicit file checkpoints in 3.1;
ordinary stream downloads and the collection-sync REPORT do not persist them.
The resumable guide is docs/resumable-downloads.md with a matching CLI command.
`DavLockLease` adds caller-driven renewal, not timers, retries or reacquisition.
Its guide is docs/lock-lifecycle.md; docs/rfc4918-requirements.md tracks protocol
requirements and evidence. Successful refresh requires HTTP 200 and lockdiscovery XML.
`tests/WebDAVClient.InteropTests` is a separate nonpackable harness with offline
tests in ordinary CI. Independent-server execution requires explicit opt-in;
historical fixtures/results retain original revisions and are not current-package certification.
The README is embedded in the NuGet package; correcting its published copy
requires a new package version. Do not describe 3.0.0 as upcoming or require a
local build to use its published APIs.

## Architecture

- **`DavClient`** — v3 resource-oriented API with immutable endpoint configuration.
  No preliminary discovery or retries. Well-formed server rejections are results;
  callers inspect them or call operation-specific `EnsureSucceeded()`. Injected
  HttpClient remains caller-owned; configure auth/TLS and disable redirects before injection.
- **`Protocol`** — shared bounded asynchronous XML reader, qualified XML values,
  exact resource/property outcomes, typed property codecs and structured DAV If
  expressions. Legacy parsers project these results while preserving documented
  compatibility behavior. Protocol types stay in the existing assembly for now.
- **`DavListing` / `DavDownloadResult`** — response owners. A listing becomes complete
  only after full enumeration and document validation. Modern uploads retain input
  ownership unless `UploadOptions.DisposeContent` is selected; legacy uploads differ.
- **`Extensions.CollectionSync`** — one sync-level 1 REPORT batch, not durable sync.
  Apply changes durably before committing tokens; a 404 member is a deletion only
  in the report profile. Server/desktop/DI/managed-lease packages remain future work.
- **`IClient` / `Client`** (`src/WebDAVClient/Client.cs`) — the public surface. `Client` implements `IDisposable`; it owns an `IHttpClientWrapper` and disposes it only when it created it itself (`m_shouldDispose`). Callers passing in their own wrapper retain ownership.
- **`IHttpClientWrapper` / `HttpClientWrapper`** (`src/WebDAVClient/HttpClient/`) — thin abstraction over `System.Net.Http.HttpClient` so the client is testable and so a single `HttpClient` can be reused (avoids the well-known socket-exhaustion leak). Custom WebDAV verbs (`PROPFIND`, `MKCOL`, `MOVE`, `COPY`) are constructed as `static readonly HttpMethod` instances on `Client`.
- **`Model.Item`** — strongly-typed representation of a WebDAV resource returned by `PROPFIND`. Folders have a trailing `/` in `Href`; files do not (this distinction is load-bearing — see release notes 1.0.15 and the `IsCollection` checks in `README.md` examples).
- **`Helpers/ResponseParser`** — parses multistatus XML asynchronously with whitespace-ignoring settings; tolerates missing/null fields (e.g. `CreationDate`). `CancellationReadStream` binds network reads to caller cancellation and `Client.ResponseBodyTimeout` (100 seconds by default). Keep synchronous parsing limited to buffered property values and internal compatibility entry points.
- **`Helpers/WebDAVException` & `WebDAVConflictException`** — all server-side failures surface as `WebDAVException` (HTTP code accessible via `GetHttpCode()`); 409 responses surface as the more specific `WebDAVConflictException`. Preserve this mapping when adding new operations.
- **`Authentication/BearerTokenAuthenticationHandler`** — shipped static-token and async-provider authentication, exposed through `Client` constructor overloads. Providers run once per request and may run concurrently; caching/refresh coordination belongs to the provider. `AuthenticationOptions`, `BearerToken`, credential abstractions, and `ClientBuilder` remain roadmap proposals, not available APIs. Preserve the legacy `Client(ICredentials, TimeSpan?, IWebProxy)` constructor.
- **Consumer configuration** — explicit `Client.CreateWithCredentials` / `CreateWithBearerToken` / `CreateWithBearerTokenProvider` factories avoid new constructor ambiguity. Null credentials mean Windows-default authentication, not anonymous. Injected handlers own proxy/auth/TLS/HTTP timeout settings; a concrete-client TLS callback with injection is rejected before I/O. Metadata body deadlines remain on concrete `Client`.

## Conventions specific to this repo

- **Braces**: always use `{}` for C# control-flow bodies, including single statements in library, sample, and test code. `.editorconfig` sets IDE0011 to error, and CI verifies it with the targeted formatting command above.
- **Layout**: put braces on separate lines, including short blocks and accessors. Leave a blank line between members, including before their XML documentation or attributes. Use multiline XML documentation elements rather than inline `<summary>...</summary>` comments; self-closing `<inheritdoc/>` references remain appropriate.
- **Path handling is opinionated**: `Server` is stored without a trailing slash; `BasePath` is normalized to `"/" + value + "/"` (or `"/"` if empty). Don't bypass these setters when constructing URLs internally — use `m_server` / `m_basePath` / `m_encodedBasePath`.
- **Async**: public I/O methods return tasks and accept a trailing `CancellationToken cancellationToken = default`. Always add `.ConfigureAwait(false)` on awaits in library code (per release-notes 1.0.7 and existing usage). Thread tokens through discovery and body reads, not only HTTP sends.
- **Field naming**: instance fields use `m_camelCase`, statics use `s_camelCase`, constants use `c_camelCase`. Match this when adding new members in `Client.cs` / `HttpClientWrapper.cs`.
- **HTTP status interpretation**: `204 NoContent` is treated as success (release-notes 1.0.10); `207 Multi-Status` uses the `c_httpStatusCode_MultiStatus` constant. Don't reintroduce a hard `IsSuccessStatusCode` check that would regress these.
- **User-Agent**: defaults to `WebDAVClient/<assembly-version>` via `s_defaultUserAgent`; `UserAgent` / `UserAgentVersion` properties override it. Keep the assembly-version fallback intact.
- **Disposal**: dispose requests and responses after use. Successful `Download`/`DownloadPartial` transfers response ownership to `ResponseOwnedStream`; do not dispose that response before returning the stream, but dispose it on acquisition/validation failure. Upload requests own and dispose their input streams; callers still need a using scope for pre-request failures.
- **Recovery**: no automatic retries. DELETE/MOVE/COPY HTTP 207 remains a failure and retains member outcomes in `WebDAVException.ResourceStatuses`; malformed recovery XML is explicit through `InnerException`. Reconcile ambiguous/partial mutations before replaying and reopen upload content for every attempt.
- **Versioning**: `Version`, `AssemblyVersion`, `FileVersion`, and `PackageReleaseNotes` in `WebDAVClient.csproj` are bumped together; `docs/reference.md`'s "Release Notes" section is the human-readable changelog and should be updated for any user-visible change.
- **`plans/`** separates design proposals (direct identity credentials, builder, retry/logging and other future APIs) from user documentation under `docs/`. LOCK/UNLOCK and PROPPATCH are implemented, not planned. Use README/IClient/XML docs for current contracts; archival signatures and dates are not API or release commitments.
