using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient.Examples;

public static class ListingAndProperties
{
    public static async Task RunAsync(DavClient client, TextWriter output, CancellationToken token)
    {
        using var listing = await client.OpenListingAsync(DavPath.Root, new PropFindOptions
        {
            Depth = DavDepth.One,
            Selection = DavPropertySelection.Selected,
            Properties = new[]
            {
                DavProperties.DisplayName.Name, DavProperties.ResourceTypes.Name,
                DavProperties.ETag.Name, new PropertyName("https://example.com/ns", "author")
            }
        }, token);
        listing.EnsureHttpSuccess();
        bool failed = false;
        await foreach (var resource in listing.ReadResourcesAsync(token))
        {
            output.WriteLine(resource.Hrefs[0]);
            if (resource.StatusCode is < 200 or >= 300)
            {
                failed = true;
                output.WriteLine("Resource failure: " + resource.StatusCode);
            }
            foreach (var name in DavProperties.DisplayName.Read(resource))
            {
                failed |= name.DecodingError != null;
                output.WriteLine(name.HasValue ? "Name: " + name.Value
                    : $"Name unavailable: status {name.Source.StatusCode}, decode error {name.DecodingError?.Message}");
            }
            foreach (var group in resource.PropertyStatuses)
            {
                foreach (var property in group.Properties)
                {
                    output.WriteLine($"{property.Name.Namespace}{property.Name.LocalName}: HTTP {property.StatusCode}");
                    if (property.StatusCode != 404 && (property.StatusCode is < 200 or >= 300))
                    {
                        failed = true;
                    }
                }
            }
        }
        if (!listing.IsComplete)
        {
            throw new IOException("Listing did not complete; do not infer removals.");
        }
        if (failed)
        {
            throw new IOException("One or more resources/properties failed; see reported statuses.");
        }
        output.WriteLine("Listing document fully read. Missing optional properties remain unknown.");
    }
}
