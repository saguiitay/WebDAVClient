using WebDAVClient.Extensions;
using WebDAVClient.Protocol;

namespace WebDAVClient.Examples;

public static class CollectionSynchronization
{
    public static async Task RunAsync(DavClient client, string? previousToken,
        TextWriter output, CancellationToken token)
    {
        var batch = await client.SyncCollectionAsync(DavPath.Root, previousToken,
            new[] { DavProperties.ETag.Name }, limit: 100, cancellationToken: token);
        batch.Response.EnsureHttpSuccess();
        bool failed = false;
        foreach (var resource in batch.Response.Resources)
        {
            if (resource.StatusCode == 404)
            {
                output.WriteLine("Deletion: " + resource.Hrefs[0]);
            }
            else if (resource.StatusCode == 507 && batch.HasMore
                && resource.Hrefs.Any(href => Uri.TryCreate(client.Endpoint, href, out var uri) && uri == client.Endpoint))
            {
                output.WriteLine("Collection batch truncated; continuation requires explicit handling.");
            }
            else
            {
                output.WriteLine("Changed: " + resource.Hrefs[0]);
                failed |= resource.StatusCode is < 200 or >= 300;
                failed |= resource.PropertyStatuses.Any(group => group.StatusCode is < 200 or >= 300);
            }
        }
        if (failed)
        {
            throw new IOException("A sync member/property failed; do not advance a stored token.");
        }
        output.WriteLine($"Has more: {batch.HasMore}. Returned token present: {batch.SyncToken != null}.");
        output.WriteLine("No token was saved. Apply changes durably before advancing a checkpoint.");
    }
}
