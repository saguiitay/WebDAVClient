# ownCloud Server integration

Connect to an ownCloud Server file collection, inspect ownCloud metadata and
download an existing file. The `owncloud` command sends PROPFIND and GET only;
it never changes remote files.

**Example:** [CloudStorage.cs](../examples/WebDAVClient.Examples/CloudStorage.cs),
with [authentication and error handling](../examples/WebDAVClient.Examples/Program.cs).
This guide targets **ownCloud Server (PHP)**, not ownCloud Infinite Scale.
Compatibility with your server version and configuration still needs checking.

## Before you run

- Copy the HTTPS WebDAV URL provided by your deployment. The ownCloud Server
  documentation uses `https://cloud.example.com/owncloud/remote.php/webdav/`.
  Preserve the installation/proxy prefix and a trailing `/`; do not use the
  browser Files page URL.
- Use an app/device password where supported or required by your deployment.
  Create it in your personal security settings and use the generated username
  and password as credentials, **not as a bearer token**.
- Choose an existing file in the target collection and a new local output path.
  For a subfolder, use that folder's WebDAV URL as the endpoint.
- Install the .NET 10 SDK to build the checkout. Run commands from the repository
  root; use `net8.0` instead if you also have the .NET 8 runtime installed.

## Run the example

This PowerShell example prompts for your app/device password. Use the generated
username if it differs from the account's usual login name.

```pwsh
$credential = Get-Credential -UserName "alice" -Message "ownCloud app/device password"
$env:WEBDAV_ENDPOINT = "https://cloud.example.com/owncloud/remote.php/webdav/Documents/"
$env:WEBDAV_AUTH = "credentials"
$env:WEBDAV_USERNAME = $credential.UserName
$env:WEBDAV_PASSWORD = $credential.GetNetworkCredential().Password
$env:WEBDAV_REMOTE_FILE = "report + notes.txt"
$env:WEBDAV_OUTPUT = ".\owncloud-download.txt"
try {
    dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- owncloud --validate
    if ($LASTEXITCODE -ne 0) { throw "Configuration validation failed." }
    dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- owncloud
} finally {
    Remove-Item Env:\WEBDAV_PASSWORD
    $credential = $null
}
```

`--validate` checks endpoint/authentication configuration offline; it does not
check server access, the remote file or the local destination. Environment
variables are not a secret vault; use your deployment's secret manager for
deployed applications.

`WEBDAV_REMOTE_FILE` is one **unescaped filename**, not a URL or a slash-separated
path. `DavPath.FromSegments` handles URL encoding. To select a different folder,
change the endpoint rather than embedding a path in the filename.

## What you get

The command fully reads a depth-one listing of the collection and its immediate
children, printing hrefs, property statuses and available text values:

| Property | Meaning |
|---|---|
| `DAV:displayname`, `DAV:resourcetype`, `DAV:getetag` | Standard name, resource type and opaque ETag; structured values retain their status rather than being flattened into text |
| `{http://owncloud.org/ns}fileid` | Internal server-local file identifier, retained as text |
| `{http://owncloud.org/ns}id` | Server-supplied file identifier; retain it as opaque text, distinct from `fileid` |
| `{http://owncloud.org/ns}permissions` | Server-reported permission string, not an authorization guarantee |
| `{http://owncloud.org/ns}size` | Server-reported size; behavior may depend on the storage backend |

These are qualified property names, not a dependency on an `oc` XML prefix.
Do not assume identifiers are portable between servers or interpret a permission
string as proof that a future write will succeed.

An optional property reported as 404 remains unavailable. A resource failure or
other failed property status stops the workflow before GET. Once the listing is
fully read without such failures, the command downloads the configured file to
a **new** local file and prints `Download complete.` It does not follow a
server-returned href to choose the download.

## Troubleshooting and boundaries

| Symptom | Check |
|---|---|
| 401 | Generated app-password username/password, revocation and deployment authentication policy |
| 403 | Account permissions, external-storage access and proxy rules |
| 404 | Installation prefix, WebDAV endpoint, collection and exact filename |
| Redirect or HTML instead of DAV XML | Use the final HTTPS WebDAV URL; browser login redirects are not followed |
| TLS failure | Trust the approved CA instead of disabling validation |
| Existing or partial local output | Choose a new output path; no automatic overwrite, append or resume |

A failed download can leave a partial local file. The command has a 60-second
deadline and returns a nonzero exit code on failure.
See [errors and lifetimes](errors-and-lifetimes.md).

For **Infinite Scale**, obtain the Space-specific WebDAV URL and authentication
requirements from your administrator. Do not derive them from the ownCloud
Server URL above or assume the same optional properties. The command selects
property names; it does not discover a product, user home or Space.

Public-link shares, OCS sharing APIs, chunked uploads and durable synchronization
are outside this example. Use the [generic transfer guide](transfers.md) on
disposable storage if your application also needs writes.

Vendor references: [ownCloud Server WebDAV access](https://doc.owncloud.com/server/11.0/classic_ui/files/access_webdav.html),
[app passwords](https://doc.owncloud.com/server/11.0/classic_ui/session_management.html)
and [ownCloud's file-property definitions](https://github.com/owncloud/core/blob/master/apps/dav/lib/Connector/Sabre/FilesPlugin.php).
