$ErrorActionPreference = 'Stop'
$names = @('RUN', 'ENDPOINT', 'SERVER', 'VERSION', 'AUTH', 'REVISION', 'OUTPUT', 'APACHE_PATH_PLUS_ALIAS', 'ABSOLUTE_PATH_RESOURCE_TAGS')
$original = @{}
$hadExitCode = Test-Path Variable:global:LASTEXITCODE
$originalExitCode = $global:LASTEXITCODE
$state = @{ Calls = $null; Formats = $null; Outputs = $null; FailBuild = $false }
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
    if ($args[0] -eq 'run') {
        $state.Formats.Add([Environment]::GetEnvironmentVariable('WEBDAV_INTEROP_ABSOLUTE_PATH_RESOURCE_TAGS'))
        $state.Outputs.Add($env:WEBDAV_INTEROP_OUTPUT)
    }
    $global:LASTEXITCODE = if ($state.FailBuild -and $args[0] -eq 'build') { 1 } else { 0 }
}.GetNewClosure())

try {
    foreach ($mode in @('unset', 'empty', 'configured', 'build-failure')) {
        foreach ($absolutePathTags in @($false, $true)) {
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
            $state.Formats = [Collections.Generic.List[object]]::new()
            $state.Outputs = [Collections.Generic.List[string]]::new()
            $state.FailBuild = $mode -eq 'build-failure'
            $failed = $false
            try {
                & (Join-Path $PSScriptRoot 'Invoke-Compatibility.ps1') -Profile wsgidav -AbsolutePathResourceTags:$absolutePathTags
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
            if (!$state.FailBuild) {
                $expectedFlag = if ($absolutePathTags) { '1' } else { $null }
                if ($state.Formats.Count -ne 2 -or @($state.Formats | Where-Object { $_ -cne $expectedFlag }).Count) {
                    throw "Resource-tag selection was not explicit in mode $mode (absolute paths: $absolutePathTags)."
                }
                foreach ($output in $state.Outputs) {
                    if ($output.Contains('-absolute-path-') -ne $absolutePathTags) {
                        throw 'Resource-tag variants must have distinct result filenames.'
                    }
                }
            }
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

Write-Output 'Wrapper path, both frameworks, explicit resource-tag variants and exact environment restoration passed; no network requests.'
