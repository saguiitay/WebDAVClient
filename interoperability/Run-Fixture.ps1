[CmdletBinding()]
param(
    [ValidateSet('wsgidav', 'apache', 'nginx', 'sabredav', 'nextcloud', 'owncloud')]
    [string] $Profile = 'wsgidav',
    [ValidateSet('basic', 'anonymous')]
    [string] $Authentication = 'basic',
    [switch] $CaptureSyntheticPropertyWire,
    [switch] $ApachePathPlusAlias,
    [switch] $AbsolutePathResourceTags
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$process = $null
$composeStarted = $false
$captureFailed = $false
$compose = Join-Path $PSScriptRoot 'fixtures\compose.yml'
$projectName = 'webdavclient-interop-' + [Guid]::NewGuid().ToString('N')
$savedAuth = $env:WEBDAV_INTEROP_AUTH
$savedUser = $env:WEBDAV_INTEROP_USER
$savedPassword = $env:WEBDAV_INTEROP_PASSWORD
$savedEndpoint = $env:WEBDAV_INTEROP_ENDPOINT
$savedVersion = $env:WEBDAV_INTEROP_VERSION
try {
    if ($ApachePathPlusAlias -and $Profile -ne 'apache') {
        throw 'Path-plus target aliasing is an explicit Apache-only profile assumption.'
    }
    if ($savedEndpoint -or $savedVersion) {
        throw 'Run-Fixture owns the local endpoint/version. Clear endpoint/version overrides or use Invoke-Compatibility for existing servers.'
    }
    if ($CaptureSyntheticPropertyWire -and $Profile -notin @('apache', 'sabredav')) {
        throw 'Synthetic wire capture is restricted to runner-owned Apache/SabreDAV loopback containers.'
    }
    if ($Authentication -eq 'anonymous' -and $Profile -notin @('wsgidav', 'nginx')) {
        throw 'Anonymous fixture mode is available only for WsgiDAV and built-in nginx DAV.'
    }
    $env:WEBDAV_INTEROP_USER = 'interop'
    $env:WEBDAV_INTEROP_PASSWORD = [Guid]::NewGuid().ToString('N') + 'aZ9!'
    $env:WEBDAV_INTEROP_AUTH = 'basic'
    if ($Profile -eq 'nginx' -or $Authentication -eq 'anonymous') {
        $env:WEBDAV_INTEROP_USER = $null
        $env:WEBDAV_INTEROP_PASSWORD = $null
        $env:WEBDAV_INTEROP_AUTH = 'anonymous'
    }
    $data = (Get-Content (Join-Path $PSScriptRoot 'profiles.json') -Raw | ConvertFrom-Json).profiles | Where-Object id -EQ $Profile
    $path = $data.path.Replace('{user}', [Uri]::EscapeDataString($env:WEBDAV_INTEROP_USER ?? ''))
    $endpoint = "http://127.0.0.1:$($data.port)$path"
    # Refuse to mistake an existing listener for this run's fixture.
    $probe = [System.Net.Sockets.TcpClient]::new()
    try {
        $probe.Connect('127.0.0.1', [int] $data.port)
        throw 'Fixture port is already in use; no server was started or tested.'
    }
    catch [System.Net.Sockets.SocketException] { }
    finally { $probe.Dispose() }
    if ($Profile -eq 'wsgidav') {
        $python = Join-Path $PSScriptRoot ($IsWindows ? '.venv\Scripts\python.exe' : '.venv/bin/python')
        if (!(Test-Path $python)) {
            throw 'Provision interoperability\.venv from fixtures\wsgidav\requirements.txt first.'
        }
        $fixtureRoot = Join-Path $PSScriptRoot "local\$projectName"
        New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
        $serve = Join-Path $PSScriptRoot 'fixtures\wsgidav\serve.py'
        $serverArguments = @('-u', "`"$serve`"", '--root', "`"$fixtureRoot`"", '--port', $data.port)
        if ($env:WEBDAV_INTEROP_AUTH -eq 'basic') {
            $serverArguments += '--basic'
        }
        $process = Start-Process $python -ArgumentList $serverArguments -PassThru -NoNewWindow
    }
    else {
        & docker info --format '{{.ServerVersion}}'
        if ($LASTEXITCODE -ne 0) {
            throw 'Docker daemon unavailable. No container compatibility result was obtained.'
        }
        # Compose expands variables across inactive profiles too; nginx itself remains anonymous.
        if ($Profile -eq 'nginx') {
            $env:WEBDAV_INTEROP_USER = 'unused'
            $env:WEBDAV_INTEROP_PASSWORD = 'unused-fixture-value'
        }
        $composeStarted = $true
        & docker compose -p $projectName -f $compose --profile $Profile up --detach --build $Profile
        if ($LASTEXITCODE -ne 0) {
            throw 'Fixture startup failed.'
        }
        New-Item -ItemType Directory -Path 'artifacts\interop' -Force | Out-Null
        & docker compose -p $projectName -f $compose images --format json |
            Set-Content "artifacts\interop\$Profile-container-images.json"
        if ($Profile -eq 'nginx') {
            $env:WEBDAV_INTEROP_USER = $null
            $env:WEBDAV_INTEROP_PASSWORD = $null
        }
    }
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseProxy = $false
    $http = [System.Net.Http.HttpClient]::new($handler)
    $http.Timeout = [TimeSpan]::FromSeconds(3)
    if ($env:WEBDAV_INTEROP_AUTH -eq 'basic') {
        $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$($env:WEBDAV_INTEROP_USER):$($env:WEBDAV_INTEROP_PASSWORD)"))
        $http.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Basic', $encoded)
    }
    try {
        # Filesystem DAV collections need not implement GET (SabreDAV returns 501 without its optional browser plugin).
        $method = if ($Profile -eq 'nginx') { 'GET' } else { 'OPTIONS' }
        $readiness = & (Join-Path $PSScriptRoot 'Wait-FixtureReady.ps1') -Client $http -Endpoint $endpoint -Method $method -ServerProcess $process
        New-Item -ItemType Directory -Path 'artifacts\interop' -Force | Out-Null
        [ordered]@{
            schemaVersion = 1
            profile = $Profile
            observedUtc = [DateTimeOffset]::UtcNow
            readiness = $readiness
        } | ConvertTo-Json -Depth 5 | Set-Content "artifacts\interop\$Profile-fixture-startup.json"
        if (!$readiness.ready) {
            throw "Fixture readiness failed (method=$method, status=$($readiness.status)); see sanitized startup artifact."
        }
        if ($CaptureSyntheticPropertyWire) {
            if (!$composeStarted) { throw 'Synthetic capture requires a container started by this invocation.' }
            try {
                & (Join-Path $PSScriptRoot 'Capture-SyntheticPropertyWire.ps1') -Client $http -Endpoint $endpoint -Profile $Profile -FixtureProject $projectName
            }
            catch {
                $captureFailed = $true
                Write-Warning 'Synthetic fixture capture was incomplete; ordinary compatibility checks still run. Inspect its bounded artifact.'
            }
        }
    }
    finally { $http.Dispose() }
    & (Join-Path $PSScriptRoot 'Invoke-Compatibility.ps1') -Profile $Profile -ApachePathPlusAlias:$ApachePathPlusAlias -AbsolutePathResourceTags:$AbsolutePathResourceTags
    if ($captureFailed) { throw 'Synthetic fixture capture was incomplete; compatibility results were retained separately.' }
}
finally {
    $teardownFailure = $null
    try {
        if ($process -and !$process.HasExited) {
            Stop-Process -Id $process.Id
            $process.WaitForExit()
        }
        if ($composeStarted) {
            if ($Profile -eq 'nginx') {
                $env:WEBDAV_INTEROP_USER = 'unused'
                $env:WEBDAV_INTEROP_PASSWORD = 'unused-fixture-value'
            }
            & docker compose -p $projectName -f $compose --profile $Profile down --volumes --remove-orphans
            if ($LASTEXITCODE -ne 0) {
                $teardownFailure = "Fixture teardown failed for Compose project $projectName."
            }
        }
        if ($Profile -eq 'wsgidav' -and $fixtureRoot -and (Test-Path $fixtureRoot)) {
            if (@(Get-ChildItem $fixtureRoot -Force).Count -ne 0) {
                $teardownFailure = "Fixture share is not empty after cleanup: $fixtureRoot"
            }
            else {
                Remove-Item $fixtureRoot
            }
        }
    }
    finally {
        $env:WEBDAV_INTEROP_AUTH = $savedAuth
        $env:WEBDAV_INTEROP_USER = $savedUser
        $env:WEBDAV_INTEROP_PASSWORD = $savedPassword
        $env:WEBDAV_INTEROP_ENDPOINT = $savedEndpoint
        $env:WEBDAV_INTEROP_VERSION = $savedVersion
        Pop-Location
    }
    if ($teardownFailure) {
        throw $teardownFailure
    }
}
