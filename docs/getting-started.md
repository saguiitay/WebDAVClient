# Getting started: authenticate, list and download

**Example:** [GettingStarted.cs](../examples/WebDAVClient.Examples/GettingStarted.cs).
The complete copy/paste program is in the [root README](../README.md).
Authentication and CLI error handling for the runnable version live in
[Program.cs](../examples/WebDAVClient.Examples/Program.cs).

## Prerequisites

- For a NuGet application: the .NET 8 or .NET 10 SDK, a WebDAV collection and
  an account with list/read access.
- The actual WebDAV endpoint, not a browser login page. It must end in `/`.
- An existing file directly inside that collection. The example downloads the
  configured file, not an arbitrary URL from server metadata.

For your own application:

```pwsh
dotnet new console --name MyWebDavApp
Set-Location MyWebDavApp
dotnet add package WebDAVClient
```

Paste the README program into `Program.cs`. It uses credentials from the process
environment, normal certificate validation and a 60-second operation budget.
Use your secret manager or local environment; do not commit passwords or tokens.

## Run the repository example

Building these multi-targeted repository examples requires the .NET 10 SDK.
Install the .NET 8 runtime too if you select `-f net8.0`.

From the repository root, configure your own values in PowerShell:

```pwsh
$env:WEBDAV_ENDPOINT = "https://dav.example.com/dav/"
$env:WEBDAV_AUTH = "credentials"
$env:WEBDAV_USERNAME = "your-user"
# Set WEBDAV_PASSWORD through your local secret-management mechanism.
$env:WEBDAV_REMOTE_FILE = "example.txt"
$env:WEBDAV_OUTPUT = "download.bin"
dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- getting-started --validate
dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- getting-started
```

`--validate` checks endpoint/authentication setup offline; it does not prove the
account can access a server or that the remote file exists. Feature-specific
inputs such as the remote filename are needed when executing the operation.
See [authentication](authentication.md) for app passwords, bearer tokens,
anonymous access and Windows credentials.

## What happens

The example first sends PROPFIND at depth one and checks its reported outcomes.
It prints resource hrefs, including the collection itself. It then GETs the
configured filename, checks the status before opening a local destination,
and copies the response stream with cancellation. It never converts a remote
filename into an automatic local path.

`WEBDAV_REMOTE_FILE` is one filename, not an already escaped URL or nested path.
In application code use `DavPath.FromSegments("reports", "example.txt")` for
multiple unescaped segments. Use `DavPath.Parse` only for already escaped
references; leading `/` means origin-relative on the modern API.

## Outcomes and troubleshooting

Successful output lists the resources and ends with `Download complete.`
Exit code 1 means an operation/transfer failed, 2 means invalid executable
configuration, and 130 means cancellation or deadline expiration.

| Symptom | Check |
|---|---|
| 301/302/307/308 | Supply the final collection endpoint; automatic redirects are deliberately disabled |
| 401/403 | Authentication mode, app password, account permissions and the WebDAV path |
| 404 | The endpoint and `WEBDAV_REMOTE_FILE`; this example does not create a missing file |
| Local file already exists | Choose another `WEBDAV_OUTPUT`; files are opened with `CreateNew` |
| Cancellation or truncated download | A partial local file may remain; do not treat it as a complete download |

For an application requiring atomic completion, copy into a run-owned temporary
file, validate the transfer, then rename without replacement. The example does
not append, retry or claim resumable downloads. Keep or explicitly remove a
known partial file after inspecting the failure.

Do not log tokens, passwords or unredacted application paths. See
[errors and ownership](errors-and-lifetimes.md) and [server compatibility](compatibility.md).
