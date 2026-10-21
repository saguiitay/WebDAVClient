namespace WebDAVClient.Examples;

public static class Authentication
{
    public static async Task RunAsync(DavClient client, TextWriter output, CancellationToken token)
    {
        var capabilities = await client.GetCapabilitiesAsync(DavPath.Root, cancellationToken: token);
        capabilities.EnsureHttpSuccess();
        output.WriteLine("OPTIONS request succeeded with the selected authentication mode.");
        output.WriteLine("DAV advertisements: " + string.Join(", ", capabilities.DavTokens));
        output.WriteLine("Allowed methods: " + string.Join(", ", capabilities.AllowedMethods));
        output.WriteLine("Advertisements are evidence for this resource, not conformance certification.");
    }
}
