using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Protocol;

namespace WebDAVClient.InteropTests;

[TestClass]
public sealed class GuardedMutationTests
{
    [TestMethod]
    public async Task GuardedWritesCheckDistinctBodiesAndBothValidatorDirections()
    {
        using var handler = new GuardedHandler();
        var result = await RunAsync(handler, locks: false).ConfigureAwait(false);
        Assert.AreEqual("1.1", result.SuiteVersion);
        Assert.AreEqual(8, result.Observations.Count);
        Assert.IsTrue(result.Observations.All(o => o.Outcome == "passed"));
        var puts = handler.Requests.Where(r => r.Method == "PUT").ToArray();
        Assert.AreEqual(5, puts.Length);
        Assert.IsFalse(puts[0].Body.SequenceEqual(puts[1].Body));
        Assert.IsFalse(puts[3].Body.SequenceEqual(puts[4].Body));
        Assert.AreEqual("*", puts[0].IfNoneMatch);
        Assert.AreEqual("*", puts[1].IfNoneMatch);
        Assert.AreEqual(puts[3].IfMatch, puts[4].IfMatch);
        Assert.IsNotNull(puts[4].IfMatch);
        Assert.AreNotEqual(puts[0].Path, puts[2].Path);
    }

    [DataTestMethod]
    [DataRow("ignore-none", "conditional-put", "conditional-integrity", "failed")]
    [DataRow("ignore-match", "conditional-stale-put", "conditional-stale-integrity", "failed")]
    [DataRow("write-on-reject", "conditional-put", "conditional-integrity", "passed")]
    [DataRow("body-on-reject", "conditional-put", "conditional-integrity", "passed")]
    [DataRow("etag-on-reject", "conditional-stale-put", "conditional-stale-integrity", "passed")]
    public async Task RejectionsRequireStatusBytesAndValidatorIntegrity(string fault, string status, string integrity, string outcome)
    {
        using var handler = new GuardedHandler(fault);
        var result = await RunAsync(handler, locks: false).ConfigureAwait(false);
        Assert.AreEqual(outcome, result.Observations.Single(o => o.Operation == status).Outcome);
        Assert.AreEqual("failed", result.Observations.Single(o => o.Operation == integrity).Outcome);
        Assert.AreEqual(5, handler.Requests.Count(r => r.Method == "PUT"), "No repair or unconditional replay.");
        Assert.IsTrue(handler.Requests.Where(r => r.Method == "PUT").All(r => r.IfMatch != null || r.IfNoneMatch != null));
    }

    [DataTestMethod]
    [DataRow("missing-etag")]
    [DataRow("weak-etag")]
    [DataRow("malformed-etag")]
    [DataRow("wildcard-etag")]
    [DataRow("duplicate-etag")]
    [DataRow("combined-etag")]
    public async Task MissingStrongValidatorBlocksDependentWrites(string fault)
    {
        using var handler = new GuardedHandler(fault);
        var result = await RunAsync(handler, locks: false).ConfigureAwait(false);
        Assert.IsTrue(result.Observations.Any(o => o.Detail == (fault == "weak-etag" ? "weak-etag" : "strong-etag-unavailable")));
        Assert.AreEqual("blocked", result.Observations.Single(o => o.Operation == "conditional-stale-put").Outcome);
        Assert.AreEqual(2, handler.Requests.Count(r => r.Method == "PUT"));
        Assert.IsTrue(handler.Requests.Where(r => r.Method == "PUT").All(r => r.IfNoneMatch == "*"));
    }

    [TestMethod]
    public async Task UnchangedValidatorCannotProduceAFalseStaleValidatorPass()
    {
        using var handler = new GuardedHandler("unchanged-etag");
        var result = await RunAsync(handler, locks: false).ConfigureAwait(false);
        Assert.AreEqual("unchanged-strong-etag", result.Observations.Single(o => o.Operation == "conditional-if-match-integrity").Detail);
        Assert.AreEqual("blocked", result.Observations.Single(o => o.Operation == "conditional-stale-put").Outcome);
        Assert.AreEqual(4, handler.Requests.Count(r => r.Method == "PUT"));
    }

