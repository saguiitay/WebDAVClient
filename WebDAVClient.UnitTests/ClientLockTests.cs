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
    /// Behavioural tests for the LOCK / UNLOCK / refresh-lock surface added per RFC 4918
    /// §9.10–9.11. They exercise both the request shape (method, URL, headers, body) and the
    /// response parsing (LockInfo population, error mapping).
    /// </summary>
    [TestClass]
    public class ClientLockTests
    {
        private const string Server = "http://example.com";
        private const string BasePath = "/webdav/";

        // Mirrors the convention used by ClientTests: the very first request from a freshly-
        // constructed Client is the base-path PROPFIND that resolves m_encodedBasePath. We
        // intercept it here so individual tests only see the LOCK/UNLOCK call.
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

        private static HttpResponseMessage LockOk(string body, string lockTokenHeader)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/xml")
            };
            if (lockTokenHeader != null)
            {
                response.Headers.Add("Lock-Token", lockTokenHeader);
            }
            return response;
        }

        // -------------------- LockFile --------------------

        [TestMethod]
        public async Task LockFile_sends_LOCK_with_depth_0_timeout_and_lockinfo_body()
        {
            using var harness = new ClientHarness(Responder(req =>
                LockOk(WebDAVResponses.LockResponse(), "<opaquelocktoken:abc-123>")), Server, BasePath);

            var info = await harness.Client.LockFile("file.txt", timeoutSeconds: 300);

            var lockRequest = harness.Handler.Requests.Last();
            Assert.AreEqual("LOCK", lockRequest.Method.Method);
            Assert.AreEqual("/webdav/file.txt", lockRequest.RequestUri.AbsolutePath);
            Assert.AreEqual("0", lockRequest.Headers["Depth"][0]);
            Assert.AreEqual("Second-300", lockRequest.Headers["Timeout"][0]);

            var body = lockRequest.BodyAsString();
            StringAssert.Contains(body, "<D:lockinfo");
            StringAssert.Contains(body, "<D:exclusive/>");
            StringAssert.Contains(body, "<D:write/>");
            StringAssert.Contains(body, "<D:owner>WebDAVClient</D:owner>");

            // Token comes from the Lock-Token header (canonical per RFC 4918 §10.5),
            // bare form (no angle brackets).
            Assert.AreEqual("opaquelocktoken:abc-123", info.Token);
        }

        [TestMethod]
        public async Task LockFile_populates_LockInfo_from_response_body()
        {
            using var harness = new ClientHarness(Responder(_ =>
                LockOk(WebDAVResponses.LockResponse(
                    token: "opaquelocktoken:in-body-token",
                    lockRoot: "/webdav/file.txt",
                    depth: "0",
                    scope: "exclusive",
                    timeout: "Second-3600",
                    ownerInnerXml: "<D:href>http://example.org/me</D:href>"),
                lockTokenHeader: null)), Server, BasePath);

            var info = await harness.Client.LockFile("file.txt");

            // No Lock-Token header → falls back to body parsing.
            Assert.AreEqual("opaquelocktoken:in-body-token", info.Token);
            Assert.AreEqual("write", info.LockType);
            Assert.AreEqual("exclusive", info.LockScope);
            Assert.AreEqual("0", info.Depth);
            Assert.AreEqual(3600, info.TimeoutSeconds);
            Assert.AreEqual("/webdav/file.txt", info.LockRoot);
            StringAssert.Contains(info.Owner, "http://example.org/me");
        }

        [TestMethod]
        public async Task LockFile_uses_custom_owner_in_request_body()
        {
            using var harness = new ClientHarness(Responder(_ =>
                LockOk(WebDAVResponses.LockResponse(), "<opaquelocktoken:t>")), Server, BasePath);

            // Special characters must be XML-escaped — no raw <, > or & allowed in the body.
            await harness.Client.LockFile("file.txt", owner: "alice <a&b>");

            var body = harness.Handler.Requests.Last().BodyAsString();
            StringAssert.Contains(body, "<D:owner>alice &lt;a&amp;b&gt;</D:owner>");
        }

        [TestMethod]
        public async Task LockFile_rejects_non_positive_timeout()
        {
            using var harness = new ClientHarness(Responder(_ =>
                LockOk(WebDAVResponses.LockResponse(), "<opaquelocktoken:t>")), Server, BasePath);

            await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(
                () => harness.Client.LockFile("file.txt", timeoutSeconds: 0));
        }

        [TestMethod]
        public async Task LockFile_throws_WebDAVException_on_423_Locked()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly((HttpStatusCode)423, "<error/>")), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => harness.Client.LockFile("file.txt"));
            Assert.AreEqual(423, ex.GetHttpCode());
        }

        [TestMethod]
        public async Task LockFolder_sends_LOCK_with_depth_infinity_and_trailing_slash()
        {
            using var harness = new ClientHarness(Responder(_ =>
                LockOk(WebDAVResponses.LockResponse(depth: "infinity"), "<opaquelocktoken:f>")), Server, BasePath);

            await harness.Client.LockFolder("folder");

            var lockRequest = harness.Handler.Requests.Last();
            Assert.AreEqual("LOCK", lockRequest.Method.Method);
            Assert.IsTrue(lockRequest.RequestUri.AbsolutePath.EndsWith("/"),
                "Folder lock URL should have a trailing slash, was: " + lockRequest.RequestUri.AbsolutePath);
            Assert.AreEqual("infinity", lockRequest.Headers["Depth"][0]);
        }

        [TestMethod]
        public async Task LockFolder_treats_207_MultiStatus_as_failure()
        {
            // RFC 4918 §9.10.6: 207 from LOCK indicates partial failure. Must NOT be reported as
            // a granted lock — that would let callers issue writes on a resource the server
            // didn't actually lock for them.
            using var harness = new ClientHarness(Responder(_ => new HttpResponseMessage((HttpStatusCode)207)
            {
                Content = new StringContent("<multistatus xmlns=\"DAV:\"/>", Encoding.UTF8, "application/xml")
            }), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => harness.Client.LockFolder("folder"));
            Assert.AreEqual(207, ex.GetHttpCode());
        }

        [TestMethod]
        public async Task LockFile_throws_when_response_lacks_token_in_both_header_and_body()
        {
            // Server responds 200 OK but with neither a Lock-Token header nor a parseable body.
            // We must not return a LockInfo with a null Token — callers couldn't unlock it.
            using var harness = new ClientHarness(Responder(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<prop xmlns=\"DAV:\"></prop>", Encoding.UTF8, "application/xml")
            }), Server, BasePath);

            await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => harness.Client.LockFile("file.txt"));
        }

        // -------------------- UnlockFile / UnlockFolder --------------------

        [TestMethod]
        public async Task UnlockFile_sends_UNLOCK_with_bracketed_LockToken_header()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.NoContent)), Server, BasePath);

            await harness.Client.UnlockFile("file.txt", "opaquelocktoken:abc-123");

            var unlockRequest = harness.Handler.Requests.Last();
            Assert.AreEqual("UNLOCK", unlockRequest.Method.Method);
            Assert.AreEqual("<opaquelocktoken:abc-123>", unlockRequest.Headers["Lock-Token"][0]);
            Assert.IsNull(unlockRequest.Body, "UNLOCK must not send a body");
        }

        [TestMethod]
        public async Task UnlockFile_accepts_bracketed_caller_token_and_normalizes_it()
        {
            // Real users copy the Lock-Token value out of response headers including the angle
            // brackets. The client must accept that form and not emit "<<token>>".
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.NoContent)), Server, BasePath);

            await harness.Client.UnlockFile("file.txt", "<opaquelocktoken:abc-123>");

            var unlockRequest = harness.Handler.Requests.Last();
            Assert.AreEqual("<opaquelocktoken:abc-123>", unlockRequest.Headers["Lock-Token"][0]);
        }

        [TestMethod]
        public async Task UnlockFile_rejects_null_or_malformed_token()
        {
            using var harness = new ClientHarness(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.Root(BasePath)), Server, BasePath);

            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.UnlockFile("file.txt", lockToken: null));
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.UnlockFile("file.txt", lockToken: ""));
            // CR/LF in the token would otherwise allow header injection in Lock-Token.
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.UnlockFile("file.txt", lockToken: "tok\r\nX-Injected: yes"));
        }

        [TestMethod]
        public async Task UnlockFile_throws_WebDAVException_on_non_204()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.Conflict)), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => harness.Client.UnlockFile("file.txt", "opaquelocktoken:t"));
            Assert.AreEqual(409, ex.GetHttpCode());
        }

        [TestMethod]
        public async Task UnlockFolder_sends_UNLOCK_to_trailing_slash_url()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.NoContent)), Server, BasePath);

            await harness.Client.UnlockFolder("folder", "opaquelocktoken:t");

            var req = harness.Handler.Requests.Last();
            Assert.IsTrue(req.RequestUri.AbsolutePath.EndsWith("/"));
        }

        // -------------------- RefreshLock --------------------

        [TestMethod]
        public async Task RefreshLock_sends_LOCK_with_If_header_no_body_and_new_timeout()
        {
            using var harness = new ClientHarness(Responder(_ =>
                LockOk(WebDAVResponses.LockResponse(token: "opaquelocktoken:t", timeout: "Second-1200"),
                       "<opaquelocktoken:t>")), Server, BasePath);

            var info = await harness.Client.RefreshLock("file.txt", "opaquelocktoken:t", timeoutSeconds: 1200);

            var req = harness.Handler.Requests.Last();
            Assert.AreEqual("LOCK", req.Method.Method);
            Assert.IsNull(req.Body, "Refresh LOCK must have no request body");
            Assert.AreEqual("(<opaquelocktoken:t>)", req.Headers["If"][0]);
            Assert.AreEqual("Second-1200", req.Headers["Timeout"][0]);
            Assert.AreEqual(1200, info.TimeoutSeconds);
            Assert.AreEqual("opaquelocktoken:t", info.Token);
        }

        [TestMethod]
        public async Task RefreshLock_falls_back_to_caller_token_when_response_body_is_empty()
        {
            // Some servers return 200 OK without an activelock body on refresh. The returned
            // LockInfo must still carry the original token so callers can keep refreshing.
            using var harness = new ClientHarness(Responder(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("", Encoding.UTF8, "application/xml")
            }), Server, BasePath);

            var info = await harness.Client.RefreshLock("file.txt", "opaquelocktoken:keepme");
            Assert.AreEqual("opaquelocktoken:keepme", info.Token);
        }

        [TestMethod]
        public async Task RefreshLock_throws_on_412_PreconditionFailed()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.PreconditionFailed)), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => harness.Client.RefreshLock("file.txt", "opaquelocktoken:stale"));
            Assert.AreEqual(412, ex.GetHttpCode());
        }

        // -------------------- LockResponseParser direct tests --------------------

        [TestMethod]
        public void LockResponseParser_returns_null_when_no_activelock_present()
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes("<prop xmlns=\"DAV:\"></prop>"));
            Assert.IsNull(LockResponseParser.Parse(stream));
        }

        [TestMethod]
        public void LockResponseParser_handles_Infinite_timeout()
        {
            Assert.IsNull(LockResponseParser.ParseTimeout("Infinite"));
            Assert.AreEqual(120, LockResponseParser.ParseTimeout("Second-120"));
            Assert.AreEqual(60, LockResponseParser.ParseTimeout("Second-60, Infinite"));
            Assert.IsNull(LockResponseParser.ParseTimeout("garbage"));
            Assert.IsNull(LockResponseParser.ParseTimeout(""));
            Assert.IsNull(LockResponseParser.ParseTimeout(null));
        }
    }
}
