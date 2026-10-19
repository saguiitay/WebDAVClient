using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests.ClientTests
{
    /// <summary>
    /// Behavioural tests for <see cref="Client.GetServerOptions"/>
    /// (RFC 4918 §9.1, RFC 9110 §9.3.7) — verifies the request shape on the
    /// wire and the parsing of the <c>DAV</c> and <c>Allow</c> response headers.
    /// </summary>
    [TestClass]
    public class ClientOptionsTests
    {
        private const string Server = "http://example.com";
        private const string BasePath = "/webdav/";

        private static Func<HttpRequestMessage, HttpResponseMessage> Responder(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            return request =>
            {
                Assert.AreEqual(HttpMethod.Options, request.Method, "Capability discovery must not require PROPFIND.");
                return handler(request);
            };
        }

        private static HttpResponseMessage OptionsResponse(string dav, string allow, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(Array.Empty<byte>())
            };
            if (dav != null)
                response.Headers.TryAddWithoutValidation("DAV", dav);
            if (allow != null)
            {
                // Allow lives on Content.Headers per RFC 9110 §10.2.1.
                foreach (var token in allow.Split(','))
                    response.Content.Headers.Allow.Add(token.Trim());
            }
            return response;
        }

        [TestMethod]
        public async Task GetServerOptions_sends_OPTIONS_to_configured_path()
        {
            using var harness = new ClientHarness(Responder(_ => OptionsResponse("1, 2", "OPTIONS, GET, PROPFIND")), Server, BasePath);

            await harness.Client.GetServerOptions();

            var req = harness.Handler.Requests.Last();
            Assert.AreEqual(1, harness.Handler.Requests.Count);
            Assert.AreEqual("OPTIONS", req.Method.Method);
            StringAssert.StartsWith(req.RequestUri.AbsoluteUri, Server + BasePath);
            Assert.IsNull(req.Body, "OPTIONS must not carry a body");
        }

        [TestMethod]
        public async Task GetServerOptions_parses_DAV_compliance_classes()
        {
            using var harness = new ClientHarness(Responder(_ => OptionsResponse("1, 2, 3", "OPTIONS")), Server, BasePath);

            var options = await harness.Client.GetServerOptions();

            Assert.IsTrue(options.IsWebDavServer);
            Assert.IsTrue(options.IsClass1);
            Assert.IsTrue(options.IsClass2);
            Assert.IsTrue(options.IsClass3);
            CollectionAssert.AreEquivalent(new[] { "1", "2", "3" }, options.DavComplianceClasses.ToArray());
            Assert.AreEqual("1, 2, 3", options.RawDavHeader);
        }

        [TestMethod]
        public async Task GetServerOptions_parses_Allow_methods()
        {
            using var harness = new ClientHarness(Responder(_ => OptionsResponse("1", "OPTIONS, GET, HEAD, PUT, DELETE, PROPFIND, PROPPATCH, MKCOL, COPY, MOVE, LOCK, UNLOCK")), Server, BasePath);

            var options = await harness.Client.GetServerOptions();

            Assert.IsTrue(options.SupportsMethod("PROPFIND"));
            Assert.IsTrue(options.SupportsMethod("propfind"), "method match must be case-insensitive");
            Assert.IsTrue(options.SupportsMethod("LOCK"));
            Assert.IsFalse(options.SupportsMethod("PROPPATCH-extension"));
            CollectionAssert.Contains(options.AllowedMethods.ToArray(), "MKCOL");
        }

        [TestMethod]
        public async Task GetServerOptions_recognises_non_numeric_extension_tokens()
        {
            using var harness = new ClientHarness(Responder(_ => OptionsResponse("1, 2, access-control, calendar-access", "OPTIONS")), Server, BasePath);

            var options = await harness.Client.GetServerOptions();

            Assert.IsTrue(options.HasComplianceToken("access-control"));
            Assert.IsTrue(options.HasComplianceToken("CALENDAR-ACCESS"), "compliance token match must be case-insensitive");
            Assert.IsFalse(options.HasComplianceToken("4"));
        }

        [TestMethod]
        public async Task GetServerOptions_handles_missing_DAV_header()
        {
            // Plain HTTP server: no DAV header, just an Allow.
            using var harness = new ClientHarness(Responder(_ => OptionsResponse(null, "OPTIONS, GET, HEAD")), Server, BasePath);

            var options = await harness.Client.GetServerOptions();

            Assert.IsFalse(options.IsWebDavServer, "absence of DAV header must surface as IsWebDavServer == false");
            Assert.AreEqual(0, options.DavComplianceClasses.Count);
            Assert.IsNull(options.RawDavHeader);
            Assert.IsTrue(options.SupportsMethod("GET"));
        }

        [TestMethod]
        public async Task GetServerOptions_accepts_204_NoContent()
        {
            using var harness = new ClientHarness(Responder(_ => OptionsResponse("1", "OPTIONS", HttpStatusCode.NoContent)), Server, BasePath);

            var options = await harness.Client.GetServerOptions();

            Assert.IsTrue(options.IsClass1);
        }

        [TestMethod]
        public async Task GetServerOptions_throws_WebDAVException_on_error_status()
        {
            using var harness = new ClientHarness(Responder(_ => StubHttpMessageHandler.StatusOnly(HttpStatusCode.NotFound)), Server, BasePath);

            try
            {
                await harness.Client.GetServerOptions();
                Assert.Fail("Expected WebDAVException for 404 response");
            }
            catch (WebDAVException ex)
            {
                Assert.AreEqual(404, ex.GetHttpCode());
            }
        }

        [DataTestMethod]
        [DataRow(null, "/webdav/")]
        [DataRow("/", "/webdav/")]
        [DataRow("file.txt", "/webdav/file.txt")]
        [DataRow("/webdav/file.txt", "/webdav/file.txt")]
        [DataRow("sub/", "/webdav/sub/")]
        [DataRow("http://EXAMPLE.com:80/webdav/file.txt", "/webdav/file.txt")]
        public async Task GetServerOptions_preserves_exact_resource_shape_without_discovery(string path, string expected)
        {
            using var harness = new ClientHarness(request => request.Method.Method == "PROPFIND"
                ? StubHttpMessageHandler.StatusOnly(HttpStatusCode.MethodNotAllowed)
                : OptionsResponse("1", "OPTIONS"), Server, BasePath);

            var options = await harness.Client.GetServerOptions(path);

            Assert.IsTrue(options.IsClass1);
            Assert.AreEqual(1, harness.Handler.Requests.Count);
            Assert.AreEqual(HttpMethod.Options, harness.Handler.Requests[0].Method);
            Assert.AreEqual(expected, harness.Handler.Requests[0].RequestUri.AbsolutePath);
        }

        [DataTestMethod]
        [DataRow("http://example.com/webdav/")]
        [DataRow("https://example.com/webdav/")]
        [DataRow("https://other.example:8443/webdav/")]
        public async Task GetServerOptions_rejects_cross_origin_targets_before_IO(string path)
        {
            using var harness = new ClientHarness(_ => throw new AssertFailedException("Must not send cross-origin OPTIONS"),
                "https://example.com", BasePath);
            harness.Client.Port = 8443;

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => harness.Client.GetServerOptions(path));

            Assert.AreEqual(0, harness.Handler.Requests.Count);
        }

        [TestMethod]
        public async Task GetServerOptions_honors_port_override()
        {
            using var harness = new ClientHarness(Responder(_ => OptionsResponse(null, "OPTIONS")), "https://example.com:9443", BasePath);
            harness.Client.Port = 8443;

            await harness.Client.GetServerOptions("file.txt");

            Assert.AreEqual("https://example.com:8443/webdav/file.txt", harness.Handler.Requests.Single().RequestUri.AbsoluteUri);
        }

        [TestMethod]
        public void OptionsHeaderParser_Split_handles_whitespace_and_empty_tokens()
        {
            var tokens = OptionsHeaderParser.Split("  1 , 2 ,, 3  ");
            CollectionAssert.AreEqual(new[] { "1", "2", "3" }, tokens);
        }

        [TestMethod]
        public void OptionsHeaderParser_Split_returns_empty_list_for_null_or_whitespace()
        {
            Assert.AreEqual(0, OptionsHeaderParser.Split(null).Count);
            Assert.AreEqual(0, OptionsHeaderParser.Split("").Count);
            Assert.AreEqual(0, OptionsHeaderParser.Split("   ").Count);
            Assert.AreEqual(0, OptionsHeaderParser.Split(",,,").Count);
        }
    }
}
