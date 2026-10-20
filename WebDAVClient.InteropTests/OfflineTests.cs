using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebDAVClient.InteropTests;

[TestClass]
public sealed class OfflineTests
{
    [DataTestMethod]
    [DataRow(405, true, false, "unsupported")]
    [DataRow(501, true, false, "unsupported")]
    [DataRow(405, false, false, "failed")]
    [DataRow(405, true, true, "failed")]
    [DataRow(501, true, true, "failed")]
    [DataRow(401, true, false, "denied")]
    [DataRow(403, true, false, "denied")]
    [DataRow(407, true, false, "denied")]
    [DataRow(404, true, false, "failed")]
    [DataRow(409, true, false, "failed")]
    [DataRow(423, true, false, "failed")]
    [DataRow(500, true, false, "failed")]
    [DataRow(302, true, false, "redirected")]
    [DataRow(207, true, false, "failed")]
    public void ClassificationsNeverTurnDenialOrAdvertisementIntoUnsupported(int status, bool optional, bool advertised, string expected)
    {
        Assert.AreEqual(expected, Matrix.Classify(status, optional, advertised));
    }

    [TestMethod]
    public void DefaultConfigurationRefusesNetwork()
    {
        Assert.ThrowsException<ArgumentException>(() => Configuration.Read(_ => null));
    }

    [DataTestMethod]
    [DataRow("https://example.test/dav/", null)]
    [DataRow("http://example.test/dav/", "1")]
    [DataRow("http://user:secret@localhost/dav/", null)]
    [DataRow("http://localhost/dav", null)]
    [DataRow("http://localhost/dav/?token=secret", null)]
    [DataRow("http://localhost/dav/#fragment", null)]
    [DataRow("file:///C:/dav/", null)]
    public void UnsafeEndpointsAreRejected(string endpoint, string? allowRemote)
    {
        var env = EnvironmentFor(endpoint);
        env["WEBDAV_INTEROP_ALLOW_REMOTE"] = allowRemote;
        Assert.ThrowsException<ArgumentException>(() => Configuration.Read(k => env.GetValueOrDefault(k)));
    }

    [TestMethod]
    public void ExplicitHttpsRemoteOptInIsAccepted()
    {
        var env = EnvironmentFor("https://example.test/dav/");
        env["WEBDAV_INTEROP_ALLOW_REMOTE"] = "1";
        var config = Configuration.Read(k => env.GetValueOrDefault(k));
        Assert.AreEqual("anonymous", config.Auth);
    }

    [DataTestMethod]
    [DataRow("anonymous", "user", "password")]
    [DataRow("windows", "user", "password")]
    [DataRow("basic", null, null)]
    [DataRow("basic", "user", null)]
    [DataRow("basic", "a:b", "password")]
    [DataRow("default", null, null)]
    public void AuthenticationRequiresUnambiguousOptIn(string auth, string? user, string? password)
    {
        var env = EnvironmentFor("http://localhost/dav/");
        env["WEBDAV_INTEROP_AUTH"] = auth;
        env["WEBDAV_INTEROP_USER"] = user;
        env["WEBDAV_INTEROP_PASSWORD"] = password;
        Assert.ThrowsException<ArgumentException>(() => Configuration.Read(k => env.GetValueOrDefault(k)));
    }

    [TestMethod]
    public void MatrixRequiresObservationsAndRejectsBlockedOrFailedRuns()
    {
        var matrix = new Matrix
        {
            Server = "test",
            ServerVersion = "1",
            Authentication = "anonymous",
            Transport = "http"
        };
        Assert.IsFalse(matrix.Succeeded);
        matrix.Observations.Add(new Observation("optional", "unsupported", 405));
        matrix.Observations.Add(new Observation("discovery", "unavailable-property", 207, PropertyStatuses: [404]));
        Assert.IsFalse(matrix.Succeeded);
        foreach (string operation in Matrix.RequiredOperations)
        {
            matrix.Observations.Add(new Observation(operation, "passed"));
        }
        Assert.IsTrue(matrix.Succeeded);
        matrix.Observations.Add(new Observation("cleanup", "blocked"));
        Assert.IsFalse(matrix.Succeeded);
    }

    [DataTestMethod]
    [DataRow(403)]
    [DataRow(405)]
    [DataRow(500)]
    public async Task UnconfirmedCollectionIsNeverDeleted(int mkcolStatus)
    {
        using var handler = new SequenceHandler(200, 404, mkcolStatus);
        var config = Configuration.Read(k => EnvironmentFor("http://localhost/dav/").GetValueOrDefault(k));
        var result = await new CompatibilitySuite(config, () => new System.Net.Http.HttpClient(handler)).RunAsync().ConfigureAwait(false);
        Assert.IsFalse(result.Succeeded);
        CollectionAssert.AreEqual(new[] { "OPTIONS", "HEAD", "MKCOL" }, handler.Methods.ToArray());
        Assert.IsFalse(result.Observations.Any(o => o.Operation == "cleanup-tree"));
    }

