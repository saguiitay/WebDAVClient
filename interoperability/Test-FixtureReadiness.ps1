$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
public sealed class InteropReadinessHandler : HttpMessageHandler
{
    public bool Denied
    {
        get;
        set;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var status = Denied ? HttpStatusCode.Unauthorized :
            request.Method == HttpMethod.Options ? HttpStatusCode.OK : HttpStatusCode.NotImplemented;
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("") });
    }
}
'@
$handler = [InteropReadinessHandler]::new()
$client = [System.Net.Http.HttpClient]::new($handler)
try {
    $probe = Join-Path $PSScriptRoot 'Wait-FixtureReady.ps1'
    $options = & $probe -Client $client -Endpoint 'http://localhost/' -Method OPTIONS -TimeoutSeconds 1 -PollMilliseconds 10
    if (!$options.ready -or $options.status -ne 200) { throw 'OPTIONS readiness regression.' }
    $get = & $probe -Client $client -Endpoint 'http://localhost/' -Method GET -TimeoutSeconds 1 -PollMilliseconds 10
    if ($get.ready -or $get.status -ne 501) { throw 'GET rejection must not become a readiness pass.' }
    $handler.Denied = $true
    $denied = & $probe -Client $client -Endpoint 'http://localhost/' -Method OPTIONS -TimeoutSeconds 1 -PollMilliseconds 10
    if ($denied.ready -or $denied.status -ne 401) { throw 'Authentication denial must not become a readiness pass.' }
}
finally { $client.Dispose() }
Write-Output 'Fixture readiness regressions passed (OPTIONS=200, GET=501, denied=401); no network requests.'
