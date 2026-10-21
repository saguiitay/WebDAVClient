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

    private static readonly byte[] s_replacement = Encoding.UTF8.GetBytes("A different guarded replacement.");

    private static readonly byte[] s_rejected = Encoding.UTF8.GetBytes("This rejected payload must never replace the stored representation.");

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
            await CheckGuardedWritesAsync(client, ct).ConfigureAwait(false);
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
                string policy = config.ApachePathPlusAlias
                    ? " Explicit Apache path-only plus/%2B target-alias opt-in; strict default identity is unchanged."
                    : "";
                try
                {
                    var response = await client.UpdatePropertiesAsync(file,
                        [DavPropertyChange.Set(new DavXmlValue(new XElement(XName.Get(s_marker.LocalName, s_marker.Namespace), "interop-v1")))],
                        options: config.ApachePathPlusAlias ? new PropPatchOptions { AllowPathPlusEncodingAlias = true } : null,
                        cancellationToken: ct).ConfigureAwait(false);
                    var result = Response("dead-property", response, optional: true);
                    if (result.Outcome != "passed")
                    {
                        return result with { Detail = (result.Detail ?? "") + policy };
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
                    return result with { Outcome = valid ? "passed" : "failed", Detail = "Set custom property and read exact value back." + policy };
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return Failure("dead-property", ex) with { Detail = stage + ":" + DescribeFailure(ex) + policy };
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
                string? wireDeviation = LockWireDeviation(response.GetHeader("Lock-Token"), response.Locks.Select(l => l.Token).ToArray());
                var blocked = await client.DeleteAsync(file, cancellationToken: ct).ConfigureAwait(false);
                Require(blocked.StatusCode == 423);
                var refresh = await client.RefreshLockAsync(file, lockToken!, cancellationToken: ct).ConfigureAwait(false);
                refresh.EnsureSucceeded();
                (await client.UnlockAsync(file, lockToken!, ct).ConfigureAwait(false)).EnsureSucceeded();
                lockToken = null;
                return result with
                {
                    Detail = "Exclusive lock, conflicting DELETE=423, refresh, UNLOCK confirmed." +
                        (wireDeviation == null ? "" : " Wire deviation: " + wireDeviation + ".")
                };
            }).ConfigureAwait(false);
            await CheckLockConditionedMutationsAsync(client, ct).ConfigureAwait(false);
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
            bool emptyReportSetConfirmed = false;
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
                emptyReportSetConfirmed = values.Length == 1 && values[0].StatusCode == 200 &&
                    SyncReportEvidence.IsConfirmedEmptyReportSet(values[0].Value.ToElement());
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
                    return SyncReportEvidence.Classify(result, config.Server, config.Version, emptyReportSetConfirmed,
                        m_matrix.AdvertisedReports, initial.Response.Document?.ToElement());
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

    internal async Task<Matrix> CheckGuardedWritesAsync(DavClient client, CancellationToken ct)
    {
        var file = DavPath.FromSegments(m_matrix.RunTree, "conditional-create.bin");
        string? originalTag = await SeedGuardedFileAsync("conditional-create", client, file, ct).ConfigureAwait(false);
        if (originalTag == null)
        {
            Block("conditional-put", "Guarded creation, bytes and strong validator were not confirmed.");
            Block("conditional-integrity", "No confirmed guarded-write baseline.");
        }
        else
        {
            await CheckAsync("conditional-put", async () =>
            {
                using var input = new MemoryStream(s_rejected);
                return Expected("conditional-put", await client.UploadAsync(file, input,
                    new UploadOptions { IfNoneMatch = "*" }, ct).ConfigureAwait(false), 412);
            }).ConfigureAwait(false);
            await CheckAsync("conditional-integrity", () =>
                VerifyStateAsync(client, file, s_content, originalTag, ct)).ConfigureAwait(false);
        }

        var etagFile = DavPath.FromSegments(m_matrix.RunTree, "conditional-etag.bin");
        string? initialTag = await SeedGuardedFileAsync("conditional-etag-create", client, etagFile, ct).ConfigureAwait(false);
        bool replaced = initialTag != null && await CheckAsync("conditional-if-match", async () =>
        {
            using var input = new MemoryStream(s_replacement);
            return Response("conditional-if-match", await client.UploadAsync(etagFile, input,
                new UploadOptions { IfMatch = initialTag }, ct).ConfigureAwait(false));
        }).ConfigureAwait(false);
        if (!replaced)
        {
            if (initialTag == null)
            {
                Block("conditional-if-match", "No confirmed strong-validator baseline.");
            }
            Block("conditional-if-match-integrity", "Matching-validator replacement was not confirmed.");
            Block("conditional-stale-put", "No confirmed replacement with a new strong validator.");
            Block("conditional-stale-integrity", "No confirmed stale-validator baseline.");
            return m_matrix;
        }

        string? replacementTag = null;
        bool verified = await CheckAsync("conditional-if-match-integrity", async () =>
        {
            var result = await VerifyDownloadAsync(client, etagFile, null, s_replacement, ct).ConfigureAwait(false);
            if (result.Outcome == "passed")
            {
                replacementTag = await ReadStrongEntityTagAsync(client, etagFile, ct).ConfigureAwait(false);
                RequireChangedEntityTag(initialTag!, replacementTag);
            }
            return result with { Detail = "Replacement bytes and a changed strong validator checked." };
        }).ConfigureAwait(false);
        if (!verified || replacementTag == null)
        {
            Block("conditional-stale-put", "Replacement bytes and a new strong validator were not confirmed.");
            Block("conditional-stale-integrity", "No confirmed stale-validator baseline.");
            return m_matrix;
        }
        await CheckAsync("conditional-stale-put", async () =>
        {
            using var input = new MemoryStream(s_rejected);
            return Expected("conditional-stale-put", await client.UploadAsync(etagFile, input,
                new UploadOptions { IfMatch = initialTag }, ct).ConfigureAwait(false), 412);
        }).ConfigureAwait(false);
        await CheckAsync("conditional-stale-integrity", () =>
            VerifyStateAsync(client, etagFile, s_replacement, replacementTag, ct)).ConfigureAwait(false);
        return m_matrix;
    }

    internal async Task<Matrix> CheckLockConditionedMutationsAsync(DavClient client, CancellationToken ct)
    {
        foreach (string operation in new[] { "put", "delete", "move" })
        {
            string prefix = "lock-" + operation;
            var file = DavPath.FromSegments(m_matrix.RunTree, prefix + " café + %.bin");
            var destination = DavPath.FromSegments(m_matrix.RunTree, prefix + "-destination café + %.bin");
            string? tag = await SeedGuardedFileAsync(prefix + "-create", client, file, ct).ConfigureAwait(false);
            if (tag == null)
            {
                Block(prefix + "-valid", "No confirmed run-owned resource/validator for lock-conditioned mutations.");
                continue;
            }
            string? token = null;
            try
            {
                bool acquired = await CheckAsync(prefix + "-acquire", async () =>
                {
                    var response = await client.AcquireLockAsync(file, cancellationToken: ct).ConfigureAwait(false);
                    var result = Response(prefix + "-acquire", response, optional: true);
                    if (result.Outcome == "passed")
                    {
                        token = response.LockToken?.Trim('<', '>');
                        Require(!string.IsNullOrWhiteSpace(token));
                        tag = await ReadStrongEntityTagAsync(client, file, ct).ConfigureAwait(false);
                    }
                    return result with { Detail = result.Outcome == "passed" ?
                        "Validated exclusive lock acquired; token value is not recorded." :
                        "LOCK not confirmed; dependent token-conditioned mutations were not executed." };
                }).ConfigureAwait(false);
                if (!acquired || token == null)
                {
                    return m_matrix;
                }
                string confirmedToken = token;
                string baselineTag = tag;
                var wrong = new DavConditionList(DavCondition.StateToken("urn:uuid:" + Guid.NewGuid()));
                var wrongConditions = operation == "move"
                    ? DavIfConditions.Tagged(new DavTaggedConditions(file, wrong))
                    : DavIfConditions.Untagged(wrong);
                await CheckAsync(prefix + "-wrong-token", async () =>
                    Expected(prefix + "-wrong-token", await MutateAsync(operation, client, file, destination,
                        wrongConditions, s_rejected, ct).ConfigureAwait(false), 412)).ConfigureAwait(false);
                bool intact = await CheckAsync(prefix + "-wrong-token-integrity", async () =>
                {
                    var result = await VerifyStateAsync(client, file, s_content, baselineTag, ct).ConfigureAwait(false);
                    if (operation == "move")
                    {
                        var absent = Expected(prefix, await client.HeadAsync(destination, cancellationToken: ct).ConfigureAwait(false), 404);
                        if (absent.Outcome != "passed")
                        {
                            return absent with { Detail = "Rejected MOVE created a destination." };
                        }
                    }
                    return result;
                }).ConfigureAwait(false);
                if (!intact)
                {
                    Block(prefix + "-valid", "Rejected mutation changed the baseline; no repair or replay attempted.");
                    continue;
                }

                var conjunction = new DavConditionList(DavCondition.StateToken(confirmedToken), DavCondition.EntityTag(baselineTag));
                var conditions = operation == "put" ? DavIfConditions.Untagged(wrong, conjunction) :
                    operation == "move" ? DavIfConditions.Tagged(new DavTaggedConditions(file, conjunction)) :
                    DavIfConditions.Untagged(conjunction);
                bool mutated = await CheckAsync(prefix + "-valid", async () =>
                    Response(prefix + "-valid", await MutateAsync(operation, client, file, destination,
                        conditions, s_replacement, ct).ConfigureAwait(false))).ConfigureAwait(false);
                if (!mutated)
                {
                    Block(prefix + "-valid-integrity", "Token-conditioned mutation did not confirm success.");
                    continue;
                }
                string currentTag = baselineTag;
                bool validState = await CheckAsync(prefix + "-valid-integrity", async () =>
                {
                    if (operation == "delete")
                    {
                        var absent = Expected(prefix, await client.HeadAsync(file, cancellationToken: ct).ConfigureAwait(false), 404);
                        if (absent.Outcome == "passed")
                        {
                            token = null;
                        }
                        return absent;
                    }
                    var result = await VerifyDownloadAsync(client, operation == "move" ? destination : file, null,
                        operation == "move" ? s_content : s_replacement, ct).ConfigureAwait(false);
                    if (result.Outcome == "passed")
                    {
                        if (operation == "move")
                        {
                            var absent = Expected(prefix, await client.HeadAsync(file, cancellationToken: ct).ConfigureAwait(false), 404);
                            if (absent.Outcome != "passed")
                            {
                                return absent;
                            }
                            // RFC 4918 section 7.6: a successful MOVE does not move the source lock.
                            token = null;
                        }
                        else
                        {
                            currentTag = await ReadStrongEntityTagAsync(client, file, ct).ConfigureAwait(false);
                            RequireChangedEntityTag(baselineTag, currentTag);
                        }
                    }
                    return result;
                }).ConfigureAwait(false);
                if (operation != "put" || !validState)
                {
                    continue;
                }

                var stale = DavIfConditions.Untagged(new DavConditionList(
                    DavCondition.StateToken(confirmedToken), DavCondition.EntityTag(baselineTag)));
                await CheckAsync(prefix + "-stale-condition", async () =>
                    Expected(prefix, await MutateAsync("put", client, file, destination, stale, s_rejected, ct).ConfigureAwait(false), 412)).ConfigureAwait(false);
                bool unchanged = await CheckAsync(prefix + "-stale-condition-integrity", () =>
                    VerifyStateAsync(client, file, s_replacement, currentTag, ct)).ConfigureAwait(false);
                if (!unchanged)
                {
                    Block(prefix + "-released-token", "Rejected composed condition changed the baseline; no replay attempted.");
                    continue;
                }
                bool released = await CheckAsync(prefix + "-release", async () =>
                {
                    var result = Response(prefix, await client.UnlockAsync(file, confirmedToken, ct).ConfigureAwait(false));
                    if (result.Outcome == "passed")
                    {
                        token = null;
                        currentTag = await ReadStrongEntityTagAsync(client, file, ct).ConfigureAwait(false);
                        Require((await VerifyDownloadAsync(client, file, null, s_replacement, ct).ConfigureAwait(false)).Outcome == "passed");
                    }
                    return result;
                }).ConfigureAwait(false);
                if (released)
                {
                    var releasedConditions = DavIfConditions.Untagged(new DavConditionList(DavCondition.StateToken(confirmedToken)));
                    await CheckAsync(prefix + "-released-token", async () =>
                        Expected(prefix, await MutateAsync("put", client, file, destination, releasedConditions, s_rejected, ct).ConfigureAwait(false), 412)).ConfigureAwait(false);
                    await CheckAsync(prefix + "-released-token-integrity", () =>
                        VerifyStateAsync(client, file, s_replacement, currentTag, ct)).ConfigureAwait(false);
                }
            }
            finally
            {
                if (token != null)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    await CheckAsync(prefix + "-cleanup-unlock", async () =>
                        Response(prefix, await client.UnlockAsync(file, token, cleanup.Token).ConfigureAwait(false))).ConfigureAwait(false);
                }
            }
        }
        return m_matrix;
    }

    private async Task<string?> SeedGuardedFileAsync(string name, DavClient client, DavPath file, CancellationToken ct)
    {
        string? tag = null;
        bool seeded = await CheckAsync(name, async () =>
        {
            using var input = new MemoryStream(s_content);
            var result = Expected(name, await client.UploadAsync(file, input,
                new UploadOptions { IfNoneMatch = "*" }, ct).ConfigureAwait(false), 201);
            if (result.Outcome == "passed")
            {
                var bytes = await VerifyDownloadAsync(client, file, null, s_content, ct).ConfigureAwait(false);
                if (bytes.Outcome != "passed")
                {
                    return bytes;
                }
                tag = await ReadStrongEntityTagAsync(client, file, ct).ConfigureAwait(false);
            }
            return result with { Detail = "Run-owned create-if-absent, exact bytes and a strong validator checked." };
        }).ConfigureAwait(false);
        return seeded ? tag : null;
    }

    private static async Task<DavResponse> MutateAsync(string operation, DavClient client, DavPath file,
        DavPath destination, DavIfConditions conditions, byte[] content, CancellationToken ct)
    {
        switch (operation)
        {
            case "put":
                using (var input = new MemoryStream(content))
                {
                    return await client.UploadAsync(file, input, new UploadOptions { Conditions = conditions }, ct).ConfigureAwait(false);
                }
            case "delete":
                return await client.DeleteAsync(file, new DavRequestOptions { Conditions = conditions }, ct).ConfigureAwait(false);
            case "move":
                return await client.MoveAsync(file, destination, new MoveOptions { Conditions = conditions }, ct).ConfigureAwait(false);
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static async Task<string> ReadStrongEntityTagAsync(DavClient client, DavPath file, CancellationToken ct)
    {
        var response = await client.HeadAsync(file, cancellationToken: ct).ConfigureAwait(false);
        response.EnsureHttpSuccess();
        Require(response.StatusCode == 200);
        if (!EntityTagHeaderValue.TryParse(response.GetHeader("ETag"), out var tag) || tag.IsWeak || tag.Tag == "*")
        {
            throw new InvalidDataException("Guarded probes require one strong ETag.");
        }
        return tag.ToString();
    }

    private static void RequireChangedEntityTag(string previous, string current)
    {
        if (string.Equals(previous, current, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Changed probe bytes require a changed strong ETag.");
        }
    }

    private static async Task<Observation> VerifyStateAsync(DavClient client, DavPath file, byte[] content, string tag, CancellationToken ct)
    {
        var result = await VerifyDownloadAsync(client, file, null, content, ct).ConfigureAwait(false);
        if (result.Outcome != "passed")
        {
            return result;
        }
        bool unchanged = string.Equals(tag, await ReadStrongEntityTagAsync(client, file, ct).ConfigureAwait(false), StringComparison.Ordinal);
        return result with { Outcome = unchanged ? "passed" : "failed", Detail = "Exact stored bytes and unchanged strong validator checked." };
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
            "A DAV resource href requires an HTTP(S) URI or an absolute path." => "invalid-resource-href",
            "Invalid character in DAV href path or query." => "invalid-href-character",
            "PROPPATCH did not report outcomes for every requested property on the target resource." => "proppatch-target-or-property-outcomes",
            "Malformed LOCK response." => "malformed-lock",
            "A successful lock acquisition requires exactly one Lock-Token header." => "lock-header-count",
            "Invalid acquired lock token URI." => "lock-token-uri",
            "The acquired Lock-Token must match exactly one active lock." => "lock-token-discovery-mismatch",
            "Invalid granted lock timeout." => "invalid-lock-timeout",
            "Invalid active lock scope, type or depth." => "invalid-lock-shape",
            "LOCK requires a DAV:prop root." => "lock-prop-root",
            "LOCK requires DAV:lockdiscovery." => "missing-lock-discovery",
            "No active locks were reported." => "empty-lock-discovery",
            "Guarded probes require one strong ETag." => "strong-etag-unavailable",
            "Changed probe bytes require a changed strong ETag." => "unchanged-strong-etag",
            _ => ex.GetType().Name
        };
        return exception.InnerException == null ? Code(exception) : Code(exception) + ":" + Code(exception.InnerException);
    }

    internal static string? LockWireDeviation(string? rawHeader, IReadOnlyList<string?> discoveryTokens)
    {
        string? raw = rawHeader?.Trim(' ', '\t');
        return !string.IsNullOrEmpty(raw) && !raw.StartsWith('<') &&
            !raw.Any(char.IsWhiteSpace) && raw.IndexOfAny(['<', '>', '"']) < 0 &&
            Uri.TryCreate(raw, UriKind.Absolute, out _) && discoveryTokens.Count == 1 &&
            string.Equals(raw, discoveryTokens[0], StringComparison.Ordinal)
                ? "unbracketed-lock-token-confirmed" : null;
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