    [TestMethod]
    public async Task RejectingEveryIfMatchDoesNotVerifyMatchingOrStaleWrites()
    {
        using var handler = new GuardedHandler("reject-match");
        var result = await RunAsync(handler, locks: false).ConfigureAwait(false);
        Assert.AreEqual("failed", result.Observations.Single(o => o.Operation == "conditional-if-match").Outcome);
        Assert.AreEqual("blocked", result.Observations.Single(o => o.Operation == "conditional-stale-put").Outcome);
        Assert.AreEqual(4, handler.Requests.Count(r => r.Method == "PUT"));
    }

    [TestMethod]
    public async Task CurrentSuiteRequiresTheNewStaleIntegrityObservation()
    {
        using var handler = new GuardedHandler();
        var result = await RunAsync(handler, locks: false).ConfigureAwait(false);
        foreach (string operation in Matrix.RequiredOperations)
        {
            if (!result.Observations.Any(o => o.Operation == operation))
            {
                result.Observations.Add(new Observation(operation, "passed"));
            }
        }
        Assert.IsTrue(result.Succeeded);
        result.Observations.RemoveAll(o => o.Operation == "conditional-stale-integrity");
        Assert.IsFalse(result.Succeeded);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LockMutationsCheckTaggedMoveAndComposedAndReleasedConditions(bool absolutePathTags)
    {
        using var handler = new GuardedHandler();
        var result = await RunAsync(handler, locks: true, absolutePathTags).ConfigureAwait(false);
        Assert.AreEqual(29, result.Observations.Count);
        Assert.IsTrue(result.Observations.All(o => o.Outcome == "passed"));
        Assert.AreEqual(3, handler.Requests.Count(r => r.Method == "LOCK"));
        Assert.AreEqual(1, handler.Requests.Count(r => r.Method == "UNLOCK"));
        var moves = handler.Requests.Where(r => r.Method == "MOVE").ToArray();
        Assert.AreEqual(3, moves.Length);
        string origin = absolutePathTags ? "" : "http://localhost";
        Assert.IsTrue(moves.All(r => r.Conditions!.StartsWith("<" + origin + r.Path + "> ", StringComparison.Ordinal)));
        var conditionedPuts = handler.Requests.Where(r => r.Method == "PUT" && r.Conditions != null).ToArray();
        Assert.AreEqual(5, conditionedPuts.Length);
        Assert.AreEqual(2, Regex.Matches(conditionedPuts[2].Conditions!, @"\([^()]*\)").Count, "Alternative lists must be OR, conditions within a list AND.");
        Assert.IsTrue(conditionedPuts[2].Conditions!.Contains("[\"", StringComparison.Ordinal));
        Assert.IsFalse(conditionedPuts[0].Body.SequenceEqual(conditionedPuts[2].Body));
        Assert.IsFalse(conditionedPuts[2].Body.SequenceEqual(conditionedPuts[3].Body));
        string json = JsonSerializer.Serialize(result);
        foreach (string token in handler.Tokens)
        {
            Assert.IsFalse(json.Contains(token, StringComparison.Ordinal));
        }
        Assert.IsFalse(json.Contains("private-etag", StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow("ignore-lock-put", "put", "failed")]
    [DataRow("ignore-lock-delete", "delete", "failed")]
    [DataRow("ignore-lock-move", "move", "failed")]
    [DataRow("lie-lock-put", "put", "passed")]
    [DataRow("lie-lock-delete", "delete", "passed")]
    [DataRow("lie-lock-move", "move", "passed")]
    public async Task WrongLockTokensCannotMutateSourceOrDestination(string fault, string operation, string status)
    {
        using var handler = new GuardedHandler(fault);
        var result = await RunAsync(handler, locks: true).ConfigureAwait(false);
        Assert.AreEqual(status, result.Observations.Single(o => o.Operation == $"lock-{operation}-wrong-token").Outcome);
        Assert.AreEqual("failed", result.Observations.Single(o => o.Operation == $"lock-{operation}-wrong-token-integrity").Outcome);
        Assert.AreEqual("blocked", result.Observations.Single(o => o.Operation == $"lock-{operation}-valid").Outcome);
    }

    [TestMethod]
    public async Task ValidTokenDoesNotOverrideFalseEntityTagConjunction()
    {
        using var handler = new GuardedHandler("ignore-dav-etag");
        var result = await RunAsync(handler, locks: true).ConfigureAwait(false);
        Assert.AreEqual("passed", result.Observations.Single(o => o.Operation == "lock-put-valid").Outcome);
        Assert.AreEqual("failed", result.Observations.Single(o => o.Operation == "lock-put-stale-condition").Outcome);
        Assert.AreEqual("failed", result.Observations.Single(o => o.Operation == "lock-put-stale-condition-integrity").Outcome);
        Assert.AreEqual("blocked", result.Observations.Single(o => o.Operation == "lock-put-released-token").Outcome);
        Assert.AreEqual(1, handler.Requests.Count(r => r.Method == "UNLOCK"), "A confirmed held lock still needs bounded cleanup.");
    }

    [DataTestMethod]
    [DataRow("put")]
    [DataRow("delete")]
    [DataRow("move")]
    public async Task ValidTokenCannotMaskAnIgnoredFalseEntityTag(string operation)
    {
        using var handler = new GuardedHandler("ignore-unknown-dav-etag");
        var result = await RunAsync(handler, locks: true).ConfigureAwait(false);
        Assert.AreEqual("failed", result.Observations.Single(o => o.Operation == $"lock-{operation}-wrong-etag").Outcome);
        Assert.AreEqual("failed", result.Observations.Single(o => o.Operation == $"lock-{operation}-wrong-etag-integrity").Outcome);
        Assert.AreEqual("blocked", result.Observations.Single(o => o.Operation == $"lock-{operation}-valid").Outcome);
    }

    [DataTestMethod]
    [DataRow("lock-rejection-dav", "passed")]
    [DataRow("lock-rejection-other-namespace", "failed")]
    [DataRow("lock-rejection-other-condition", "failed")]
    [DataRow("lock-rejection-empty", "failed")]
    public async Task LockedRejectionRequiresTheQualifiedLockCondition(string fault, string outcome)
    {
        using var handler = new GuardedHandler(fault);
        var result = await RunAsync(handler, locks: true).ConfigureAwait(false);
        foreach (string operation in new[] { "put", "delete", "move" })
        {
            var rejected = result.Observations.Single(o => o.Operation == $"lock-{operation}-wrong-token");
            Assert.AreEqual(outcome, rejected.Outcome);
            Assert.AreEqual<int?>(423, rejected.HttpStatus);
            Assert.AreEqual("passed", result.Observations.Single(o => o.Operation == $"lock-{operation}-wrong-etag").Outcome);
            Assert.AreEqual("passed", result.Observations.Single(o => o.Operation == $"lock-{operation}-valid").Outcome);
        }
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("sensitive-body-text", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AlternativesMustNotBeTreatedAsOneConjunction()
    {
        using var handler = new GuardedHandler("and-alternatives");
        var result = await RunAsync(handler, locks: true).ConfigureAwait(false);
        Assert.AreEqual("failed", result.Observations.Single(o => o.Operation == "lock-put-valid").Outcome);
        Assert.AreEqual("blocked", result.Observations.Single(o => o.Operation == "lock-put-valid-integrity").Outcome);
    }

    [TestMethod]
    public async Task ReleasedTokenCannotAuthorizeAnotherWrite()
    {
        using var handler = new GuardedHandler("ignore-released");
        var result = await RunAsync(handler, locks: true).ConfigureAwait(false);
        Assert.AreEqual("failed", result.Observations.Single(o => o.Operation == "lock-put-released-token").Outcome);
        Assert.AreEqual("failed", result.Observations.Single(o => o.Operation == "lock-put-released-token-integrity").Outcome);
    }

    [TestMethod]
    public async Task UnsupportedLockDoesNotExecuteDependentMutations()
    {
        using var handler = new GuardedHandler("no-lock");
        var result = await RunAsync(handler, locks: true).ConfigureAwait(false);
        Assert.AreEqual(2, result.Observations.Count);
        Assert.AreEqual("unsupported", result.Observations.Single(o => o.Operation == "lock-put-acquire").Outcome);
        Assert.IsFalse(handler.Requests.Any(r => r.Conditions != null));
        Assert.AreEqual(1, handler.Requests.Count(r => r.Method == "PUT"));
    }

    [DataTestMethod]
    [DataRow("put")]
    [DataRow("delete")]
    [DataRow("move")]
    public async Task RejectingValidLockConditionsIsNotACompatibilityPass(string operation)
    {
        using var handler = new GuardedHandler("reject-lock-" + operation);
        var result = await RunAsync(handler, locks: true).ConfigureAwait(false);
        Assert.AreEqual("passed", result.Observations.Single(o => o.Operation == $"lock-{operation}-wrong-token").Outcome);
        Assert.AreEqual("failed", result.Observations.Single(o => o.Operation == $"lock-{operation}-valid").Outcome);
        Assert.AreEqual("blocked", result.Observations.Single(o => o.Operation == $"lock-{operation}-valid-integrity").Outcome);
    }

    private static async Task<Matrix> RunAsync(GuardedHandler handler, bool locks, bool absolutePathTags = false)
    {
        var environment = new Dictionary<string, string?>
        {
            ["WEBDAV_INTEROP_RUN"] = "1",
            ["WEBDAV_INTEROP_ENDPOINT"] = "http://localhost/dav/",
            ["WEBDAV_INTEROP_SERVER"] = "test",
            ["WEBDAV_INTEROP_VERSION"] = "1"
        };
        environment["WEBDAV_INTEROP_ABSOLUTE_PATH_RESOURCE_TAGS"] = absolutePathTags ? "1" : null;
        var configuration = Configuration.Read(k => environment.GetValueOrDefault(k));
        var suite = new CompatibilitySuite(configuration);
        using var http = new System.Net.Http.HttpClient(handler);
        using var client = new DavClient(configuration.Endpoint, http);
        return locks ? await suite.CheckLockConditionedMutationsAsync(client, CancellationToken.None).ConfigureAwait(false) :
            await suite.CheckGuardedWritesAsync(client, CancellationToken.None).ConfigureAwait(false);
    }

    private sealed record SentRequest(string Method, string Path, string? IfMatch, string? IfNoneMatch,
        string? Conditions, byte[] Body);

    private sealed class Entry(byte[] content, string tag)
    {
        internal byte[] Content
        {
            get;
            set;
        } = content;

        internal string Tag
        {
            get;
            set;
        } = tag;

        internal string? Token
        {
            get;
            set;
        }
    }

    private sealed class GuardedHandler(string fault = "") : HttpMessageHandler
    {
        private readonly Dictionary<string, Entry> m_files = new(StringComparer.Ordinal);

        private int m_version;

        internal List<SentRequest> Requests
        {
            get;
        } = [];

        internal List<string> Tokens
        {
            get;
        } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string method = request.Method.Method;
            string path = request.RequestUri!.AbsolutePath;
            string? match = Header(request, "If-Match");
            string? none = Header(request, "If-None-Match");
            string? conditions = Header(request, "If");
            byte[] body = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add(new SentRequest(method, path, match, none, conditions, body));
            m_files.TryGetValue(path, out var entry);
            if (method is "GET" or "HEAD")
            {
                if (entry == null)
                {
                    return Reply(404);
                }
                var response = Reply(200, method == "HEAD" ? [] : entry.Content);
                response.Content.Headers.ContentLength = entry.Content.Length;
                string? tag = fault switch
                {
                    "missing-etag" => null,
                    "weak-etag" => "W/" + entry.Tag,
                    "malformed-etag" => "private-invalid-etag",
                    "wildcard-etag" => "*",
                    "combined-etag" => entry.Tag + ", \"private-other-etag\"",
                    _ => entry.Tag
                };
                if (tag != null)
                {
                    response.Headers.TryAddWithoutValidation("ETag", tag);
                    if (fault == "duplicate-etag")
                    {
                        response.Headers.TryAddWithoutValidation("ETag", "\"private-other-etag\"");
                    }
                }
                return response;
            }
            if (method == "LOCK")
            {
                if (fault == "no-lock")
                {
                    return Reply(405);
                }
                Assert.IsNotNull(entry);
                string token = "urn:uuid:" + Guid.NewGuid();
                entry.Token = token;
                Tokens.Add(token);
                XNamespace dav = "DAV:";
                var xml = new XElement(dav + "prop", new XElement(dav + "lockdiscovery",
                    new XElement(dav + "activelock",
                        new XElement(dav + "lockscope", new XElement(dav + "exclusive")),
                        new XElement(dav + "locktype", new XElement(dav + "write")),
                        new XElement(dav + "depth", "0"),
                        new XElement(dav + "timeout", "Second-600"),
                        new XElement(dav + "locktoken", new XElement(dav + "href", token)),
                        new XElement(dav + "lockroot", new XElement(dav + "href", request.RequestUri.AbsoluteUri)))));
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(xml.ToString(), Encoding.UTF8, "application/xml")
                };
                response.Headers.TryAddWithoutValidation("Lock-Token", "<" + token + ">");
                return response;
            }
            if (method == "UNLOCK")
            {
                if (entry?.Token == null || Header(request, "Lock-Token")?.Trim('<', '>') != entry.Token)
                {
                    return Reply(409);
                }
                entry.Token = null;
                return Reply(204);
            }

            bool badNone = none == "*" && entry != null;
            bool badMatch = match != null && match != entry?.Tag;
            bool badCondition = conditions != null && !Evaluate(conditions, entry);
            bool ignoreCondition = fault == "ignore-lock-" + method.ToLowerInvariant() ||
                fault == "ignore-released" && entry?.Token == null;
            bool rejected = badNone && fault != "ignore-none" || badMatch && fault != "ignore-match" ||
                badCondition && !ignoreCondition || fault == "reject-match" && match != null ||
                fault == "reject-lock-" + method.ToLowerInvariant() && conditions != null;
            bool lying = rejected && (fault is "write-on-reject" or "body-on-reject" || fault == "lie-lock-" + method.ToLowerInvariant());
            if (rejected && !lying)
            {
                if (fault.StartsWith("lock-rejection-", StringComparison.Ordinal) &&
                    entry?.Token != null && conditions != null && !conditions.Contains(entry.Token, StringComparison.Ordinal))
                {
                    if (fault == "lock-rejection-empty")
                    {
                        return Reply(423);
                    }
                    XNamespace dav = "DAV:";
                    XNamespace other = "urn:webdavclient:private";
                    var condition = fault == "lock-rejection-other-namespace" ? other + "lock-token-submitted" :
                        fault == "lock-rejection-other-condition" ? dav + "no-conflicting-lock" : dav + "lock-token-submitted";
                    var xml = new XElement(dav + "error", new XElement(condition),
                        new XElement(other + "message", "sensitive-body-text"));
                    return new HttpResponseMessage((HttpStatusCode)423)
                    {
                        Content = new StringContent(xml.ToString(), Encoding.UTF8, "application/xml")
                    };
                }
                if (fault == "etag-on-reject" && entry != null)
                {
                    entry.Tag = NextTag();
                }
                return Reply(412);
            }
            int status;
            switch (method)
            {
                case "PUT":
                    status = entry == null ? 201 : 204;
                    m_files[path] = new Entry(body, lying && fault == "body-on-reject" && entry != null ? entry.Tag : NextTag()) { Token = entry?.Token };
                    break;
                case "DELETE":
                    status = m_files.Remove(path) ? 204 : 404;
                    break;
                case "MOVE":
                    Assert.IsNotNull(entry);
                    string destination = new Uri(Header(request, "Destination")!).AbsolutePath;
                    Assert.IsFalse(m_files.ContainsKey(destination));
                    m_files.Remove(path);
                    entry.Token = null;
                    m_files.Add(destination, entry);
                    status = 201;
                    break;
                default:
                    Assert.Fail("Unexpected offline request: " + method);
                    throw new InvalidOperationException();
            }
            return Reply(lying ? 412 : status);
        }

        private bool Evaluate(string header, Entry? entry)
        {
            // Only the AND/OR condition forms emitted by these probes are modeled here.
            var alternatives = Regex.Matches(header, @"\(([^()]*)\)").Select(list =>
                Regex.Matches(list.Groups[1].Value, @"<([^>]+)>|\[([^\]]+)\]").All(condition =>
                    condition.Groups[1].Success ? condition.Groups[1].Value == entry?.Token :
                    fault == "ignore-unknown-dav-etag" ||
                    fault == "ignore-dav-etag" && condition.Groups[2].Value.StartsWith("\"private-etag-", StringComparison.Ordinal) ||
                    condition.Groups[2].Value == entry?.Tag));
            return fault == "and-alternatives" ? alternatives.All(value => value) : alternatives.Any(value => value);
        }

        private string NextTag() => fault == "unchanged-etag" ? "\"private-etag\"" : $"\"private-etag-{++m_version}\"";

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

        private static HttpResponseMessage Reply(int status, byte[]? body = null) =>
            new((HttpStatusCode)status) { Content = new ByteArrayContent(body ?? []) };
    }
}
