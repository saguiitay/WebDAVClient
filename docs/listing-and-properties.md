# Listings and typed/custom properties

**Example:** [ListingAndProperties.cs](../examples/WebDAVClient.Examples/ListingAndProperties.cs).

```pwsh
dotnet run --project examples\WebDAVClient.Examples -f net10.0 -- listing
```

Configure the [endpoint and authentication](authentication.md) first. The command
is read-only. It requests display name, resource type, ETag and an example custom
property, prints qualified names, statuses and available text values, and consumes
a streaming listing to completion. Structured XML values are not flattened.

## Select the right query

| `PropFindOptions` | Meaning |
|---|---|
| Default / `Selection = All` | `allprop`; does not promise every live property |
| `Selection = Selected`, `Properties = ...` | Request exact qualified property names |
| `Selection = Names` | `propname`; discovers names, not values |
| `Selection = All`, nonempty `Properties` | `allprop` with `include` |
| `Depth = Zero`, `One`, `Infinity` | Resource only, immediate children, or descendants; the server can reject infinity |

`PropFindAsync` buffers results. `OpenListingAsync` owns a single-use streamed
response. The addressed collection is included, unlike legacy `List`.

## Read values without inventing defaults

`DavProperties.DisplayName.Read(resource)` and other codecs return **all**
occurrences. Check `HasValue`, `Source.StatusCode` and `DecodingError`. No entry
means missing, not an empty/default value. A missing `resourcetype` is unknown,
not proof that a resource is a file. A successful empty resource type is distinct
from a collection containing the qualified `DAV:collection` type.

Custom properties use `WebDAVClient.Model.PropertyName(namespace, localName)`.
`resource.GetProperties(name)` retains exact outcomes. `DavXmlValue.ToElement()`
returns a defensive XML copy; `TryGetText` does not flatten child elements.
Names, statuses and XML remain authoritative even when typed decoding fails.

The example treats 404 on the optional requested properties as unavailable and
prints it; other failed statuses fail the command. This is an explicit example
policy, not a library-wide suppression. For strict core-result handling call
`EnsureSucceeded()` on a buffered result, which checks reported property failures.

## Complete does not mean successful or atomic

Enumerate `listing.ReadResourcesAsync(token)` fully and then check `IsComplete`.
Early break, cancellation, disposal or malformed trailing XML leave it false.
Even a complete document can contain denied members or represent a non-atomic
server view. Never infer deletions from an incomplete or denied listing.

The default XML budgets are 16 Mi characters total, 1 Mi per retained subtree and
depth 64. A buffered document counts as a retained subtree. Prefer streaming for
large listings; only increase `XmlLimits` deliberately. DTDs and external
resolution are prohibited. The metadata deadline includes enumeration time.

To set/remove properties, see the [file-management example](file-management.md).
Detailed XML/status and limit contracts remain in the [reference](reference.md#v3-migration-and-contracts).
