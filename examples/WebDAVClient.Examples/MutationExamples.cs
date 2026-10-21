using System.Text;
using WebDAVClient.Helpers;
using WebDAVClient.Protocol;

namespace WebDAVClient.Examples;

public static class MutationExamples
{
    public static async Task<int> RunAsync(DavClient client, string command, Stream? source,
        string localFile, TextWriter output, TextWriter error, CancellationToken token)
    {
        string name = "webdavclient-example-" + Guid.NewGuid().ToString("N");
        var collection = DavPath.Parse(name + "/");
        var file = DavPath.FromSegments(name, "sample.bin");
        bool created = false;
        string? lockToken = null;
        int exitCode = 0;
        try
        {
            output.WriteLine("Run-owned collection: " + client.Resolve(collection));
            (await client.CreateCollectionAsync(collection, cancellationToken: token)).EnsureSucceeded();
            created = true;
            using var memory = new MemoryStream(Encoding.UTF8.GetBytes("WebDAVClient example content"));
            (await client.UploadAsync(file, source ?? memory,
                new UploadOptions { IfNoneMatch = "*" }, token)).EnsureSucceeded();
            switch (command)
            {
                case "transfers":
                    await Transfers.RunAsync(client, file, localFile, output, token);
                    break;
                case "file-management":
                    await FileManagement.RunAsync(client, name, file, output, token);
                    break;
                case "locking":
                    var acquired = await client.AcquireLockAsync(file, cancellationToken: token);
                    acquired.EnsureSucceeded();
                    lockToken = acquired.LockToken?.Trim('<', '>')
                        ?? throw new IOException("LOCK returned no token header; reconcile the run-owned resource.");
                    await Locking.RunAsync(client, file, lockToken, output, token);
                    break;
                default:
                    throw new ArgumentException("Unknown mutation example.");
            }
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("Example cancelled or timed out; attempting bounded cleanup.");
            exitCode = 130;
        }
        catch (Exception ex) when (ex is WebDAVException || ex is HttpRequestException
            || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
        {
            error.WriteLine("Example failed: " + ex.Message);
            exitCode = 1;
        }
        finally
        {
            if (lockToken != null && !await CleanupAsync(async ct =>
                (await client.UnlockAsync(file, lockToken, ct)).EnsureSucceeded(), "Unlock", error))
            {
                exitCode = 1;
            }
            if (created && !await CleanupAsync(async ct =>
                (await client.DeleteAsync(collection, cancellationToken: ct)).EnsureSucceeded(),
                "Delete " + client.Resolve(collection), error))
            {
                exitCode = 1;
            }
        }
        return exitCode;
    }

    private static async Task<bool> CleanupAsync(Func<CancellationToken, Task> action,
        string description, TextWriter error)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await action(deadline.Token);
            return true;
        }
        catch (Exception ex) when (ex is WebDAVException || ex is HttpRequestException
            || ex is IOException || ex is OperationCanceledException)
        {
            error.WriteLine(description + " failed; manual reconciliation/cleanup required: " + ex.Message);
            return false;
        }
    }
}
