# Server compatibility

Use this guide to find your server's WebDAV endpoint, choose authentication and
check the operations your application needs. Available features depend on the
server version, enabled modules, account permissions and proxy configuration.

**Compatibility status:** the servers below have not been verified with this
package. The setup notes are based on vendor documentation, not a compatibility
guarantee. Check your deployment before using it with production data.

For version-specific positive and negative observations, see the
[independent-server evidence and test guide](../interoperability/README.md).
Those records identify historical source revisions and configurations; they
are not verification of every published package or deployment. The suite
mutates disposable storage and requires explicit opt-in.

The [prioritized gaps](../interoperability/GAPS.md) separate confirmed server
restrictions from missing evidence. In particular, the tested nginx built-in
DAV profile ignores `If-None-Match: *` on PUT. Do not use that configuration for
safe conditional creation or assume a preliminary HEAD closes the write race.

The [protocol support table](protocol-support.md) lists the client's supported
operations and extensions. Your server must also support the features you use.

For complete connection examples, see the [Nextcloud](nextcloud.md) and
[ownCloud Server](owncloud.md) guides.

## Products and deployment considerations

| Server/product | Endpoint and setup | Authentication and feature considerations |
|---|---|---|
| [Nextcloud](https://docs.nextcloud.com/server/latest/developer_manual/client_apis/WebDAV/basic.html) | Common authenticated file endpoint: `/remote.php/dav/files/{user}/` | Use an app password via Basic authentication where required. Reverse-proxy prefixes and public-share endpoints may differ. |
| [ownCloud](https://doc.owncloud.com/server/11.0/developer_manual/webdav_api/index.html) | Use the WebDAV URL published by your ownCloud deployment | Check the endpoint and authentication for your product/version; Nextcloud settings may not apply. |
| [Apache HTTP Server `mod_dav` / `mod_dav_fs`](https://httpd.apache.org/docs/2.4/mod/mod_dav.html) | Use the configured DAV-enabled location | Access depends on filesystem and authorization permissions. Locking also requires a configured lock database. Check property persistence and URI encoding if your application depends on them. |
| [Microsoft IIS WebDAV](https://learn.microsoft.com/en-us/iis/install/installing-publishing-technologies/installing-and-configuring-webdav-on-iis) | WebDAV publishing and authoring rules must be enabled | Check Windows/Basic authentication, TLS, request filtering and permissions for each HTTP verb you need. |
| [nginx built-in `ngx_http_dav_module`](https://nginx.org/en/docs/http/ngx_http_dav_module.html) | The built-in module supports PUT, DELETE, MKCOL, COPY and MOVE | The built-in module alone cannot provide directory listing (PROPFIND) or locking (LOCK); these require an additional implementation. |
| [sabre/dav](https://sabre.io/dav/) | Use the endpoint provided by the application built on sabre/dav | Authentication, properties, locks and collection sync depend on the application's enabled plugins. |
| [rclone `serve webdav`](https://rclone.org/commands/rclone_serve_webdav/) | Use the configured WebDAV listener URL | Authentication is configured on the listener. ETags, permissions, ranges and filesystem behavior depend on the storage backend. |
| NAS/vendor WebDAV packages (for example Synology) | Use the vendor-provided HTTPS WebDAV service URL, not the browser file-manager URL | Check the WebDAV package/version, enabled port, TLS certificate chain, permissions and supported methods. |

Endpoint examples are hints, not defaults that the library discovers for you.
For a product not listed, use its actual WebDAV URL and the same checklist.

## Check your connection

1. Configure your WebDAV endpoint and credentials using the
   [authentication guide](authentication.md).
2. Use the authentication example to inspect the server's OPTIONS response.
   Missing capability headers do not necessarily mean a feature is unsupported.
3. Run [listing](listing-and-properties.md) and a
   [known-file download](getting-started.md) with the target account.
4. If your application needs to write files or use optional features, use
   disposable storage to try [transfers](transfers.md),
   [namespace/property operations](file-management.md), [locks](locking.md) and
   [sync](collection-sync.md) only if your application depends on them.
5. Check the filenames and failure cases your application needs, such as spaces,
   Unicode, literal plus signs, percent escapes, redirects, denied properties,
   conditional-write conflicts and partial failures.

A successful download does not establish support for locks, property updates,
collection sync or a different authentication method. Never run write examples
against production data.

For failed requests, see [errors and lifetimes](errors-and-lifetimes.md). When
reporting a problem, include your package version, server product/version,
authentication method and failing operation. Remove credentials, tokens and
private paths from any logs you share.
