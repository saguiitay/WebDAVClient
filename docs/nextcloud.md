# Nextcloud integration

Connect to an authenticated Nextcloud file collection, inspect file metadata and
download an existing file. The `nextcloud` command sends PROPFIND and GET only;
it never changes remote files.

**Example:** [CloudStorage.cs](../examples/WebDAVClient.Examples/CloudStorage.cs),
with [authentication and error handling](../examples/WebDAVClient.Examples/Program.cs).
These instructions follow Nextcloud's documented API; compatibility with your
server version and configuration still needs checking.

## Before you run

- Use an HTTPS WebDAV URL from the Files application's settings. A typical
  endpoint is `https://cloud.example.com/nextcloud/remote.php/dav/files/alice/`.
  Keep any installation or reverse-proxy prefix and the trailing `/`.
- Use the user ID in the WebDAV URL, not an assumed display name or email address.
  Copy the supplied URL rather than constructing it from your login name.
- Create an app password in your personal security settings, particularly when
  two-factor authentication or external login is enabled. Use the username and
  app password provided there as credentials, **not as a bearer token**.
- Choose an existing file in the target collection and a new local output path.
  To download from a subfolder, use that folder's WebDAV URL as the endpoint.
- Install the .NET 10 SDK to build the checkout. Run commands from the repository
  root; use `net8.0` instead if you also have the .NET 8 runtime installed.

## Run the example

This PowerShell example prompts for the app password rather than putting it in
source code or command history. Replace the endpoint and filename with yours.

```pwsh
$credential = Get-Credential -UserName "alice" -Message "Nextcloud app password"
$env:WEBDAV_ENDPOINT = "https://cloud.example.com/nextcloud/remote.php/dav/files/alice/Documents/"
$env:WEBDAV_AUTH = "credentials"
$env:WEBDAV_USERNAME = $credential.UserName
$env:WEBDAV_PASSWORD = $credential.GetNetworkCredential().Password
$env:WEBDAV_REMOTE_FILE = "report + notes.txt"
$env:WEBDAV_OUTPUT = ".\nextcloud-download.txt"
try {
    dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- nextcloud --validate
    if ($LASTEXITCODE -ne 0) { throw "Configuration validation failed." }
    dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- nextcloud
} finally {
    Remove-Item Env:\WEBDAV_PASSWORD
    $credential = $null
}
```

`--validate` checks endpoint/authentication configuration offline; it does not
check server access, the remote file or the local destination. Environment
variables are process configuration, not a secret vault; supply credentials
through your deployment's secret manager in a deployed application.

`WEBDAV_REMOTE_FILE` is one **unescaped filename**, not a URL or a slash-separated
path. `DavPath.FromSegments` encodes spaces, plus signs, Unicode and literal
percent signs. The download uses your configured filename, not a returned href.

## What you get

The command fully reads a depth-one listing of the collection and its immediate
children, printing hrefs, property statuses and available text values:

| Property | Meaning |
|---|---|
| `DAV:displayname`, `DAV:resourcetype`, `DAV:getetag` | Standard name, resource type and opaque ETag; structured values retain their status rather than being flattened into text |
| `{http://owncloud.org/ns}fileid` | Server-local file identifier, retained as text |
| `{http://owncloud.org/ns}permissions` | Server-reported permission string, not an authorization guarantee |
| `{http://owncloud.org/ns}size` | Server-reported size; not a substitute for validating downloaded bytes |
| `{http://nextcloud.org/ns}has-preview` | Nextcloud preview metadata; this does not download or generate a preview |

Both namespaces are intentional: Nextcloud uses properties in the ownCloud
namespace too. The XML prefix itself is irrelevant.

An optional property reported as 404 remains unavailable; the example does not
invent a value. A resource failure or other failed property status stops the
workflow before GET. After a complete successful listing, it downloads the
configured file to a **new** local file and prints `Download complete.`

## Troubleshooting and boundaries

| Symptom | Check |
|---|---|
| 401 | App-password username/password, revoked credentials and whether your provider requires an app password |
| 403 | Account access to the collection/file, server policy and proxy rules |
| 404 | User ID, installation prefix, collection URL and exact filename |
| Redirect or HTML instead of DAV XML | Use the final HTTPS WebDAV URL, not the browser Files page; redirects are deliberately disabled |
| TLS failure | Install the appropriate trusted CA; do not disable certificate validation |
| Local file error | Choose a writable path that does not already exist |

A failed download can leave a partial local file; it is not resumed or
overwritten on the next run. The command has a 60-second deadline and returns a
nonzero exit code on failure. See [errors and lifetimes](errors-and-lifetimes.md).

This example targets authenticated file access, not public shares, OCS sharing
APIs, calendar/contacts, chunked uploads or desktop synchronization.
Nextcloud's transactional storage locking is not proof of WebDAV LOCK support;
check your server before using the separate [locking example](locking.md).

See Nextcloud's [file API and properties](https://docs.nextcloud.com/server/latest/developer_manual/client_apis/WebDAV/basic.html)
and the [authentication guide](authentication.md) for other transport modes.
