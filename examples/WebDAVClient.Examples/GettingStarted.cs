namespace WebDAVClient.Examples;

public static class GettingStarted
{
    public static async Task RunAsync(DavClient client, DavPath remoteFile,
        string localFile, TextWriter output, CancellationToken token)
    {
        var listing = await client.PropFindAsync(DavPath.Root, cancellationToken: token);
        listing.EnsureSucceeded();
        foreach (var resource in listing.Resources)
        {
            output.WriteLine(resource.Hrefs[0]);
        }
        using var download = await client.DownloadAsync(remoteFile, cancellationToken: token);
        download.EnsureHttpSuccess();
        var content = download.Content ?? throw new IOException("No downloadable content.");
        await using var destination = new FileStream(localFile, FileMode.CreateNew, FileAccess.Write);
        await content.CopyToAsync(destination, token);
        output.WriteLine("Download complete.");
    }
}
