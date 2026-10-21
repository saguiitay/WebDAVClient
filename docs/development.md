# Development and documentation validation

Install the .NET 10 SDK to build the solution, and both .NET 8 and .NET 10
runtimes to run both test targets (CI installs both SDKs).
The library has no NuGet dependencies;
the test project has private test/coverage/compiler dependencies.

## Repository layout

| Folder/file | Purpose |
|---|---|
| `src\WebDAVClient` | Packaged library and shared protocol/transfer code |
| `tests\WebDAVClient.UnitTests` | Automated contract, loopback, example and documentation tests |
| `examples\WebDAVClient.Examples` | Task-focused and vendor integration commands |
| `examples\TestWebDAVClient` | Legacy/modern smoke-test executable |
| `docs` | User guides, API contracts and development instructions |
| `plans` | Design proposals and future work, not shipped API contracts |
| `WebDAVClient.sln` | Root solution for all projects |
| `LICENSE.md`, `NOTICE.md` | Current MIT terms and retained upstream provenance |

Run the following commands from the repository root. SDK-style projects use
`dotnet restore`; no checked-in NuGet executable or legacy restore targets are
required.

```pwsh
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
dotnet format style WebDAVClient.sln --diagnostics IDE0011 --severity error --no-restore --verify-no-changes
```

The solution includes the legacy smoke executable and the
[feature examples](../examples/WebDAVClient.Examples). Examples build on both
frameworks. Choose a framework explicitly when running them:

```pwsh
dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- --help
dotnet run --project examples\TestWebDAVClient -f net10.0 -- --help
```

## Validate documentation

Marked complete programs in the root README and `docs` are compiled by
`ReadmeCompilationTests` against each framework and the current library.
The inventory is explicit so accidentally dropping/moving a complete example
does not silently remove validation. The runnable examples project is built
and its onboarding/failure workflows are covered separately.

```pwsh
dotnet test tests\WebDAVClient.UnitTests\WebDAVClient.UnitTests.csproj -c Release --filter "FullyQualifiedName~ReadmeCompilationTests|FullyQualifiedName~DeveloperExamplesTests"
```

Use complete markers only for standalone programs. Label fragments with their
required context/imports. Keep source links and the [guide index](README.md)
consistent with each runnable example. Do not claim independent-server
compatibility from unit/loopback tests.

## Coverage and publishing

```pwsh
dotnet test tests\WebDAVClient.UnitTests\WebDAVClient.UnitTests.csproj -c Release -p:CollectCoverage=true
```

Coverage gates require at least 98% lines and 95% branches separately for each
production module (library and existing smoke sample), on each target. Do not
add production exclusions to reach those gates. The new feature examples are
demonstration code, not an additional production library.

Release builds already generate `.nupkg` and `.snupkg`; do not add a redundant
pack step unless you need another output location. The README is embedded.
Changing the published NuGet copy requires a new package version. Keep package,
assembly/file versions, release notes and documentation synchronized.

PRs targeting `master` run Linux validation without publishing. Successful
code pushes to `master` and manual publication dispatches publish through the
existing workflow. Do not use a publish dispatch as a validation-only rehearsal.
Duplicate versions fail rather than masquerading as new releases.
