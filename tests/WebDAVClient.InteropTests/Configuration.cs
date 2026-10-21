using System.Net;

namespace WebDAVClient.InteropTests;

internal sealed record Configuration(Uri Endpoint, string Server, string Version, string Auth, string? User, string? Password,
    string Output, bool ApachePathPlusAlias = false)
{
    internal static Configuration Read(Func<string, string?> get)
    {
        if (get("WEBDAV_INTEROP_RUN") != "1")
        {
            throw new ArgumentException("Set WEBDAV_INTEROP_RUN=1 to authorize run-owned mutations.");
        }
        if (!Uri.TryCreate(get("WEBDAV_INTEROP_ENDPOINT"), UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length != 0
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
            || !endpoint.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("An HTTP(S) collection endpoint ending in / without credentials, query or fragment is required.");
        }
        if (!endpoint.IsLoopback && (get("WEBDAV_INTEROP_ALLOW_REMOTE") != "1" || endpoint.Scheme != "https"))
        {
            throw new ArgumentException("Remote servers require HTTPS and WEBDAV_INTEROP_ALLOW_REMOTE=1.");
        }
        string server = Label(get("WEBDAV_INTEROP_SERVER"));
        string version = Label(get("WEBDAV_INTEROP_VERSION"));
        string? plusAlias = get("WEBDAV_INTEROP_APACHE_PATH_PLUS_ALIAS");
        if (plusAlias is not (null or "0" or "1") || (plusAlias == "1" && server != "apache"))
        {
            throw new ArgumentException("Apache path-plus target aliasing requires an explicit 1 and the apache profile.");
        }
        string auth = get("WEBDAV_INTEROP_AUTH") ?? "anonymous";
        string? user = get("WEBDAV_INTEROP_USER");
        string? password = get("WEBDAV_INTEROP_PASSWORD");
        if (auth is not ("anonymous" or "basic" or "windows") ||
            (auth == "basic" && (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password) || user.Contains(':'))) ||
            (auth != "basic" && (user != null || password != null)))
        {
            throw new ArgumentException("Select anonymous, basic (explicit USER/PASSWORD), or windows (explicit default credentials opt-in).");
        }
        return new Configuration(endpoint, server, version, auth, user, password,
            get("WEBDAV_INTEROP_OUTPUT") ?? $"artifacts/interop/{server}-{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.Replace(" ", "")}.json",
            plusAlias == "1");
    }

    internal System.Net.Http.HttpClient CreateTransport()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            Credentials = Auth == "windows" ? CredentialCache.DefaultNetworkCredentials : null
        };
        var client = new System.Net.Http.HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        if (Auth == "basic")
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(User + ":" + Password)));
        }
        return client;
    }

    private static string Label(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 ||
            value.Any(c => !(char.IsAsciiLetterOrDigit(c) || ".-_+".Contains(c))))
        {
            throw new ArgumentException("SERVER and VERSION must be nonempty safe product/version labels (letters, digits, .-_+).");
        }
        return value;
    }
}
