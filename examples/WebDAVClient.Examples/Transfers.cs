using System.Net.Http.Headers;

namespace WebDAVClient.Examples;

public static class Transfers
{
    public static async Task RunAsync(DavClient client, DavPath file,
        string localFile, TextWriter output, CancellationToken token)
    {
        using var download = await client.DownloadAsync(file, cancellationToken: token);
        download.EnsureHttpSuccess();
        var content = download.Content ?? throw new IOException("No downloadable content.");
        await using (var destination = new FileStream(localFile, FileMode.CreateNew, FileAccess.Write))
        {
            await content.CopyToAsync(destination, token);
        }
        using var range = await client.DownloadAsync(file, new DownloadOptions
        {
            Range = new RangeHeaderValue(0, 15)
        }, token);
        range.EnsureHttpSuccess();
        var rangeContent = range.Content ?? throw new IOException("No range content.");
        using var prefix = new MemoryStream();
        await rangeContent.CopyToAsync(prefix, token);
        output.WriteLine($"Downloaded to a new local file; prefix length: {prefix.Length}.");
        output.WriteLine("The range was not appended to an existing file; no resumability is implied.");
    }
}
