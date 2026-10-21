using System.Net;
using WebDAVClient.Authentication;
using WebDAVClient.Helpers;

namespace WebDAVClient.Examples;

public static class Program
{
    private static readonly string[] s_commands =
    {
        "getting-started", "authentication", "listing", "transfers",
        "file-management", "locking", "collection-sync"
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help"))
        {
            Console.WriteLine("Usage: <command> [--validate] [--allow-writes]");
            Console.WriteLine("Commands: " + string.Join(", ", s_commands));
            Console.WriteLine("Set WEBDAV_ENDPOINT to a collection URL ending in '/'.");
            Console.WriteLine("WEBDAV_AUTH: anonymous (default), credentials, windows, bearer, provider.");
            Console.WriteLine("Credentials: WEBDAV_USERNAME + WEBDAV_PASSWORD; bearer/provider: WEBDAV_BEARER_TOKEN.");
            Console.WriteLine("getting-started: WEBDAV_REMOTE_FILE (one filename), WEBDAV_OUTPUT (new local file).");
            Console.WriteLine("transfers: WEBDAV_INPUT (existing local file), WEBDAV_OUTPUT (new local file).");
            Console.WriteLine("collection-sync: optional WEBDAV_SYNC_TOKEN; tokens are never persisted.");
            Console.WriteLine("Writes require --allow-writes and use a unique run-owned collection.");
            Console.WriteLine("--validate is offline. TLS validation stays enabled; redirects stay disabled.");
            return 0;
        }
        try
        {
            string command = args[0];
            if (!s_commands.Contains(command) || args.Skip(1).Any(arg => arg != "--validate" && arg != "--allow-writes"))
            {
                throw new ArgumentException("Unknown command or flag. Use --help.");
            }
            bool writes = command is "transfers" or "file-management" or "locking";
            if (writes && !args.Contains("--allow-writes"))
            {
                throw new ArgumentException("This example requires --allow-writes; use a disposable test account.");
            }
            var endpoint = new Uri(Required("WEBDAV_ENDPOINT"));
            using var http = CreateHttpClient(endpoint);
            using var client = new DavClient(endpoint, http);
            if (args.Contains("--validate"))
            {
                Console.WriteLine("Configuration valid. No server contacted.");
                return 0;
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var token = deadline.Token;
            switch (command)
            {
                case "getting-started":
                    await GettingStarted.RunAsync(client, DavPath.FromSegments(Required("WEBDAV_REMOTE_FILE")),
                        Environment.GetEnvironmentVariable("WEBDAV_OUTPUT") ?? "download.bin", Console.Out, token);
                    break;
                case "authentication":
                    await Authentication.RunAsync(client, Console.Out, token);
                    break;
                case "listing":
                    await ListingAndProperties.RunAsync(client, Console.Out, token);
                    break;
                case "collection-sync":
                    await CollectionSynchronization.RunAsync(client,
                        Environment.GetEnvironmentVariable("WEBDAV_SYNC_TOKEN"), Console.Out, token);
                    break;
                default:
                    string? input = command == "transfers" ? Required("WEBDAV_INPUT") : null;
                    using (var source = input == null ? null : File.OpenRead(input))
                    {
                        return await MutationExamples.RunAsync(client, command, source,
                            Environment.GetEnvironmentVariable("WEBDAV_OUTPUT") ?? "download.bin",
                            Console.Out, Console.Error, token);
                    }
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled or deadline expired.");
            return 130;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is UriFormatException || ex is InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex) when (ex is WebDAVException || ex is HttpRequestException
            || ex is IOException || ex is UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex is WebDAVException web
                ? $"WebDAV failed: HTTP {web.ResponseStatusCode ?? web.GetHttpCode()}, member {web.FailedStatusCode}."
                : "Transfer or transport failed: " + ex.Message);
            return 1;
        }
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new ArgumentException("Set " + name + ".");

    private static System.Net.Http.HttpClient CreateHttpClient(Uri endpoint)
    {
        string mode = Environment.GetEnvironmentVariable("WEBDAV_AUTH") ?? "anonymous";
        if (mode is not ("anonymous" or "credentials" or "windows" or "bearer" or "provider"))
        {
            throw new ArgumentException("Unknown WEBDAV_AUTH mode.");
        }
        if (mode != "anonymous" && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Use HTTPS for authentication.");
        }
        var credentials = mode == "credentials"
            ? new NetworkCredential(Required("WEBDAV_USERNAME"), Required("WEBDAV_PASSWORD")) : null;
        string? bearerToken = mode is "bearer" or "provider" ? Required("WEBDAV_BEARER_TOKEN") : null;
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseDefaultCredentials = mode == "windows",
            UseCookies = false
        };
        if (credentials != null)
        {
            handler.Credentials = credentials;
        }
        HttpMessageHandler pipeline = handler;
        if (mode is "bearer" or "provider")
        {
            var bearer = mode == "bearer"
                ? new BearerTokenAuthenticationHandler(bearerToken!)
                : new BearerTokenAuthenticationHandler(ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(Required("WEBDAV_BEARER_TOKEN"));
                });
            bearer.InnerHandler = handler;
            pipeline = bearer;
        }
        return new System.Net.Http.HttpClient(pipeline);
    }
}
