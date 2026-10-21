# Licensing notices and provenance

## Current distribution

WebDAVClient is distributed under the [MIT License](LICENSE.md). MIT is the single
license offered for the current package, not a choice between MIT and CC0 or
Apache 2.0. The project owner has confirmed ownership of the project and
authorized distribution under MIT. Commercial use, modification, and redistribution
are permitted subject to the license's notice and other terms.

## Historical upstream attribution

The inherited client implementation originated in
[`kvdb/WebDAVClient`](https://github.com/kvdb/WebDAVClient). Its original source
header contains the following attribution and Creative Commons Zero link:

```text
(C) 2010 Kees van den Broek: kvdb@kvdb.net
         D-centralize: d-centralize.nl

Latest version and examples on: http://kvdb.net/projects/webdav

Feel free to use this code however you like.
http://creativecommons.org/license/zero/
```

That notice is retained as historical provenance, not as a separate CC0 license
requirement for the current package. The current distribution, including the
adapted inherited implementation, is offered under MIT; this does not revoke
the upstream author's original permissions. The historical URL above is
reproduced as written. The original README describes the adaptations as
task-based async support and strongly typed responses.

The header can be verified in the
[upstream source at `aab7b278`](https://github.com/kvdb/WebDAVClient/blob/aab7b278185d2a8b7d3bda1a4485e59101c94f0c/WebDAVClient/WebDAVClient.cs)
and this project's
[initial client at `95b83d7f`](https://github.com/saguiitay/WebDAVClient/blob/95b83d7fe652d90cd5857a0f8f17c9e61e283343/WebDAVClient/Client.cs).
It was removed from `Client.cs` in
[`1b61e80b`](https://github.com/saguiitay/WebDAVClient/commit/1b61e80b9a5cf0f03e77c084a1f6cfca5aba6661);
refactoring does not by itself remove inherited provenance.

## Historical license record

Earlier package metadata linked to Apache 2.0 in
[`c0c0541f` (2014)](https://github.com/saguiitay/WebDAVClient/blob/c0c0541f4f39fff9e3afde26270dde804ad2f476/NuGet.Package/Package.nuspec)
and explicitly declared MIT in
[`7338050a` (2020)](https://github.com/saguiitay/WebDAVClient/commit/7338050abdd68289f79a6659b67d097becc4e8d7).
In [`2796f14e` (2023)](https://github.com/saguiitay/WebDAVClient/commit/2796f14e54df7e0a66f626faec5b2fc2e2b2ab1b),
a combined MIT/Apache file was added without a component-level scope explanation.

That file, including its `Copyright (c) 2021 Microsoft` notice and Apache 2.0
text, is preserved unchanged in
[Git history](https://github.com/saguiitay/WebDAVClient/blob/f686e530f2bfea3d908686cc722e0048497d733e/licenses/HISTORICAL-LICENSE.md).
The project owner has confirmed that the project is unrelated to Microsoft;
that historical notice is not a current attribution of project ownership.
The archived combined text does not designate the current distribution as a
whole-package MIT/Apache dual-license choice.

The 3.0.1 package declared `MIT` in its NuGet license metadata and included
`LICENSE.md`, this notice, and the historical record. Starting with 3.1.1, packages
include the current `LICENSE.md` and this provenance notice; the superseded
combined text is retained in Git history instead. This cleanup does not
change previously published packages or withdraw permissions already granted
with earlier distributions. Preserve the
supplied license and attribution notices when redistributing.
