$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
public sealed class SyntheticWireHandler : HttpMessageHandler
{
    private bool m_created;

    public int Calls
    {
        get;
        private set;
    }

    public bool Oversized
    {
        get;
        set;
    }

    public bool Preexisting
    {
        get;
        set;
    }

    public bool RefuseCreation
    {
        get;
        set;
    }

    public int Deletes
    {
        get;
        private set;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        string method = request.Method.Method;
        if (method == "LOCK" || method == "UNLOCK")
        {
            throw new InvalidOperationException("Probe acquired a lock.");
        }
        int code = method switch
        {
            "HEAD" => m_created || Preexisting ? 200 : 404,
            "MKCOL" => RefuseCreation ? 500 : 201,
            "PUT" => 201,
            "PROPPATCH" => 207,
            "DELETE" => 204,
            _ => 500
        };
        if (method == "MKCOL" && code == 201)
        {
            m_created = true;
        }
        if (method == "DELETE")
        {
            Deletes++;
            m_created = false;
        }
        string body = method == "PROPPATCH" ? Oversized ? new string('x', 100) : "<d:multistatus xmlns:d='DAV:'/>" : "";
        return Task.FromResult(new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(body) });
    }
}
'@
$output = Join-Path 'artifacts\interop' ('synthetic-test-' + [Guid]::NewGuid().ToString('N'))
$nativeDirectory = [Environment]::CurrentDirectory
$handler = [SyntheticWireHandler]::new()
$http = [Net.Http.HttpClient]::new($handler)
$probe = Join-Path $PSScriptRoot 'Capture-SyntheticPropertyWire.ps1'
$fixture = 'webdavclient-interop-' + [Guid]::NewGuid().ToString('N')
try {
    [Environment]::CurrentDirectory = $PSScriptRoot
    foreach ($endpoint in @(
        'https://example.test/', 'http://example.test:19081/dav/', 'http://localhost:19081/dav/',
        'http://127.0.0.1:19081/', 'http://127.0.0.1:19082/dav/', 'https://127.0.0.1:19081/dav/',
        'http://127.0.0.1:19081/dav/?q=1', 'http://127.0.0.1:19081/dav/#fragment',
        'http://fixture@127.0.0.1:19081/dav/'
    )) {
        $rejected = $false
        try {
            & $probe -Client $http -Endpoint $endpoint -Profile apache -FixtureProject $fixture -OutputDirectory $output
        }
        catch { $rejected = $true }
        if (!$rejected -or $handler.Calls -ne 0) { throw 'Nonfixture endpoint was not rejected before I/O.' }
    }
    foreach ($guard in @('Preexisting', 'RefuseCreation')) {
        $handler.$guard = $true
        $rejected = $false
        try {
            & $probe -Client $http -Endpoint 'http://127.0.0.1:19081/dav/' -Profile apache -FixtureProject $fixture -OutputDirectory $output
        }
        catch { $rejected = $true }
        if (!$rejected -or $handler.Deletes -ne 0) { throw 'Unconfirmed tree ownership permitted deletion.' }
        $handler.$guard = $false
    }
    & $probe -Client $http -Endpoint 'http://127.0.0.1:19081/dav/' -Profile apache -FixtureProject $fixture -OutputDirectory $output
    $metadata = Get-ChildItem $output -Recurse -Filter capture.json |
        ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json } |
        Where-Object { !$_.failure -and $_.cases.Count -eq 2 }
    if (!$metadata.cleanupConfirmed -or $metadata.cases.Count -ne 2 -or $metadata.capturedHeaders -or $metadata.acquiredLocks) {
        throw 'Synthetic capture/cleanup regression.'
    }
    $handler.Oversized = $true
    $rejected = $false
    try {
        & $probe -Client $http -Endpoint 'http://127.0.0.1:19081/dav/' -Profile apache -FixtureProject $fixture -OutputDirectory $output -MaxResponseBytes 32
    }
    catch { $rejected = $true }
    if (!$rejected) { throw 'Oversized synthetic response was not rejected.' }
    $failed = Get-ChildItem $output -Recurse -Filter capture.json |
        ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json } |
        Where-Object { $_.failure -and $_.byteCapPerResponse -eq 32 }
    if (!$failed.cleanupConfirmed) { throw 'Oversized response prevented independent cleanup.' }
}
finally {
    [Environment]::CurrentDirectory = $nativeDirectory
    $http.Dispose()
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
}
Write-Output 'Synthetic probe guards, byte cap, no-lock behavior and independent cleanup passed; no network requests.'
