using System.Text;
using WebDAVClient.Protocol;

namespace WebDAVClient.Examples;

public static class Locking
{
    public static async Task RunAsync(DavClient client, DavPath file,
        string token, TextWriter output, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("updated under a lock"));
        (await client.UploadAsync(file, content, new UploadOptions
        {
            Conditions = DavIfConditions.Untagged(new DavConditionList(DavCondition.StateToken(token)))
        }, cancellationToken)).EnsureSucceeded();
        (await client.RefreshLockAsync(file, token, cancellationToken: cancellationToken)).EnsureSucceeded();
        output.WriteLine("Conditional write and explicit lock refresh succeeded; unlock follows during cleanup.");
    }
}
