using WebDAVClient.Transfers;

namespace WebDAVClient.Examples;

public static class ResumableDownloads
{
    public static async Task RunAsync(DavClient client, DavPath remoteFile,
        string localFile, TextWriter output, CancellationToken token)
    {
        await client.DownloadFileAsync(remoteFile, localFile, token);
        output.WriteLine("Download complete. The destination was published without overwriting an existing file.");
        output.WriteLine("The .davlock sentinel is retained; never delete it while a transfer may be running.");
    }
}
