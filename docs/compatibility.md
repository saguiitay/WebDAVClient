# Server compatibility

**Scope:** the released v3 client and the code on `master`. This is a server
assessment checklist, **not a list of certified compatible products**.
As of this documentation update, this branch has no release-scoped,
independent-server test matrix establishing compatibility with the products
below. Mocked and loopback HTTP tests validate client contracts, not a vendor's
server, authentication stack or deployment.

The separate [protocol support table](protocol-support.md) describes implemented
client features. A server's documentation or DAV advertisement does not prove
that a feature works for your account, resource, version or proxy configuration.

## Products and deployment considerations

| Server/product | Vendor-documented setup / endpoint hint | Verification for this release | What to check |
|---|---|---|---|
| [Nextcloud](https://docs.nextcloud.com/server/latest/developer_manual/client_apis/WebDAV/basic.html) | Common authenticated file endpoint: `/remote.php/dav/files/{user}/` | Not verified | App password via Basic authentication where required; reverse-proxy prefix, public-share endpoints and headers differ |
| [ownCloud](https://doc.owncloud.com/server/11.0/developer_manual/webdav_api/index.html) | Use the WebDAV URL published by your ownCloud deployment | Not verified | Product/version-specific endpoint and authentication; do not infer compatibility from Nextcloud lineage |
| [Apache HTTP Server `mod_dav` / `mod_dav_fs`](https://httpd.apache.org/docs/2.4/mod/mod_dav.html) | DAV-enabled location, filesystem provider and lock database | Not verified | Filesystem/authz permissions, lock database, property persistence and URI encoding |
| [Microsoft IIS WebDAV](https://learn.microsoft.com/en-us/iis/install/installing-publishing-technologies/installing-and-configuring-webdav-on-iis) | Enable publishing and configure authentication/authoring rules | Not verified | Request filtering, verb permissions, Windows/Basic authentication and TLS |
| [nginx built-in `ngx_http_dav_module`](https://nginx.org/en/docs/http/ngx_http_dav_module.html) | Documents PUT, DELETE, MKCOL, COPY and MOVE, not a complete DAV server | Not verified; built-in module alone lacks required listing methods | PROPFIND/LOCK need an additional implementation; identify the actual modules instead of assuming full WebDAV |
| [sabre/dav](https://sabre.io/dav/) | A configurable PHP framework; actual capabilities depend on plugins and application | Not verified | Enabled plugins, authentication backend, properties, locks and optional sync REPORT profile |
| [rclone `serve webdav`](https://rclone.org/commands/rclone_serve_webdav/) | Serves an rclone remote through a configured WebDAV listener | Not verified | Backend-specific ETags, permissions, ranges, filesystem behavior and authentication |
| NAS/vendor WebDAV packages (for example Synology) | Use the vendor-provided HTTPS WebDAV service URL, not the browser file-manager URL | Not verified | Package/version, enabled port, TLS chain, permissions and advertised methods |

Endpoint examples are hints, not defaults that the library discovers for you.
For a product not listed, use its actual WebDAV URL and the same checklist.
Historical references to 4shared or SVN in old release notes are not current
certification.

## Verify a deployment

1. Record the package version, server product/version, endpoint profile,
   authentication mode, TLS/proxy setup and required operations.
2. Use the [authentication example](authentication.md) to check OPTIONS; absence
   of an advertisement remains unknown, not definitive lack of support.
3. Run [listing](listing-and-properties.md) and a
   [known-file download](getting-started.md) with the target account.
4. On disposable storage, separately test [transfers](transfers.md),
   [namespace/property operations](file-management.md), [locks](locking.md) and
   [sync](collection-sync.md) only if your application depends on them.
5. Include spaces, Unicode, literal plus signs, percent escapes, redirects,
   missing/denied properties, conditional-write conflicts and partial failures.

Retain operation-specific, versioned results and exact configuration without
publishing credentials, bodies or sensitive paths. A passed GET does not imply
LOCK, PROPPATCH, sync REPORT or every authentication scheme passed.
Never point mutation probes at production data.

Proposed interoperability work on other branches/PRs does not automatically
establish compatibility for a released package. Update this table only when a
reproducible run identifies the tested package/commit, server and profile, and
states both passing operations and limitations.
