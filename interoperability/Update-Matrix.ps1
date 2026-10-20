[CmdletBinding()]
param(
    [string] $ResultsDirectory = (Join-Path $PSScriptRoot 'results')
)
$ErrorActionPreference = 'Stop'
$profiles = (Get-Content (Join-Path $PSScriptRoot 'profiles.json') -Raw | ConvertFrom-Json).profiles
$rows = @()
foreach ($profile in $profiles) {
    $runs = @()
    foreach ($file in @(Get-ChildItem $ResultsDirectory -Filter "$($profile.id)-*-net*.json" -File)) {
        $json = Get-Content $file.FullName -Raw
        if (!(Test-Json -Json $json -SchemaFile (Join-Path $PSScriptRoot 'result.schema.json'))) {
            throw "Invalid result contract: $($file.Name)"
        }
        $run = $json | ConvertFrom-Json
        if ($run.server -ne $profile.id) {
            throw "Profile/result identity mismatch: $($file.Name)"
        }
        $runs += [ordered]@{
            evidence = $file.Name
            startedUtc = $run.startedUtc
            runtime = $run.runtime
            operatingSystem = $run.operatingSystem
            serverVersion = $run.serverVersion
            authentication = $run.authentication
            transport = $run.transport
            sourceRevision = $run.sourceRevision
            succeeded = $run.succeeded
            passed = @($run.observations | Where-Object outcome -EQ 'passed').Count
            unsupported = @($run.observations | Where-Object outcome -EQ 'unsupported' | ForEach-Object operation)
            unavailableProperties = @($run.observations | Where-Object outcome -EQ 'unavailable-property' | ForEach-Object operation)
            nonpassing = @($run.observations | Where-Object outcome -NotIn @('passed', 'unsupported', 'unavailable-property'))
            cleanup = @($run.observations | Where-Object operation -EQ 'cleanup-tree' | ForEach-Object outcome)
        }
    }
    $rows += [ordered]@{
        server = $profile.id
        configuredVersion = $profile.version
        executionStatus = if ($runs.Count -eq 0) { 'not-executed' } elseif (@($runs | Where-Object { !$_.succeeded }).Count -gt 0) { 'observed-nonpassing' } else { 'observed-compatible-subset' }
        evidenceGap = if ($runs.Count -eq 0) { 'No committed independent-server execution evidence. See README fixture limitations and tooling.json.' } else { $null }
        runs = $runs
    }
}
$matrix = [ordered]@{
    schemaVersion = 1
    suiteVersion = '1.0'
    generatedUtc = [DateTimeOffset]::UtcNow
    scope = 'Independent-server observations only; not full RFC conformance, publication, or unobserved configuration support.'
    servers = $rows
}
$matrix | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $ResultsDirectory 'compatibility-matrix.json')
