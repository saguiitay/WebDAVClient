# Package plan: WebDAVClient.Authentication.AzureIdentity

Status: proposed optional NuGet package.

Related: [package index](../SUBPACKAGE-AND-DESKTOP-PLAN.md) and P-08 in the [protocol plan](../PROTOCOL-COMPLETENESS-PLAN.md).

## Purpose and boundaries

Adapt `Azure.Core.TokenCredential` to the existing asynchronous bearer-token provider. Keep Azure authentication dependencies outside `WebDAVClient`; callers choose credential implementations such as those supplied by Azure.Identity.

Use the real Azure.Core type rather than the historical roadmap's illustrative credential namespace. An Azure credential does not automatically authorize every WebDAV endpoint.

## Dependencies and delivery

Depends on existing bearer integration and the P-08 configuration/lifetime contract.

1. Require explicit scopes/audience and endpoint/account association.
2. Request tokens asynchronously with cancellation and expiry-aware reuse.
3. Define concurrent refresh behavior without substituting anonymous access on failure.
4. Document interactive/native versus hosted credential selection and safe diagnostics.

## Acceptance

- Fake credentials verify scopes, cancellation, expiry and concurrent acquisition.
- Credential failures surface explicitly; tokens never appear in logs or launch URLs.
- Redirect/origin policy prevents forwarding credentials to an unrelated server.
- Core client consumers acquire neither Azure.Core nor Azure.Identity transitively.

## Sources

[Azure.Core.TokenCredential](https://learn.microsoft.com/en-us/dotnet/api/azure.core.tokencredential) establishes the supported adapter boundary.
