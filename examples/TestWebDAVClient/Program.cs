using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebDAVClient;
using WebDAVClient.Authentication;
using WebDAVClient.Helpers;
using WebDAVClient.Protocol;

namespace TestWebDAVClient
{
    internal static class Program
    {
        internal static async Task<int> Main(string[] args)
        {
            if (args.Contains("--help") || (args.Length == 0 && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBDAV_SERVER"))))
            {
                Console.WriteLine("Usage: --server https://host --base-path /dav/ [--modern] [--target file] [--mutate] [--partial-put] [--validate]");
                Console.WriteLine("Defaults: read-only listing, anonymous authentication, 60-second operation deadline.");
                Console.WriteLine("Environment: WEBDAV_SERVER, WEBDAV_BASE_PATH, WEBDAV_USERNAME + WEBDAV_PASSWORD, or WEBDAV_BEARER_TOKEN.");
                Console.WriteLine("--mutate creates a unique run-owned folder and attempts bounded cleanup; never use on irreplaceable data.");
                Console.WriteLine("--modern uses DavClient; otherwise the retained Client API is used.");
                Console.WriteLine("--partial-put requires legacy --mutate and explicit server support for nonstandard partial PUT.");
                Console.WriteLine("--validate checks configuration offline. Ctrl+C cancels operations, not cleanup.");
                return 0;
            }
            string server = Environment.GetEnvironmentVariable("WEBDAV_SERVER");
            string basePath = Environment.GetEnvironmentVariable("WEBDAV_BASE_PATH") ?? "/";
            string target = null;
            bool mutate = false, validate = false, modern = false, partialPut = false;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--mutate":
                            mutate = true;
                            break;
                        case "--validate":
                            validate = true;
                            break;
                        case "--modern":
                            modern = true;
                            break;
                        case "--partial-put":
                            partialPut = true;
                            break;
                        case "--server":
                            server = Value(args, ref i);
                            break;
                        case "--base-path":
                            basePath = Value(args, ref i);
                            break;
                        case "--target":
                            target = Value(args, ref i);
                            break;
                        default:
                            throw new ArgumentException("Unknown argument. Use --help.");
                    }
                }
                if (!Uri.TryCreate(server, UriKind.Absolute, out var uri)
                    || (uri.Scheme != "https" && uri.Scheme != "http") || uri.AbsolutePath != "/"
                    || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                {
                    throw new ArgumentException("Supply --server as an HTTP(S) origin without credentials, query or path; use --base-path for the directory.");
                }

                if (string.IsNullOrWhiteSpace(basePath))
                {
                    throw new ArgumentException("--base-path must not be blank.");
                }
                if (partialPut && (!mutate || modern))
                {
                    throw new ArgumentException("--partial-put requires --mutate with the legacy API (omit --modern).");
                }
                string collectionPath = basePath.Trim('/');
                var endpoint = new Uri(uri, collectionPath.Length == 0 ? "/" : "/" + collectionPath + "/");
                if (modern && (endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0))
                {
                    throw new ArgumentException("--base-path must be a collection path without query or fragment.");
                }

                string user = Environment.GetEnvironmentVariable("WEBDAV_USERNAME");
                string password = Environment.GetEnvironmentVariable("WEBDAV_PASSWORD");
                string bearer = Environment.GetEnvironmentVariable("WEBDAV_BEARER_TOKEN");
                if ((!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(bearer))
                    || (string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(password)))
                {
                    throw new ArgumentException("Choose username/password or bearer authentication, not both; password requires a username.");
                }

                if (validate)
                {
                    Console.WriteLine("Configuration valid. No server contacted.");
                    return 0;
                }
                var transport = new HttpClientHandler
                {
                    UseDefaultCredentials = false,
                    AllowAutoRedirect = !modern,
                    UseCookies = !modern
                };
                if (!string.IsNullOrEmpty(user))
                {
                    transport.Credentials = new NetworkCredential(user, password);
                }

                HttpMessageHandler pipeline = transport;
                if (!string.IsNullOrEmpty(bearer))
                {
                    pipeline = new BearerTokenAuthenticationHandler(bearer)
                    {
                        InnerHandler = transport
                    };
                }

                using var http = new HttpClient(pipeline);
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                ConsoleCancelEventHandler cancel = (_, e) =>
                {
                    e.Cancel = true;
                    cancellation.Cancel();
                };
                Console.CancelKeyPress += cancel;
                try
                {
                    if (modern)
                    {
                        using var client = new DavClient(endpoint, http);
                        return await RunModernAsync(client, mutate, target, Console.Out, Console.Error, cancellation.Token).ConfigureAwait(false);
                    }
                    using var legacy = new Client(http)
                    {
                        Server = server,
                        BasePath = basePath
                    };
                    return await RunAsync(legacy, mutate, target, Console.Out, Console.Error, cancellation.Token, partialPut).ConfigureAwait(false);
                }
                finally
                {
                    Console.CancelKeyPress -= cancel;
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
        }

        private static string Value(string[] args, ref int index)
        {
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException(args[index] + " requires a value.");
            }

            return args[++index];
        }

        internal static async Task<int> RunAsync(IClient client, bool mutate, string target, TextWriter output, TextWriter error, CancellationToken token,
            bool partialPut = false)
        {
            string directory = null, lockToken = null;
            bool created = false;
            int exitCode = 0;
            try
            {
                var items = (await client.List(cancellationToken: token).ConfigureAwait(false)).ToList();
                if (items.Count == 0)
                {
                    output.WriteLine("No items found. An empty server is supported.");
                }

                foreach (var item in items)
                {
                    output.WriteLine($"{(item.IsCollection ? "Folder" : "File")}: {item.DisplayName}");
                }

                if (target != null)
                {
                    using var stream = await client.Download(target, token).ConfigureAwait(false);
                    await stream.CopyToAsync(Stream.Null, token).ConfigureAwait(false);
                    output.WriteLine("Requested target read successfully.");
                }
                if (mutate)
                {
                    var options = await client.GetServerOptions(cancellationToken: token).ConfigureAwait(false);
                    directory = "webdavclient-sample-" + Guid.NewGuid().ToString("N");
                    await client.CreateDir("/", directory, token).ConfigureAwait(false);
                    created = true;
                    output.WriteLine("Created run-owned directory: " + directory);
                    using (var input = new MemoryStream(Encoding.ASCII.GetBytes("abcdefgh")))
                    {
                        await client.Upload(directory, input, "sample.bin", cancellationToken: token).ConfigureAwait(false);
                    }

                    if (options.SupportsMethod("LOCK"))
                    {
                        var info = await client.LockFile(directory + "/sample.bin", cancellationToken: token).ConfigureAwait(false);
                        lockToken = info.Token;
                    }
                    byte[] expected = Encoding.ASCII.GetBytes("cde");
                    if (partialPut)
                    {
                        expected = new byte[]
                        {
                            0xF0, 0x0B, 0xA5
                        };
                        using var patch = new MemoryStream(expected);
                        await client.UploadPartial(directory, patch, "sample.bin", 2, 5, lockToken, token).ConfigureAwait(false);
                    }

                    using var part = await client.DownloadPartial(directory + "/sample.bin", 2, 4, token).ConfigureAwait(false);
                    using var result = new MemoryStream();
                    await part.CopyToAsync(result, token).ConfigureAwait(false);
                    if (!result.ToArray().SequenceEqual(expected))
                    {
                        throw new InvalidDataException("Partial transfer returned unexpected bytes.");
                    }

                    output.WriteLine("Run-owned transfer roundtrip completed.");
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                error.WriteLine("Operation cancelled. Attempting independent cleanup of confirmed run-owned resources.");
                exitCode = 130;
            }
            catch (Exception ex) when (ex is WebDAVException || ex is HttpRequestException || ex is IOException || ex is InvalidDataException || ex is OperationCanceledException)
            {
                error.WriteLine(ex is WebDAVException web && web.GetHttpCode() == 404
                    ? "Requested target was not found. Choose an existing --target or omit it."
                    : "Sample operation failed: " + ex.Message);
                exitCode = 1;
            }
            finally
            {
                if (lockToken != null && !await CleanupAsync(
                    ct => client.UnlockFile(directory + "/sample.bin", lockToken, ct), "Unlock", error).ConfigureAwait(false))
                {
                    exitCode = 1;
                }

                if (created && !await CleanupAsync(
                    ct => client.DeleteFolder(directory, cancellationToken: ct), "Delete run-owned directory " + directory, error).ConfigureAwait(false))
                {
                    exitCode = 1;
                }
            }
            return exitCode;
        }

        internal static async Task<int> RunModernAsync(DavClient client, bool mutate, string target, TextWriter output, TextWriter error,
            CancellationToken token)
        {
            DavPath directory = null;
            bool created = false;
            int exitCode = 0;
            try
            {
                var listing = await client.PropFindAsync(DavPath.Root, cancellationToken: token).ConfigureAwait(false);
                listing.EnsureSucceeded();
                foreach (var resource in listing.Resources)
                {
                    output.WriteLine("Resource: " + resource.Hrefs[0]);
                }
                if (target != null)
                {
                    using var download = await client.DownloadAsync(DavPath.Parse(target), cancellationToken: token).ConfigureAwait(false);
                    download.EnsureHttpSuccess();
                    await download.Content.CopyToAsync(Stream.Null, token).ConfigureAwait(false);
                    output.WriteLine("Requested target read successfully.");
                }
                if (mutate)
                {
                    string name = "webdavclient-sample-" + Guid.NewGuid().ToString("N");
                    directory = DavPath.Parse(name + "/");
                    (await client.CreateCollectionAsync(directory, cancellationToken: token).ConfigureAwait(false)).EnsureSucceeded();
                    created = true;
                    output.WriteLine("Created run-owned directory: " + name);
                    var file = DavPath.FromSegments(name, "sample.bin");
                    var expected = Encoding.ASCII.GetBytes("abcdefgh");
                    using (var input = new MemoryStream(expected))
                    {
                        (await client.UploadAsync(file, input, new UploadOptions
                        {
                            IfNoneMatch = "*"
                        }, token).ConfigureAwait(false)).EnsureSucceeded();
                    }
                    using var download = await client.DownloadAsync(file, cancellationToken: token).ConfigureAwait(false);
                    download.EnsureHttpSuccess();
                    using var result = new MemoryStream();
                    await download.Content.CopyToAsync(result, token).ConfigureAwait(false);
                    if (!result.ToArray().SequenceEqual(expected))
                    {
                        throw new InvalidDataException("Transfer returned unexpected bytes.");
                    }
                    output.WriteLine("Run-owned modern transfer roundtrip completed.");
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                error.WriteLine("Operation cancelled. Attempting independent cleanup of confirmed run-owned resources.");
                exitCode = 130;
            }
            catch (Exception ex) when (ex is WebDAVException || ex is HttpRequestException || ex is IOException || ex is InvalidDataException || ex is OperationCanceledException)
            {
                error.WriteLine("Sample operation failed: " + ex.Message);
                exitCode = 1;
            }
            finally
            {
                if (created && !await CleanupAsync(async ct =>
                    (await client.DeleteAsync(directory, cancellationToken: ct).ConfigureAwait(false)).EnsureSucceeded(),
                    "Delete run-owned directory " + directory.Reference, error).ConfigureAwait(false))
                {
                    exitCode = 1;
                }
            }
            return exitCode;
        }

        private static async Task<bool> CleanupAsync(Func<CancellationToken, Task> cleanup, string operation, TextWriter error)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await cleanup(deadline.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (ex is WebDAVException || ex is HttpRequestException || ex is IOException || ex is OperationCanceledException)
            {
                error.WriteLine(operation + " failed; manual cleanup may be required: " + ex.Message);
                return false;
            }
        }
    }
}
