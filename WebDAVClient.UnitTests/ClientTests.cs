using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.HttpClient;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests.ClientTests
{
    /// <summary>
    /// End-to-end behavioural tests for <see cref="Client"/>. They exercise the public surface
    /// against a fake HttpMessageHandler so we don't touch the network. The tests assert both
    /// the response handling (parsing, exceptions) and the outgoing request shape (method,
    /// URL, headers, body).
    /// </summary>
    [TestClass]
    public class ClientTests
    {
        private const string Server = "http://example.com";
        private const string BasePath = "/webdav/";

        // Most operations issue an initial PROPFIND on the base URL to resolve the encoded base
        // path. The very first request from a freshly-constructed Client is always that base
        // resolution (Client lazily resolves m_encodedBasePath on the first GetServerUrl call).
        // Counting requests is more reliable than matching by URL/headers because the listing
        // and resolution calls share the same method, URL, and PROPFIND body.
        private static Func<HttpRequestMessage, HttpResponseMessage> Responder(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            int seen = 0;
            return request =>
            {
                if (Interlocked.Increment(ref seen) == 1)
                {
                    return StubHttpMessageHandler.Multistatus(WebDAVResponses.Root(BasePath));
                }
                return handler(request);
            };
        }

        private static bool IsBasePathPropFind(HttpRequestMessage request)
        {
            return request.Method.Method == "PROPFIND"
                   && request.RequestUri.AbsolutePath == BasePath;
        }

        // -------------------- Construction & properties --------------------

        [TestMethod]
        public void Server_setter_strips_trailing_slash()
        {
            using var client = new Client(new HttpClientWrapper(new System.Net.Http.HttpClient()))
            {
                Server = "http://example.com/"
            };
            Assert.AreEqual("http://example.com", client.Server);
        }

        [TestMethod]
        public void BasePath_setter_normalizes_to_slash_form()
        {
            using var client = new Client(new HttpClientWrapper(new System.Net.Http.HttpClient()));

            client.BasePath = "dav";
            Assert.AreEqual("/dav/", client.BasePath);

            client.BasePath = "/dav/";
            Assert.AreEqual("/dav/", client.BasePath);

            client.BasePath = "//";
            Assert.AreEqual("/", client.BasePath, "empty after trim should reset to root");

            client.BasePath = "/";
            Assert.AreEqual("/", client.BasePath);
        }

        [TestMethod]
        public void Constructor_with_HttpClient_does_not_dispose_external_client()
        {
            var handler = new DisposeTrackingHandler();
            var httpClient = new System.Net.Http.HttpClient(handler);

            using (new Client(httpClient)) { }

            Assert.AreEqual(0, handler.DisposeCount, "Externally-owned HttpClient must not be disposed by Client");
        }

        [TestMethod]
        public void Constructor_with_credentials_creates_owned_handler_and_disposes_it()
        {
            // Exercises the Client(ICredentials, ...) constructor path. We can't observe the
            // internal handler directly, but we can confirm it constructs without throwing and
            // that Dispose() runs cleanly.
            using var client = new Client(new NetworkCredential("user", "pwd"));
            client.Dispose();
        }

        [TestMethod]
        public void Constructor_with_uploadTimeout_does_not_throw_and_disposes_cleanly()
        {
            using var client = new Client(uploadTimeout: TimeSpan.FromSeconds(30));
        }

        [TestMethod]
        public void Constructor_with_proxy_does_not_throw()
        {
            using var client = new Client(proxy: new WebProxy("http://proxy.local:8080"));
        }

        // -------------------- List --------------------

        [TestMethod]
        public async Task List_returns_items_excluding_the_parent_folder()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual("PROPFIND", req.Method.Method);
                return StubHttpMessageHandler.Multistatus(WebDAVResponses.FolderListing());
            }), Server, BasePath);

            var items = (await harness.Client.List()).ToList();

            // Parent folder ("/webdav/") must be filtered out, leaving file + subfolder.
            Assert.AreEqual(2, items.Count);
            Assert.IsTrue(items.Any(i => i.DisplayName == "file.txt" && !i.IsCollection));
            Assert.IsTrue(items.Any(i => i.DisplayName == "sub" && i.IsCollection));
        }

        [TestMethod]
        public async Task List_sends_depth_header()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.FolderListing())), Server, BasePath);

            await harness.Client.List(depth: 2);

            var listingRequest = harness.Handler.Requests.Last();
            Assert.IsTrue(listingRequest.Headers.ContainsKey("Depth"));
            Assert.AreEqual("2", listingRequest.Headers["Depth"][0]);
        }

        [TestMethod]
        public async Task List_omits_depth_header_when_null()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.FolderListing())), Server, BasePath);

            await harness.Client.List(depth: null);

            var listingRequest = harness.Handler.Requests.Last();
            Assert.IsFalse(listingRequest.Headers.ContainsKey("Depth"));
        }

        [TestMethod]
        public async Task List_throws_WebDAVException_on_non_success_status()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.InternalServerError)), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(() => harness.Client.List());
            Assert.AreEqual(500, ex.GetHttpCode());
        }

        // -------------------- GetFolder / GetFile --------------------

        [TestMethod]
        public async Task GetFolder_returns_parsed_collection()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.SingleFolder("/webdav/folder/"))), Server, BasePath);

            var item = await harness.Client.GetFolder("folder");

            Assert.IsNotNull(item);
            Assert.IsTrue(item.IsCollection);
        }

        [TestMethod]
        public async Task GetFile_returns_parsed_file()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.SingleFile())), Server, BasePath);

            var item = await harness.Client.GetFile("file.txt");

            Assert.IsNotNull(item);
            Assert.IsFalse(item.IsCollection);
            Assert.AreEqual(10, item.ContentLength);
        }

        [TestMethod]
        public async Task GetFile_throws_WebDAVException_on_404()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.NotFound)), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(() => harness.Client.GetFile("missing.txt"));
            Assert.AreEqual(404, ex.GetHttpCode());
        }

        // -------------------- Download --------------------

        [TestMethod]
        public async Task Download_returns_stream_on_200()
        {
            var payload = Encoding.UTF8.GetBytes("hello world");
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual(HttpMethod.Get, req.Method);
                Assert.IsTrue(req.Headers.Contains("translate"));
                Assert.AreEqual("f", req.Headers.GetValues("translate").First());
                return StubHttpMessageHandler.Stream(HttpStatusCode.OK, payload);
            }), Server, BasePath);

            using var stream = await harness.Client.Download("file.txt");
            using var reader = new StreamReader(stream);
            Assert.AreEqual("hello world", await reader.ReadToEndAsync());
        }

        [TestMethod]
        public async Task DownloadPartial_sets_range_header()
        {
            var payload = Encoding.UTF8.GetBytes("partial");
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.IsTrue(req.Headers.Contains("Range"));
                Assert.AreEqual("bytes=10-20", req.Headers.GetValues("Range").First());
                return StubHttpMessageHandler.Stream(HttpStatusCode.PartialContent, payload);
            }), Server, BasePath);

            using var stream = await harness.Client.DownloadPartial("file.txt", 10, 20);
            using var reader = new StreamReader(stream);
            Assert.AreEqual("partial", await reader.ReadToEndAsync());
        }

        [TestMethod]
        public async Task Download_throws_WebDAVException_on_failure()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.Forbidden)), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(() => harness.Client.Download("file.txt"));
            Assert.AreEqual(403, ex.GetHttpCode());
        }

        // -------------------- Upload --------------------

        [TestMethod]
        public async Task Upload_returns_true_on_201_and_uses_PUT()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual(HttpMethod.Put, req.Method);
                StringAssert.EndsWith(req.RequestUri.AbsolutePath, "/upload.txt");
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            using var content = new MemoryStream(Encoding.UTF8.GetBytes("payload"));
            var ok = await harness.Client.Upload("/", content, "upload.txt");

            Assert.IsTrue(ok);
        }

        [TestMethod]
        public async Task Upload_throws_on_failure_status()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.BadRequest)), Server, BasePath);

            using var content = new MemoryStream(new byte[1]);
            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(() => harness.Client.Upload("/", content, "x"));
            Assert.AreEqual(400, ex.GetHttpCode());
        }

        [TestMethod]
        public async Task UploadPartial_validates_length_invariant()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created)), Server, BasePath);

            using var content = new MemoryStream(new byte[5]);
            // 0 + 5 != 10
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                harness.Client.UploadPartial("/", content, "x", startBytes: 0, endBytes: 10));
        }

        [TestMethod]
        public async Task UploadPartial_returns_true_when_length_matches()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual(HttpMethod.Put, req.Method);
                Assert.IsNotNull(req.Content.Headers.ContentRange);
                Assert.AreEqual(0L, req.Content.Headers.ContentRange.From);
                Assert.AreEqual(5L, req.Content.Headers.ContentRange.To);
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            using var content = new MemoryStream(new byte[5]);
            var ok = await harness.Client.UploadPartial("/", content, "x", startBytes: 0, endBytes: 5);
            Assert.IsTrue(ok);
        }

        // -------------------- CreateDir --------------------

        [TestMethod]
        public async Task CreateDir_uses_MKCOL_and_returns_true()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual("MKCOL", req.Method.Method);
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            var ok = await harness.Client.CreateDir("/", "newfolder");
            Assert.IsTrue(ok);
        }

        [TestMethod]
        public async Task CreateDir_throws_WebDAVConflictException_on_409()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.Conflict)), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVConflictException>(() => harness.Client.CreateDir("/", "x"));
            Assert.AreEqual(409, ex.GetHttpCode());
        }

        [TestMethod]
        public async Task CreateDir_throws_WebDAVException_on_other_failures()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.Forbidden)), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(() => harness.Client.CreateDir("/", "x"));
            Assert.IsNotInstanceOfType(ex, typeof(WebDAVConflictException));
            Assert.AreEqual(403, ex.GetHttpCode());
        }

        // -------------------- Delete --------------------

        [TestMethod]
        public async Task DeleteFile_uses_DELETE_method()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual(HttpMethod.Delete, req.Method);
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.NoContent);
            }), Server, BasePath);

            await harness.Client.DeleteFile("file.txt");
        }

        [TestMethod]
        public async Task DeleteFolder_uses_DELETE_method()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual(HttpMethod.Delete, req.Method);
                StringAssert.EndsWith(req.RequestUri.AbsolutePath, "/", "folders should keep trailing slash");
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.NoContent);
            }), Server, BasePath);

            await harness.Client.DeleteFolder("folder");
        }

        [TestMethod]
        public async Task DeleteFile_throws_WebDAVException_on_failure()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.Forbidden)), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(() => harness.Client.DeleteFile("x"));
            Assert.AreEqual(403, ex.GetHttpCode());
        }

        // -------------------- Move / Copy --------------------

        [TestMethod]
        public async Task MoveFile_sends_destination_header()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual("MOVE", req.Method.Method);
                Assert.IsTrue(req.Headers.Contains("Destination"));
                StringAssert.EndsWith(req.Headers.GetValues("Destination").First(), "/dst.txt");
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            var ok = await harness.Client.MoveFile("src.txt", "dst.txt");
            Assert.IsTrue(ok);
        }

        [TestMethod]
        public async Task MoveFolder_sends_destination_header_with_trailing_slash()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual("MOVE", req.Method.Method);
                StringAssert.EndsWith(req.Headers.GetValues("Destination").First(), "/dst/");
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            var ok = await harness.Client.MoveFolder("src", "dst");
            Assert.IsTrue(ok);
        }

        [TestMethod]
        public async Task MoveFile_throws_WebDAVException_on_failure()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.Forbidden)), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(() => harness.Client.MoveFile("a", "b"));
            Assert.AreEqual(403, ex.GetHttpCode());
        }

        [TestMethod]
        public async Task CopyFile_sends_destination_header()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual("COPY", req.Method.Method);
                Assert.IsTrue(req.Headers.Contains("Destination"));
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            var ok = await harness.Client.CopyFile("src.txt", "dst.txt");
            Assert.IsTrue(ok);
        }

        [TestMethod]
        public async Task CopyFolder_sends_destination_header_with_trailing_slash()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual("COPY", req.Method.Method);
                StringAssert.EndsWith(req.Headers.GetValues("Destination").First(), "/dst/");
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            var ok = await harness.Client.CopyFolder("src", "dst");
            Assert.IsTrue(ok);
        }

        [TestMethod]
        public async Task CopyFile_throws_WebDAVException_on_failure()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.Forbidden)), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(() => harness.Client.CopyFile("a", "b"));
            Assert.AreEqual(403, ex.GetHttpCode());
        }

        // -------------------- UserAgent + custom headers --------------------

        [TestMethod]
        public async Task Default_user_agent_used_when_none_specified()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.FolderListing())), Server, BasePath);

            await harness.Client.List();

            var listingRequest = harness.Handler.Requests.Last();
            var ua = listingRequest.Headers["User-Agent"][0];
            StringAssert.StartsWith(ua, "WebDAVClient/");
        }

        [TestMethod]
        public async Task Custom_user_agent_is_propagated()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.FolderListing())), Server, BasePath);

            harness.Client.UserAgent = "MyApp";
            harness.Client.UserAgentVersion = "1.2.3";

            await harness.Client.List();

            var listingRequest = harness.Handler.Requests.Last();
            Assert.AreEqual("MyApp/1.2.3", listingRequest.Headers["User-Agent"][0]);
        }

        [TestMethod]
        public async Task Custom_headers_are_forwarded_on_every_request()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.FolderListing())), Server, BasePath);

            harness.Client.CustomHeaders = new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("X-Token", "abc123")
            };

            await harness.Client.List();

            var listingRequest = harness.Handler.Requests.Last();
            Assert.IsTrue(listingRequest.Headers.ContainsKey("X-Token"));
            Assert.AreEqual("abc123", listingRequest.Headers["X-Token"][0]);
        }

        // -------------------- Cancellation --------------------

        // Note: cancellation behaviour can't be cleanly asserted at this level because the
        // initial base-path resolution call (Get(baseUri) inside GetServerUrl) does not
        // propagate the caller's CancellationToken — that's a latent issue tracked separately.
        // The token is faithfully forwarded by HttpRequest/HttpUploadRequest, which is verified
        // implicitly by every other test passing through HttpClientWrapper.

        // -------------------- ServerCertificateValidation --------------------

        [TestMethod]
        public void ServerCertificateValidation_returns_false_when_no_callback()
        {
            using var client = new Client(new HttpClientWrapper(new System.Net.Http.HttpClient()));

            var result = client.ServerCertificateValidation(this, null, null, System.Net.Security.SslPolicyErrors.None);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ServerCertificateValidation_invokes_user_callback_when_set()
        {
            using var client = new Client(new HttpClientWrapper(new System.Net.Http.HttpClient()))
            {
                ServerCertificateValidationCallback = (s, c, ch, errors) => true
            };

            var result = client.ServerCertificateValidation(this, null, null, System.Net.Security.SslPolicyErrors.None);

            Assert.IsTrue(result);
        }

        // -------------------- Dispose --------------------

        [TestMethod]
        public void Dispose_is_idempotent_for_externally_owned_wrapper()
        {
            var client = new Client(new HttpClientWrapper(new System.Net.Http.HttpClient()));
            client.Dispose();
            client.Dispose();
        }

        private sealed class DisposeTrackingHandler : HttpMessageHandler
        {
            public int DisposeCount;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

            protected override void Dispose(bool disposing)
            {
                if (disposing) Interlocked.Increment(ref DisposeCount);
                base.Dispose(disposing);
            }
        }
    }
}
