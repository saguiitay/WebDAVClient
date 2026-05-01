# WebDAVClient — Copilot Instructions

Strongly-typed, async WebDAV client library for .NET. Published as the [`WebDAVClient`](https://www.nuget.org/packages/WebDAVClient/) NuGet package.

## Build, test, run

Targets `net8.0` and `net10.0` (multi-targeted, both LTS). The CI workflow (`.github/workflows/manual.yml`) installs both SDKs.

```pwsh
dotnet restore
dotnet build -c Release --no-restore          # builds both TFMs
dotnet build -c Release-Unsigned              # third configuration defined in csproj
dotnet pack  -c Release -o Release/nuget      # produces .nupkg + .snupkg
```

Solution-level `dotnet test` is currently a no-op: only `WebDAVClient` and `TestWebDAVClient` are listed in `WebDAVClient.sln`. The `WebDAVClient.UnitTests` folder exists but contains no test sources and is **not** part of the solution — if you add tests, also add the project to the .sln (and a test SDK + framework reference) before relying on `dotnet test`.

`TestWebDAVClient/Program.cs` is a manual smoke-test console app (hard-coded credentials placeholders) — not an automated test. Run it with `dotnet run --project TestWebDAVClient` only after editing the credentials/server.

`GeneratePackageOnBuild=true` is set on the library, so every Release build produces a `.nupkg` in `WebDAVClient/bin/Release`. Don't add a separate pack step in local workflows unless you need a custom output path.

## Architecture

- **`IClient` / `Client`** (`WebDAVClient/Client.cs`) — the public surface. `Client` implements `IDisposable`; it owns an `IHttpClientWrapper` and disposes it only when it created it itself (`m_shouldDispose`). Callers passing in their own wrapper retain ownership.
- **`IHttpClientWrapper` / `HttpClientWrapper`** (`WebDAVClient/HttpClient/`) — thin abstraction over `System.Net.Http.HttpClient` so the client is testable and so a single `HttpClient` can be reused (avoids the well-known socket-exhaustion leak). Custom WebDAV verbs (`PROPFIND`, `MKCOL`, `MOVE`, `COPY`) are constructed as `static readonly HttpMethod` instances on `Client`.
- **`Model.Item`** — strongly-typed representation of a WebDAV resource returned by `PROPFIND`. Folders have a trailing `/` in `Href`; files do not (this distinction is load-bearing — see release notes 1.0.15 and the `IsCollection` checks in `README.md` examples).
- **`Helpers/ResponseParser`** — parses multistatus XML responses with whitespace-ignoring settings; tolerates missing/null fields (e.g. `CreationDate`).
- **`Helpers/WebDAVException` & `WebDAVConflictException`** — all server-side failures surface as `WebDAVException` (HTTP code accessible via `GetHttpCode()`); 409 responses surface as the more specific `WebDAVConflictException`. Preserve this mapping when adding new operations.
- **`Authentication/`** (`AuthenticationOptions`, `BearerToken`) and **`Configuration/ClientBuilder`** — newer fluent/modern-auth surface added per `ROADMAP.md`. The legacy `Client(ICredentials, TimeSpan?, IWebProxy)` constructor must remain backward compatible — see the explicit "maintain backward compatibility" note at the top of `Client.cs`.

## Conventions specific to this repo

- **Path handling is opinionated**: `Server` is stored without a trailing slash; `BasePath` is normalized to `"/" + value + "/"` (or `"/"` if empty). Don't bypass these setters when constructing URLs internally — use `m_server` / `m_basePath` / `m_encodedBasePath`.
- **Async**: every public I/O method is `async` and accepts a trailing `CancellationToken cancellationToken = default`. Always add `.ConfigureAwait(false)` on awaits in library code (per release-notes 1.0.7 and existing usage).
- **Field naming**: instance fields use `m_camelCase`, statics use `s_camelCase`, constants use `c_camelCase`. Match this when adding new members in `Client.cs` / `HttpClientWrapper.cs`.
- **HTTP status interpretation**: `204 NoContent` is treated as success (release-notes 1.0.10); `207 Multi-Status` uses the `c_httpStatusCode_MultiStatus` constant. Don't reintroduce a hard `IsSuccessStatusCode` check that would regress these.
- **User-Agent**: defaults to `WebDAVClient/<assembly-version>` via `s_defaultUserAgent`; `UserAgent` / `UserAgentVersion` properties override it. Keep the assembly-version fallback intact.
- **Disposal**: `HttpRequestMessage` and `HttpResponseMessage` are disposed after use **except** when returning a `Stream` from `Download`/`DownloadPartial` — those streams own the response lifetime (release-notes 1.0.12). Don't wrap those in `using`.
- **Versioning**: `Version`, `AssemblyVersion`, `FileVersion`, and `PackageReleaseNotes` in `WebDAVClient.csproj` are bumped together; `README.md`'s "Release Notes" section is the human-readable changelog and should be updated for any user-visible change.
- **`ROADMAP.md`** is authoritative for planned API direction (modern auth via `TokenCredential`/`ApiKeyCredential`, LOCK/UNLOCK, PROPPATCH, `ClientBuilder` fluent API, retry/logging). Align new work with the patterns described there rather than inventing parallel APIs.