    [TestMethod]
    public async Task UploadFailureStillCleansOnlyConfirmedRunTree()
    {
        using var handler = new SequenceHandler(200, 404, 201, 403, 204, 404);
        var config = Configuration.Read(k => EnvironmentFor("http://localhost/dav/").GetValueOrDefault(k));
        var result = await new CompatibilitySuite(config, () => new System.Net.Http.HttpClient(handler)).RunAsync().ConfigureAwait(false);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("denied", result.Observations.Single(o => o.Operation == "unicode-put").Outcome);
        Assert.AreEqual("passed", result.Observations.Single(o => o.Operation == "cleanup-tree").Outcome);
        CollectionAssert.AreEqual(new[] { "OPTIONS", "HEAD", "MKCOL", "PUT", "DELETE", "HEAD" }, handler.Methods.ToArray());
        Assert.AreEqual("/dav/" + result.RunTree + "/", handler.Paths[4]);
    }

    [TestMethod]
    public async Task PreflightCollisionNeverMutatesExistingTree()
    {
        using var handler = new SequenceHandler(200, 200);
        var config = Configuration.Read(k => EnvironmentFor("http://localhost/dav/").GetValueOrDefault(k));
        var result = await new CompatibilitySuite(config, () => new System.Net.Http.HttpClient(handler)).RunAsync().ConfigureAwait(false);
        Assert.IsFalse(result.Succeeded);
        CollectionAssert.AreEqual(new[] { "OPTIONS", "HEAD" }, handler.Methods.ToArray());
    }

    [TestMethod]
    public async Task CleanupRejectionCannotBeReportedAsSuccess()
    {
        using var handler = new SequenceHandler(200, 404, 201, 403, 500);
        var config = Configuration.Read(k => EnvironmentFor("http://localhost/dav/").GetValueOrDefault(k));
        var result = await new CompatibilitySuite(config, () => new System.Net.Http.HttpClient(handler)).RunAsync().ConfigureAwait(false);
        Assert.AreEqual("failed", result.Observations.Single(o => o.Operation == "cleanup-tree").Outcome);
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public void ProtocolFailureCodesExposeKnownCausesButNeverArbitraryMessages()
    {
        var known = new WebDAVClient.Helpers.WebDAVException(207, "Malformed DAV resource response.",
            new System.Xml.XmlException("A DAV:propstat requires one prop and one status."));
        Assert.AreEqual("malformed-resource:propstat-shape", CompatibilitySuite.DescribeFailure(known));
        var sensitive = new IOException("https://user:secret@example.test/private",
            new System.Xml.XmlException("token=private-lock-token"));
        Assert.AreEqual("IOException:XmlException", CompatibilitySuite.DescribeFailure(sensitive));
        Assert.AreEqual("invalid-resource-href", CompatibilitySuite.DescribeFailure(
            new System.Xml.XmlException("A DAV resource href requires an HTTP(S) URI or an absolute path.")));
        Assert.AreEqual("invalid-href-character", CompatibilitySuite.DescribeFailure(
            new System.Xml.XmlException("Invalid character in DAV href path or query.")));
    }

    [TestMethod]
    public void LockWireDeviationRequiresExactlyOneMatchingTokenAndNeverReturnsTheToken()
    {
        const string token = "opaquelocktoken:private-fixture-token";
        Assert.AreEqual("unbracketed-lock-token-confirmed", CompatibilitySuite.LockWireDeviation(token, [token]));
        Assert.AreEqual("unbracketed-lock-token-confirmed", CompatibilitySuite.LockWireDeviation(" \t" + token + "\t ", [token]));
        Assert.IsNull(CompatibilitySuite.LockWireDeviation("<" + token + ">", [token]));
        Assert.IsNull(CompatibilitySuite.LockWireDeviation(token, []));
        Assert.IsNull(CompatibilitySuite.LockWireDeviation(token, [token, token]));
        Assert.IsNull(CompatibilitySuite.LockWireDeviation(token, ["opaquelocktoken:different"]));
        Assert.IsNull(CompatibilitySuite.LockWireDeviation("relative", ["relative"]));
        Assert.IsNull(CompatibilitySuite.LockWireDeviation("opaquelocktoken:bad token", ["opaquelocktoken:bad token"]));
        Assert.IsNull(CompatibilitySuite.LockWireDeviation(null, [token]));
    }

    private sealed class SequenceHandler(params int[] statuses) : HttpMessageHandler
    {
        private int m_index;

        internal List<string> Methods
        {
            get;
        } = [];

        internal List<string> Paths
        {
            get;
        } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method.Method);
            Paths.Add(request.RequestUri!.AbsolutePath);
            Assert.IsTrue(m_index < statuses.Length, "Unexpected request");
            return Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)statuses[m_index++])
            {
                Content = new StringContent(""),
                RequestMessage = request
            });
        }
    }

    private static Dictionary<string, string?> EnvironmentFor(string endpoint) => new()
    {
        ["WEBDAV_INTEROP_RUN"] = "1",
        ["WEBDAV_INTEROP_ENDPOINT"] = endpoint,
        ["WEBDAV_INTEROP_SERVER"] = "test",
        ["WEBDAV_INTEROP_VERSION"] = "1.0"
    };
}
