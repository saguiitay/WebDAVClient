[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [System.Net.Http.HttpClient] $Client,
    [Parameter(Mandatory)]
    [Uri] $Endpoint,
    [Parameter(Mandatory)]
    [ValidateSet('apache', 'sabredav')]
    [string] $Profile,
    [Parameter(Mandatory)]
    [ValidatePattern('^webdavclient-interop-[a-f0-9]{32}$')]
    [string] $FixtureProject,
    [string] $OutputDirectory = 'artifacts\interop\synthetic-wire',
    [ValidateRange(1, 65536)]
    [int] $MaxResponseBytes = 65536
)
$ErrorActionPreference = 'Stop'
$port = if ($Profile -eq 'apache') { 19081 } else { 19083 }
$path = if ($Profile -eq 'apache') { '/dav/' } else { '/' }
if ($Endpoint.Scheme -ne 'http' -or $Endpoint.Host -ne '127.0.0.1' -or $Endpoint.Port -ne $port -or
    $Endpoint.AbsolutePath -ne $path -or $Endpoint.UserInfo -or $Endpoint.Query -or $Endpoint.Fragment) {
    throw 'Raw capture is restricted to the exact loopback fixture binding, never operator endpoints.'
}
$tree = 'synthetic-wire-' + [Guid]::NewGuid().ToString('N') + '/'
$created = $false
$cleanupConfirmed = $false
$cases = @()
$failure = $null
$directory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
    (Join-Path $OutputDirectory "$Profile-$($tree.TrimEnd('/'))"))
New-Item $directory -ItemType Directory -Force | Out-Null
$deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(30))

function Send-SyntheticRequest([string] $Method, [string] $Relative, [string] $Body, [Threading.CancellationToken] $Token) {
    if ($Method -notin @('HEAD', 'MKCOL', 'PUT', 'PROPPATCH', 'DELETE') -or !$Relative.StartsWith($tree, [StringComparison]::Ordinal)) {
        throw 'Synthetic probe cannot access another tree or acquire locks.'
    }
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), [Uri]::new($Endpoint, $Relative))
    if ($Method -in @('PUT', 'PROPPATCH')) {
        $request.Content = [Net.Http.StringContent]::new($Body, [Text.Encoding]::UTF8, 'application/xml')
    }
    try {
        $response = $Client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $Token).GetAwaiter().GetResult()
        try {
            $stream = $response.Content.ReadAsStreamAsync($Token).GetAwaiter().GetResult()
            $buffer = [byte[]]::new($MaxResponseBytes + 1)
            $count = 0
            while ($count -lt $buffer.Length) {
                $read = $stream.ReadAsync($buffer, $count, $buffer.Length - $count, $Token).GetAwaiter().GetResult()
                if ($read -eq 0) { break }
                $count += $read
            }
            if ($count -gt $MaxResponseBytes) { throw 'Synthetic response exceeded the byte cap; body not retained.' }
            $bytes = [byte[]]::new($count)
            [Array]::Copy($buffer, $bytes, $count)
            return [pscustomobject]@{ status = [int]$response.StatusCode; bytes = $bytes }
        }
        finally { $response.Dispose() }
    }
    finally { $request.Dispose() }
}

try {
    $preflight = Send-SyntheticRequest HEAD $tree '' $deadline.Token
    if ($preflight.status -ne 404) { throw 'Synthetic tree absence was not confirmed.' }
    $mkcol = Send-SyntheticRequest MKCOL $tree '' $deadline.Token
    if ($mkcol.status -ne 201) { throw 'Synthetic tree ownership was not confirmed.' }
    $created = $true
    $body = [Xml.Linq.XElement]::Parse(
        '<propertyupdate xmlns="DAV:"><set><prop><marker xmlns="urn:webdavclient:interop:v1">interop-v1</marker></prop></set></propertyupdate>'
    ).ToString([Xml.Linq.SaveOptions]::DisableFormatting)
    foreach ($case in @(@{ id = 'ascii'; name = 'ascii.bin' }, @{ id = 'unicode'; name = 'café 中文 + % #.bin' })) {
        $relative = $tree + [Uri]::EscapeDataString($case.name)
        $put = Send-SyntheticRequest PUT $relative 'synthetic' $deadline.Token
        if ($put.status -ne 201) { throw 'Synthetic upload was not confirmed.' }
        $patch = Send-SyntheticRequest PROPPATCH $relative $body $deadline.Token
        [IO.File]::WriteAllText((Join-Path $directory "$($case.id)-request.xml"), $body, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllBytes((Join-Path $directory "$($case.id)-response.xml"), $patch.bytes)
        $cases += [ordered]@{
            case = $case.id
            requestPath = [Uri]::new($Endpoint, $relative).AbsolutePath
            httpStatus = $patch.status
            responseBytes = $patch.bytes.Length
        }
    }
}
catch { $failure = $_.Exception.GetType().Name }
finally {
    $cleanup = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(10))
    try {
        if ($created) {
            $delete = Send-SyntheticRequest DELETE $tree '' $cleanup.Token
            if ($delete.status -eq 204) {
                $absent = Send-SyntheticRequest HEAD $tree '' $cleanup.Token
                $cleanupConfirmed = $absent.status -eq 404
            }
        }
    }
    catch { $failure = 'cleanup-failed' }
    finally { $cleanup.Dispose(); $deadline.Dispose() }
    [ordered]@{
        schemaVersion = 1
        purpose = 'Explicit loopback-owned-fixture synthetic wire diagnostic; not a compatibility pass.'
        profile = $Profile
        fixtureProject = $FixtureProject
        runTree = $tree
        byteCapPerResponse = $MaxResponseBytes
        capturedHeaders = $false
        acquiredLocks = $false
        cases = $cases
        cleanupConfirmed = $cleanupConfirmed
        failure = $failure
    } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $directory 'capture.json')
}
if ($failure -or !$cleanupConfirmed -or $cases.Count -ne 2) {
    throw 'Synthetic fixture probe was incomplete; inspect its bounded diagnostic artifact.'
}
Write-Output 'Synthetic property XML captured from two fresh run-owned files; cleanup and absence confirmed; no headers or locks.'
