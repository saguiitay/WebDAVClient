[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [System.Net.Http.HttpClient] $Client,
    [Parameter(Mandatory)]
    [Uri] $Endpoint,
    [Parameter(Mandatory)]
    [ValidateSet('GET', 'OPTIONS')]
    [string] $Method,
    [ValidateRange(1, 300)]
    [int] $TimeoutSeconds = 300,
    [ValidateRange(10, 2000)]
    [int] $PollMilliseconds = 2000,
    [System.Diagnostics.Process] $ServerProcess
)
$until = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$status = $null
$attempts = 0
$failure = 'timeout'
while ([DateTime]::UtcNow -lt $until) {
    if ($ServerProcess -and $ServerProcess.HasExited) {
        $failure = 'process-exited'
        break
    }
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), $Endpoint)
    try {
        $attempts++
        $response = $Client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $status = [int] $response.StatusCode
            if ($status -eq 200) {
                return [pscustomobject]@{ ready = $true; method = $Method; status = $status; attempts = $attempts; failure = $null }
            }
        }
        finally { $response.Dispose() }
    }
    catch { $failure = 'request-failed' }
    finally { $request.Dispose() }
    Start-Sleep -Milliseconds $PollMilliseconds
}
[pscustomobject]@{ ready = $false; method = $Method; status = $status; attempts = $attempts; failure = $failure }
