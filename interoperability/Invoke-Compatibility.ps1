[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('wsgidav', 'apache', 'nginx', 'sabredav', 'nextcloud', 'owncloud', 'iis')]
    [string] $Profile,
    [ValidateSet('net8.0', 'net10.0')]
    [string[]] $Framework = @('net8.0', 'net10.0'),
    [string] $OutputDirectory = 'artifacts\interop'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$names = @('RUN', 'ENDPOINT', 'SERVER', 'VERSION', 'AUTH', 'REVISION', 'OUTPUT')
$saved = @{}
foreach ($name in $names) {
    $saved[$name] = [Environment]::GetEnvironmentVariable("WEBDAV_INTEROP_$name")
}
try {
    $profileData = (Get-Content (Join-Path $PSScriptRoot 'profiles.json') -Raw | ConvertFrom-Json).profiles |
        Where-Object id -EQ $Profile
    $env:WEBDAV_INTEROP_RUN = '1'
    $env:WEBDAV_INTEROP_SERVER = $Profile
    if (!$env:WEBDAV_INTEROP_VERSION) {
        $env:WEBDAV_INTEROP_VERSION = $profileData.version
    }
    if ($Profile -eq 'iis' -and $env:WEBDAV_INTEROP_VERSION -eq 'operator-required') {
        throw 'IIS requires an explicit observed WEBDAV_INTEROP_VERSION.'
    }
    if (!$env:WEBDAV_INTEROP_AUTH) {
        $env:WEBDAV_INTEROP_AUTH = $profileData.authentication[0]
    }
    if (!$env:WEBDAV_INTEROP_ENDPOINT) {
        if ($Profile -eq 'iis') {
            throw 'IIS requires an explicit operator-owned WEBDAV_INTEROP_ENDPOINT.'
        }
        $path = $profileData.path.Replace('{user}', [Uri]::EscapeDataString($env:WEBDAV_INTEROP_USER ?? ''))
        $env:WEBDAV_INTEROP_ENDPOINT = "http://127.0.0.1:$($profileData.port)$path"
    }
    $env:WEBDAV_INTEROP_REVISION = (& git rev-parse HEAD)
    & dotnet build WebDAVClient.InteropTests -c Release --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'Suite build failed.'
    }
    $failed = $false
    foreach ($tfm in $Framework) {
        $env:WEBDAV_INTEROP_OUTPUT = Join-Path $OutputDirectory "$Profile-$($env:WEBDAV_INTEROP_AUTH)-$tfm.json"
        & dotnet run --project WebDAVClient.InteropTests -c Release -f $tfm --no-build -- --run
        $failed = $failed -or $LASTEXITCODE -ne 0
    }
    if ($failed) {
        throw 'One or more compatibility runs were nonpassing. Inspect the sanitized JSON matrices.'
    }
}
finally {
    foreach ($name in $names) {
        [Environment]::SetEnvironmentVariable("WEBDAV_INTEROP_$name", $saved[$name])
    }
    Pop-Location
}
