using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using WebDAVClient.Extensions;
using WebDAVClient.Helpers;
using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient.InteropTests;

internal sealed class CompatibilitySuite(Configuration config, Func<System.Net.Http.HttpClient>? transportFactory = null)
{
    private static readonly PropertyName s_marker = new("urn:webdavclient:interop:v1", "marker");

    private static readonly PropertyName s_missing = new("urn:webdavclient:interop:v1", "absent");

    private static readonly byte[] s_content = Encoding.UTF8.GetBytes("Independent WebDAV roundtrip: café 中文 + % #\n0123456789");

    private readonly Matrix m_matrix = new()
    {
        Server = config.Server,
        ServerVersion = config.Version,
        Authentication = config.Auth,
        Transport = config.Endpoint.Scheme,
        Loopback = config.Endpoint.IsLoopback,
        SourceRevision = ReadRevision()
    };

    internal async Task<Matrix> RunAsync()
    {
        using var http = transportFactory?.Invoke() ?? config.CreateTransport();
        using var client = new DavClient(config.Endpoint, http, new DavClientOptions
        {
            MetadataTimeout = TimeSpan.FromSeconds(15)
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var ct = deadline.Token;
        var tree = DavPath.Parse(m_matrix.RunTree + "/");
        var file = DavPath.FromSegments(m_matrix.RunTree, "café 中文 + % #.bin");
        bool created = false;
        string? lockToken = null;
        try
        {
            await CheckAsync("options", async () =>
            {
                var capabilities = await client.GetCapabilitiesAsync(DavPath.Root, cancellationToken: ct).ConfigureAwait(false);
                m_matrix.AdvertisedDav.AddRange(capabilities.DavTokens);
                m_matrix.AdvertisedMethods.AddRange(capabilities.AllowedMethods);
                return Response("options", capabilities, optional: true);
            }).ConfigureAwait(false);
            bool absent = await CheckAsync("isolation-preflight", async () =>
                Expected("isolation-preflight", await client.HeadAsync(tree, cancellationToken: ct).ConfigureAwait(false), 404)).ConfigureAwait(false);
            if (!absent)
            {
                Block("create-tree", "Run tree absence not confirmed; no mutations attempted.");
                return m_matrix;
            }
            created = await CheckAsync("create-tree", async () =>
                Response("create-tree", await client.CreateCollectionAsync(tree, cancellationToken: ct).ConfigureAwait(false))).ConfigureAwait(false);
            if (!created)
            {
                Block("roundtrip", "MKCOL not confirmed; no upload or cleanup of unconfirmed ownership.");
                return m_matrix;
            }
            bool uploaded = await CheckAsync("unicode-put", async () =>
            {
                using var input = new MemoryStream(s_content);
                return Response("unicode-put", await client.UploadAsync(file, input,
                    new UploadOptions { IfNoneMatch = "*" }, ct).ConfigureAwait(false));
            }).ConfigureAwait(false);
            if (!uploaded)
            {
                Block("roundtrip", "Upload did not confirm success.");
                return m_matrix;
            }
            if (config.Auth != "anonymous")
            {
                await CheckAsync("authentication-boundary", async () =>
                {
                    using var anonymousHttp = (config with { Auth = "anonymous", User = null, Password = null }).CreateTransport();
                    using var anonymousClient = new DavClient(config.Endpoint, anonymousHttp);
                    var response = await anonymousClient.HeadAsync(file, cancellationToken: ct).ConfigureAwait(false);
                    var result = Expected("authentication-boundary", response, 401);
                    string challenge = response.GetHeader("WWW-Authenticate") ?? "";
                    bool valid = config.Auth == "basic"
                        ? challenge.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
                        : challenge.StartsWith("Negotiate", StringComparison.OrdinalIgnoreCase) || challenge.StartsWith("NTLM", StringComparison.OrdinalIgnoreCase);
                    return result with
                    {
                        Outcome = result.Outcome == "passed" && valid ? "passed" : "failed",
                        Detail = "Unauthenticated HEAD must be 401 with the configured authentication scheme; no bad-password attempts."
                    };
                }).ConfigureAwait(false);
            }
            await CheckAsync("unicode-get", async () => await VerifyDownloadAsync(client, file, null, s_content, ct).ConfigureAwait(false)).ConfigureAwait(false);
            await CheckAsync("head-metadata", async () =>
            {
                var response = await client.HeadAsync(file, cancellationToken: ct).ConfigureAwait(false);
                var result = Response("head-metadata", response);
                return result.Outcome == "passed" && response.GetHeader("Content-Length") != s_content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ? result with { Outcome = "failed", Detail = "HEAD content length differs from uploaded bytes." } : result;
            }).ConfigureAwait(false);
            await CheckAsync("range-get", async () => await VerifyDownloadAsync(client, file,
                new DownloadOptions { Range = new RangeHeaderValue(2, 7) }, s_content[2..8], ct).ConfigureAwait(false)).ConfigureAwait(false);
            await CheckAsync("conditional-put", async () =>
            {
                using var input = new MemoryStream(s_content);
                return Expected("conditional-put", await client.UploadAsync(file, input,
                    new UploadOptions { IfNoneMatch = "*" }, ct).ConfigureAwait(false), 412);
            }).ConfigureAwait(false);
            await CheckAsync("conditional-integrity", async () => await VerifyDownloadAsync(client, file, null, s_content, ct).ConfigureAwait(false)).ConfigureAwait(false);
            await CheckAsync("propfind-statuses", async () =>
            {
                var response = await client.PropFindAsync(file, new PropFindOptions
                {
                    Depth = DavDepth.Zero,
                    Selection = DavPropertySelection.Selected,
                    Properties = [DavProperties.ContentLength.Name, s_missing]
                }, ct).ConfigureAwait(false);
                var result = Snapshot("propfind-statuses", response, optional: true);
                if (response.StatusCode != 207)
                {
                    return result;
                }
                bool valid = response.Resources.Count == 1 &&
                    response.Resources[0].GetProperties(s_missing).SingleOrDefault()?.StatusCode == 404 &&
                    DavProperties.ContentLength.Read(response.Resources[0]).SingleOrDefault() is { HasValue: true } length &&
                    length.Value == s_content.Length;
                return result with { Outcome = valid ? "passed" : "failed", Detail = "Requested missing property must be 404 beside valid content length." };
            }).ConfigureAwait(false);
            await CheckAsync("streamed-listing", async () =>
            {
                using var listing = await client.OpenListingAsync(tree, cancellationToken: ct).ConfigureAwait(false);
                if (listing.StatusCode != 207)
                {
                    return Response("streamed-listing", listing, optional: true);
                }
                int count = 0;
                bool found = false;
                var members = new List<int>();
                var properties = new List<int>();
                await foreach (var item in listing.ReadResourcesAsync(ct).ConfigureAwait(false))
                {
                    count++;
                    found |= item.Hrefs.Any(h => Uri.UnescapeDataString(h).EndsWith("café 中文 + % #.bin", StringComparison.Ordinal));
                    if (item.StatusCode.HasValue)
                    {
                        members.Add(item.StatusCode.Value);
                    }
                    properties.AddRange(item.PropertyStatuses.Where(p => p.StatusCode.HasValue).Select(p => p.StatusCode!.Value));
                }
                bool valid = listing.IsComplete && count >= 2 && found &&
                    members.Concat(properties).All(s => s is >= 200 and < 300);
                return new Observation("streamed-listing", valid ? "passed" : "failed",
                    listing.StatusCode, members.ToArray(), properties.ToArray(),
                    Detail: "Full enumeration, document completion, member/property success, and Unicode child href checked.");
            }).ConfigureAwait(false);
            await CheckAsync("dead-property", async () =>
            {
                string stage = "proppatch";
                try
                {
                    var response = await client.UpdatePropertiesAsync(file,
                        [DavPropertyChange.Set(new DavXmlValue(new XElement(XName.Get(s_marker.LocalName, s_marker.Namespace), "interop-v1")))],
                        cancellationToken: ct).ConfigureAwait(false);
                    var result = Response("dead-property", response, optional: true);
                    if (result.Outcome != "passed")
                    {
                        return result;
                    }
                    stage = "propfind-readback";
                    var read = await client.PropFindAsync(file, new PropFindOptions
                    {
                        Depth = DavDepth.Zero,
                        Selection = DavPropertySelection.Selected,
                        Properties = [s_marker]
                    }, ct).ConfigureAwait(false);
                    read.EnsureSucceeded();
                    bool valid = read.Resources.Count == 1 && read.Resources[0].GetProperties(s_marker).SingleOrDefault()?.Value.ToElement().Value == "interop-v1";
                    return result with { Outcome = valid ? "passed" : "failed", Detail = "Set custom property and read exact value back." };
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return Failure("dead-property", ex) with { Detail = stage + ":" + DescribeFailure(ex) };
                }
            }).ConfigureAwait(false);
            await CheckAsync("lock-unlock", async () =>
            {
                var response = await client.AcquireLockAsync(file, cancellationToken: ct).ConfigureAwait(false);
                var result = Response("lock-unlock", response, optional: true);
                if (result.Outcome != "passed")
                {
                    return result;
                }
                lockToken = response.LockToken?.Trim('<', '>');
                Require(!string.IsNullOrWhiteSpace(lockToken));
                var blocked = await client.DeleteAsync(file, cancellationToken: ct).ConfigureAwait(false);
                Require(blocked.StatusCode == 423);
                var refresh = await client.RefreshLockAsync(file, lockToken!, cancellationToken: ct).ConfigureAwait(false);
                refresh.EnsureSucceeded();
                (await client.UnlockAsync(file, lockToken!, ct).ConfigureAwait(false)).EnsureSucceeded();
                lockToken = null;
                return result with { Detail = "Exclusive lock, conflicting DELETE=423, refresh, UNLOCK confirmed." };
            }).ConfigureAwait(false);
            var copy = DavPath.FromSegments(m_matrix.RunTree, "copy.bin");
            var moved = DavPath.FromSegments(m_matrix.RunTree, "moved.bin");
            bool copied = await CheckAsync("copy", async () =>
            {
                var result = Response("copy", await client.CopyAsync(file, copy, cancellationToken: ct).ConfigureAwait(false));
                if (result.Outcome == "passed")
                {
                    Require((await VerifyDownloadAsync(client, copy, null, s_content, ct).ConfigureAwait(false)).Outcome == "passed");
                }
                return result;
            }).ConfigureAwait(false);
            if (copied)
            {
                await CheckAsync("copy-no-overwrite", async () =>
                    Expected("copy-no-overwrite", await client.CopyAsync(file, copy, cancellationToken: ct).ConfigureAwait(false), 412)).ConfigureAwait(false);
                await CheckAsync("move", async () =>
                {
                    var result = Response("move", await client.MoveAsync(copy, moved, cancellationToken: ct).ConfigureAwait(false));
                    if (result.Outcome == "passed")
                    {
                        Require((await VerifyDownloadAsync(client, moved, null, s_content, ct).ConfigureAwait(false)).Outcome == "passed");
                        Require((await client.HeadAsync(copy, cancellationToken: ct).ConfigureAwait(false)).StatusCode == 404);
                    }
                    return result;
                }).ConfigureAwait(false);
            }
            await CheckAsync("sync-discovery", async () =>
            {
                var name = new PropertyName("DAV:", "supported-report-set");
                var response = await client.PropFindAsync(tree, new PropFindOptions
                {
                    Depth = DavDepth.Zero,
                    Selection = DavPropertySelection.Selected,
                    Properties = [name]
                }, ct).ConfigureAwait(false);
                if (response.StatusCode != 207)
                {
                    return Snapshot("sync-discovery", response, optional: true);
                }
                var values = response.Resources.SelectMany(r => r.GetProperties(name)).ToArray();
                foreach (var property in values.Where(p => p.StatusCode == 200))
                {
                    m_matrix.AdvertisedReports.AddRange(property.Value.ToElement().Descendants(XName.Get("report", "DAV:"))
                        .Elements().Select(e => e.Name.ToString()));
                }
                return Snapshot("sync-discovery", response, optional: true) with
                {
                    Outcome = values.Length == 1 && values[0].StatusCode == 404 ? "unavailable-property" :
                        values.Length == 1 && values[0].StatusCode == 200 ? "passed" : "failed",
                    Detail = "404 property means discovery property unavailable, NOT proof REPORT is unsupported."
                };
            }).ConfigureAwait(false);
            await CheckAsync("sync-report", async () =>
            {
                var initial = await client.SyncCollectionAsync(tree, null, [DavProperties.ETag.Name], cancellationToken: ct).ConfigureAwait(false);
                var result = Snapshot("sync-report", initial.Response, optional: true);
                if (initial.Response.StatusCode != 207)
                {
                    return result;
                }
                Require(initial.SyncToken != null && !initial.HasMore);
                Require(SyncMembersSucceeded(initial.Response, allowDeletion: false));
                var syncFile = DavPath.FromSegments(m_matrix.RunTree, "sync.bin");
                using (var input = new MemoryStream(s_content))
                {
                    (await client.UploadAsync(syncFile, input, new UploadOptions { IfNoneMatch = "*" }, ct).ConfigureAwait(false)).EnsureSucceeded();
                }
                var delta = await client.SyncCollectionAsync(tree, initial.SyncToken, [DavProperties.ETag.Name], cancellationToken: ct).ConfigureAwait(false);
                Require(delta.Response.StatusCode == 207 && !delta.HasMore && delta.Response.Resources.Any(r =>
                    r.Hrefs.Any(h => h.EndsWith("/sync.bin", StringComparison.Ordinal)) && r.GetProperties(DavProperties.ETag.Name).Any(p => p.StatusCode == 200)));
                Require(SyncMembersSucceeded(delta.Response, allowDeletion: false));
                (await client.DeleteAsync(syncFile, cancellationToken: ct).ConfigureAwait(false)).EnsureSucceeded();
                var deleted = await client.SyncCollectionAsync(tree, delta.SyncToken, [DavProperties.ETag.Name], cancellationToken: ct).ConfigureAwait(false);
                Require(deleted.Response.StatusCode == 207 && !deleted.HasMore && deleted.Response.Resources.Any(r =>
                    r.StatusCode == 404 && r.Hrefs.Any(h => h.EndsWith("/sync.bin", StringComparison.Ordinal))));
                Require(SyncMembersSucceeded(deleted.Response, allowDeletion: true));
                return result with { Outcome = "passed", Detail = "Initial token, incremental creation, and profile-specific 404 deletion observed; no durable sync claim." };
            }).ConfigureAwait(false);
            await CheckAsync("delete-file", async () =>
            {
                var result = Response("delete-file", await client.DeleteAsync(file, cancellationToken: ct).ConfigureAwait(false));
                if (result.Outcome == "passed")
                {
                    Require((await client.HeadAsync(file, cancellationToken: ct).ConfigureAwait(false)).StatusCode == 404);
                }
                return result;
            }).ConfigureAwait(false);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            if (lockToken != null)
            {
                await CheckAsync("cleanup-unlock", async () =>
                    Response("cleanup-unlock", await client.UnlockAsync(file, lockToken, cleanup.Token).ConfigureAwait(false))).ConfigureAwait(false);
            }
            if (created)
            {
                await CheckAsync("cleanup-tree", async () =>
                {
                    var result = Response("cleanup-tree", await client.DeleteAsync(tree, cancellationToken: cleanup.Token).ConfigureAwait(false));
                    if (result.Outcome == "passed")
                    {
                        Require((await client.HeadAsync(tree, cancellationToken: cleanup.Token).ConfigureAwait(false)).StatusCode == 404);
                    }
                    return result with { Detail = "Only confirmed run-owned tree deleted; absence verified." };
                }).ConfigureAwait(false);
            }
        }
        return m_matrix;
    }

    private async Task<bool> CheckAsync(string name, Func<Task<Observation>> action)
    {
        Observation result;
        try
        {
            result = (await action().ConfigureAwait(false)) with { Operation = name };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            result = Failure(name, ex);
        }
        m_matrix.Observations.Add(result);
        return result.Outcome == "passed";
    }

    private static Observation Failure(string name, Exception ex)
    {
        int? status = ex is WebDAVException dav ? dav.ResponseStatusCode : ex is HttpRequestException http ? (int?)http.StatusCode : null;
        int? member = ex is WebDAVException failure ? failure.FailedStatusCode : null;
        return new Observation(name, ex is OperationCanceledException ? "timed-out" :
            member.HasValue || status.HasValue ? Matrix.Classify(member ?? status!.Value, false, false) : "failed",
            status, PropertyStatuses: member.HasValue ? [member.Value] : null, Detail: DescribeFailure(ex));
    }

    internal static string DescribeFailure(Exception exception)
    {
        // Only fixed local protocol messages become codes; never persist an arbitrary exception message.
        string Code(Exception ex) => ex.Message switch
        {
            "Malformed DAV XML response." => "malformed-xml",
            "Malformed DAV resource response." => "malformed-resource",
            "Expected DAV:multistatus." => "missing-multistatus",
            "A DAV:response requires either a resource status or property statuses." => "resource-status-shape",
            "A DAV:propstat requires one prop and one status." => "propstat-shape",
            "Invalid DAV HTTP status line." => "invalid-status-line",
            "A DAV:response requires a direct, nonempty DAV:href." => "missing-href",
            "Duplicate DAV response status." => "duplicate-resource-status",
            "Unexpected text in DAV:multistatus." => "multistatus-text",
            "DAV XML nesting limit exceeded." => "xml-depth-limit",
            "DAV resource XML size limit exceeded." => "xml-size-limit",
            _ => ex.GetType().Name
        };
        return exception.InnerException == null ? Code(exception) : Code(exception) + ":" + Code(exception.InnerException);
    }

    private Observation Response(string name, DavResponse response, bool optional = false)
    {
        var snapshot = Snapshot(name, response, optional);
        if (response.StatusCode is < 200 or >= 300)
        {
            return snapshot;
        }
        try
        {
            if (response is DavMultiStatusResult multi)
            {
                multi.EnsureSucceeded();
            }
            else if (response is DavLockResult locked)
            {
                locked.EnsureSucceeded();
            }
            else
            {
                response.EnsureHttpSuccess();
            }
            return snapshot with { Outcome = "passed" };
        }
        catch (Exception)
        {
            return snapshot with { Outcome = "failed", Detail = "Outer success did not confirm operation/member/property success." };
        }
    }

    private Observation Snapshot(string name, DavResponse response, bool optional)
    {
        var resources = response switch
        {
            DavMultiStatusResult multi => multi.Resources,
            DavReportResult report => report.Resources,
            DavLockResult locked => locked.Resources,
            _ => Array.Empty<DavResourceResponse>()
        };
        bool advertised = m_matrix.AdvertisedMethods.Contains(response.Method, StringComparer.OrdinalIgnoreCase) ||
            response.Method == "REPORT" && m_matrix.AdvertisedReports.Contains("{DAV:}sync-collection");
        return new Observation(name, Matrix.Classify(response.StatusCode, optional, advertised), response.StatusCode,
            resources.Where(r => r.StatusCode.HasValue).Select(r => r.StatusCode!.Value).ToArray(),
            resources.SelectMany(r => r.PropertyStatuses).Where(p => p.StatusCode.HasValue).Select(p => p.StatusCode!.Value).ToArray());
    }

    private static Observation Expected(string name, DavResponse response, int status) =>
        new(name, response.StatusCode == status ? "passed" : Matrix.Classify(response.StatusCode, false, false), response.StatusCode,
            Detail: "Expected HTTP " + status);

    private static async Task<Observation> VerifyDownloadAsync(DavClient client, DavPath path, DownloadOptions? options,
        byte[] expected, CancellationToken ct)
    {
        using var download = await client.DownloadAsync(path, options, ct).ConfigureAwait(false);
        if (download.StatusCode != (options == null ? 200 : 206))
        {
            return new Observation("download", options != null && download.StatusCode == 200 ? "unsupported" :
                Matrix.Classify(download.StatusCode, false, false), download.StatusCode,
                Detail: options != null && download.StatusCode == 200 ? "Server ignored Range; not a verified range pass." : null);
        }
        if (download.Content == null)
        {
            return new Observation("download", "failed", download.StatusCode, Detail: "Missing response stream.");
        }
        // Read at most one byte beyond the expected payload, even if a server streams indefinitely.
        byte[] buffer = new byte[expected.Length + 1];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await download.Content.ReadAsync(buffer.AsMemory(count), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            count += read;
        }
        bool valid = count == expected.Length && buffer.AsSpan(0, count).SequenceEqual(expected);
        if (options != null)
        {
            valid &= download.ContentRange == $"bytes 2-7/{s_content.Length}";
        }
        return new Observation("download", valid ? "passed" : "failed", download.StatusCode, Detail: "Byte-for-byte body and range metadata checked.");
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidDataException();
        }
    }

    private static bool SyncMembersSucceeded(DavReportResult response, bool allowDeletion) =>
        response.Resources.All(r => (r.StatusCode == null || r.StatusCode is >= 200 and < 300 || allowDeletion && r.StatusCode == 404)
            && r.PropertyStatuses.All(p => p.StatusCode is >= 200 and < 300));

    private void Block(string name, string reason) => m_matrix.Observations.Add(new Observation(name, "blocked", Detail: reason));

    private static string? ReadRevision()
    {
        string? revision = Environment.GetEnvironmentVariable("WEBDAV_INTEROP_REVISION");
        return revision is { Length: 40 } && revision.All(Uri.IsHexDigit) ? revision : null;
    }
}
