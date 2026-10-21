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
| `tests\WebDAVClient.InteropTests` | Offline harness regressions and explicitly opted-in independent-server executable |
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

The [RFC 4918 requirement ledger](rfc4918-requirements.md) maps client
implementation and deterministic evidence separately from server obligations
and unsupported profiles. Update it when changing protocol contracts.

## Independent-server compatibility tooling

Ordinary solution tests include the separate harness's offline regressions and
do not contact a WebDAV server:

```pwsh
dotnet test tests\WebDAVClient.InteropTests\WebDAVClient.InteropTests.csproj -c Release
.\interoperability\Test-FixtureReadiness.ps1
.\interoperability\Test-SyntheticPropertyWire.ps1
```

The [interoperability guide](../interoperability/README.md) covers explicit
mutation opt-in, disposable fixtures, bounded diagnostics and historical evidence.
Live-server runs are not part of ordinary PR validation and do not publish
packages. Never promote offline results or historical source runs to
certification of a current package/server combination.

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

PRs targeting `master` and code pushes to `master` run Linux validation without
publishing. Push code freely without bumping the package version.

To publish, prepare a new package version and matching changelog entry, push the
commit, then open **Actions > Validate and publish > Run workflow** and select
the branch containing that commit. Equivalently, run:

```pwsh
gh workflow run publish.yml --ref master
```

Only this manual dispatch publishes to NuGet, after the same build, tests and
coverage gates pass. It also creates the GitHub release. Do not use a manual
dispatch as a validation-only rehearsal. Duplicate versions still fail rather
than masquerading as new releases. Validation runs have separate concurrency
groups, so a push cannot cancel an in-progress publication.

## GitHub releases

For each public version, add a short entry headed `## [x.y.z] - YYYY-MM-DD` to
the root [CHANGELOG.md](../CHANGELOG.md). Lead with what users can now do,
important fixes, and any breaking behavior or migration requirement. Keep old
implementation details in the [historical reference](reference.md#release-notes),
not in the new version's summary.

CI validates that the package version has exactly one nonempty changelog entry
on PRs and before publishing. After a successful NuGet push, a separate job
creates a `v<version>` tag at that exact commit and a GitHub release containing
only that version's notes and its NuGet link. Only this post-publication job
has repository write permission; pushes and PRs never create tags or releases.

If GitHub release creation fails after NuGet publication, rerun only the failed
release job. Do not republish the immutable NuGet version. An existing tag must
point to the original published commit; never retarget it to a later fix.

Preview the generated notes without publishing:

```pwsh
.\scripts\Get-ReleaseNotes.ps1 -Version 3.2.0
```
