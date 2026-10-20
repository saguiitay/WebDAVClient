[CmdletBinding()]
param(
    [ValidateSet('wsgidav', 'apache', 'nginx', 'sabredav', 'nextcloud', 'owncloud')]
    [string] $Profile = 'wsgidav',
    [ValidateSet('basic', 'anonymous')]
    [string] $Authentication = 'basic'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$process = $null
$composeStarted = $false
$compose = Join-Path $PSScriptRoot 'fixtures\compose.yml'
$projectName = 'webdavclient-interop-' + [Guid]::NewGuid().ToString('N')
$savedAuth = $env:WEBDAV_INTEROP_AUTH
$savedUser = $env:WEBDAV_INTEROP_USER
$savedPassword = $env:WEBDAV_INTEROP_PASSWORD
$savedEndpoint = $env:WEBDAV_INTEROP_ENDPOINT
$savedVersion = $env:WEBDAV_INTEROP_VERSION
try {
    if ($savedEndpoint -or $savedVersion) {
        throw 'Run-Fixture owns the local endpoint/version. Clear endpoint/version overrides or use Invoke-Compatibility for existing servers.'
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
        $ready = $false
        $until = [DateTime]::UtcNow.AddMinutes(5)
        while ([DateTime]::UtcNow -lt $until) {
            if ($process -and $process.HasExited) {
                throw 'Independent server process exited before readiness.'
            }
            try {
                $response = $http.GetAsync($endpoint).GetAwaiter().GetResult()
                $ready = [int]$response.StatusCode -eq 200
                $response.Dispose()
            }
            catch { }
            if ($ready) {
                break
            }
            Start-Sleep -Seconds 2
        }
        if (!$ready) {
            throw 'Fixture did not become ready within five minutes; no green result is inferred.'
        }
    }
    finally { $http.Dispose() }
    & (Join-Path $PSScriptRoot 'Invoke-Compatibility.ps1') -Profile $Profile
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
