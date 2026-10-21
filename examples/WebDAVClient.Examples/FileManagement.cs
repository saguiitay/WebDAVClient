using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient.Examples;

public static class FileManagement
{
    public static async Task RunAsync(DavClient client, string collectionName,
        DavPath source, TextWriter output, CancellationToken token)
    {
        var copy = DavPath.FromSegments(collectionName, "copy.bin");
        var moved = DavPath.FromSegments(collectionName, "moved.bin");
        (await client.CopyAsync(source, copy, new CopyOptions { Overwrite = false }, token)).EnsureSucceeded();
        (await client.MoveAsync(copy, moved, new MoveOptions { Overwrite = false }, token)).EnsureSucceeded();
        var property = new PropertyName("https://example.com/ns", "example");
        (await client.UpdatePropertiesAsync(moved, new[]
        {
            DavPropertyChange.Set(DavXmlValue.Text(property, "created by the example")),
            DavPropertyChange.Remove(property)
        }, cancellationToken: token)).EnsureSucceeded();
        (await client.DeleteAsync(moved, cancellationToken: token)).EnsureSucceeded();
        output.WriteLine("Copied, moved, updated properties and deleted a run-owned file.");
    }
}
