$ErrorActionPreference = 'Stop'
$names = @('RUN', 'ENDPOINT', 'SERVER', 'VERSION', 'AUTH', 'REVISION', 'OUTPUT', 'APACHE_PATH_PLUS_ALIAS')
$original = @{}
$hadExitCode = Test-Path Variable:global:LASTEXITCODE
$originalExitCode = $global:LASTEXITCODE
$state = @{ Calls = $null; FailBuild = $false }
foreach ($name in $names) {
    $original[$name] = [Environment]::GetEnvironmentVariable("WEBDAV_INTEROP_$name")
}

function Restore-Variables($values) {
    foreach ($name in $names) {
        if ($null -eq $values[$name]) {
            if (Test-Path -LiteralPath "Env:WEBDAV_INTEROP_$name") {
                Remove-Item -LiteralPath "Env:WEBDAV_INTEROP_$name"
            }
        }
        else {
            [Environment]::SetEnvironmentVariable("WEBDAV_INTEROP_$name", $values[$name])
        }
    }
}

function git {
    $global:LASTEXITCODE = 0
    '0123456789012345678901234567890123456789'
}

Set-Item -Path Function:dotnet -Value ({
    $state.Calls.Add(($args -join ' '))
    $global:LASTEXITCODE = if ($state.FailBuild -and $args[0] -eq 'build') { 1 } else { 0 }
}.GetNewClosure())

try {
    foreach ($mode in @('unset', 'empty', 'configured', 'build-failure')) {
        $expected = @{}
        foreach ($name in $names) {
            $expected[$name] = switch ($mode) {
                'empty' { '' }
                'configured' { "original-$name" }
                default { $null }
            }
        }
        Restore-Variables $expected
        $state.Calls = [Collections.Generic.List[string]]::new()
        $state.FailBuild = $mode -eq 'build-failure'
        $failed = $false
        try {
            & (Join-Path $PSScriptRoot 'Invoke-Compatibility.ps1') -Profile wsgidav
        }
        catch {
            if (!$state.FailBuild -or $_.Exception.Message -ne 'Suite build failed.') { throw }
            $failed = $true
        }
        if ($failed -ne $state.FailBuild) { throw "Unexpected wrapper outcome: $mode" }
        foreach ($name in $names) {
            $actual = [Environment]::GetEnvironmentVariable("WEBDAV_INTEROP_$name")
            if ($actual -cne $expected[$name]) { throw "Wrapper changed $name in mode $mode." }
        }
        if ($state.Calls[0] -notmatch 'build .*tests[\\/]WebDAVClient.InteropTests') {
            throw 'Wrapper did not resolve the moved project.'
        }
        if ($state.FailBuild) {
            if ($state.Calls.Count -ne 1) { throw 'Wrapper ran tests after a failed build.' }
        }
        elseif ($state.Calls.Count -ne 3 -or $state.Calls[1] -notmatch '-f net8.0 ' -or $state.Calls[2] -notmatch '-f net10.0 ') {
            throw 'Wrapper did not invoke both target frameworks.'
        }
    }
}
finally {
    Restore-Variables $original
    if ($hadExitCode) {
        $global:LASTEXITCODE = $originalExitCode
    }
    elseif (Test-Path Variable:global:LASTEXITCODE) {
        Remove-Variable LASTEXITCODE -Scope Global
    }
}

Write-Output 'Wrapper path, both frameworks, and unset/empty/configured environment restoration passed; no network requests.'
