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
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests.ClientTests
{
    /// <summary>
    /// Behavioural tests for the <c>If</c> lock-token header that PUT / DELETE / MOVE / COPY
    /// must send when operating on locked resources (RFC 4918 §10.4).
    /// </summary>
    [TestClass]
    public class ClientIfHeaderTests
    {
        private const string Server = "http://example.com";
        private const string BasePath = "/webdav/";

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

        private static string GetIfHeader(HttpRequestMessage req)
        {
            Assert.IsTrue(req.Headers.Contains("If"), "Expected an If lock-token header on a locked-resource request.");
            return req.Headers.GetValues("If").Single();
        }

        // -------------------- No-op (no token supplied) --------------------

        [TestMethod]
        public async Task Upload_does_not_send_If_header_when_no_lock_token()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.IsFalse(req.Headers.Contains("If"), "If header must be omitted when no lock token is supplied.");
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            using var content = new MemoryStream(Encoding.UTF8.GetBytes("x"));
            await harness.Client.Upload("/", content, "f.txt");
        }

        [TestMethod]
        public async Task DeleteFile_does_not_send_If_header_when_no_lock_token()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.IsFalse(req.Headers.Contains("If"));
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.NoContent);
            }), Server, BasePath);

            await harness.Client.DeleteFile("file.txt");
        }

        // -------------------- PUT (Upload) --------------------

        [TestMethod]
        public async Task Upload_sends_If_header_with_no_tag_list_form_when_lock_token_supplied()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                // RFC 4918 §10.4.6 No-tag-list — applies to the request URI (the file being PUT).
                Assert.AreEqual("(<opaquelocktoken:abc-123>)", GetIfHeader(req));
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            using var content = new MemoryStream(Encoding.UTF8.GetBytes("x"));
            await harness.Client.Upload("/", content, "f.txt", lockToken: "opaquelocktoken:abc-123");
        }

        [TestMethod]
        public async Task Upload_accepts_angle_bracket_wrapped_lock_token()
        {
            // Tokens are commonly copy-pasted from response headers including the brackets.
            // NormalizeLockToken strips them; the emitted If header must still be well-formed.
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual("(<opaquelocktoken:abc-123>)", GetIfHeader(req));
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            using var content = new MemoryStream(Encoding.UTF8.GetBytes("x"));
            await harness.Client.Upload("/", content, "f.txt", lockToken: "<opaquelocktoken:abc-123>");
        }

        [TestMethod]
        public async Task UploadPartial_sends_If_header_when_lock_token_supplied()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual("(<opaquelocktoken:zzz>)", GetIfHeader(req));
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            using var content = new MemoryStream(new byte[3]);
            await harness.Client.UploadPartial("/", content, "f.txt", startBytes: 0, endBytes: 3,
                lockToken: "opaquelocktoken:zzz");
        }

        // -------------------- DELETE --------------------

        [TestMethod]
        public async Task DeleteFile_sends_If_header_with_no_tag_list_form()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual("(<opaquelocktoken:abc-123>)", GetIfHeader(req));
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.NoContent);
            }), Server, BasePath);

            await harness.Client.DeleteFile("file.txt", lockToken: "opaquelocktoken:abc-123");
        }

        [TestMethod]
        public async Task DeleteFolder_sends_If_header_with_no_tag_list_form()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.AreEqual("(<opaquelocktoken:abc-123>)", GetIfHeader(req));
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.NoContent);
            }), Server, BasePath);

            await harness.Client.DeleteFolder("folder", lockToken: "opaquelocktoken:abc-123");
        }

        // -------------------- MOVE --------------------

        [TestMethod]
        public async Task MoveFile_sends_no_tag_If_header_when_only_source_locked()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                // Only source token → no-tag-list (applies to request URI = source).
                Assert.AreEqual("(<opaquelocktoken:src-1>)", GetIfHeader(req));
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            await harness.Client.MoveFile("src.txt", "dst.txt", sourceLockToken: "opaquelocktoken:src-1");
        }

        [TestMethod]
        public async Task MoveFile_sends_tagged_If_header_when_only_destination_locked()
        {
            // Destination locked but source unlocked → tagged-list pinned to the destination URI
            // (no-tag-list would tag the request URI = source, which is the wrong target).
            using var harness = new ClientHarness(Responder(req =>
            {
                var ifHeader = GetIfHeader(req);
                StringAssert.Contains(ifHeader, "/webdav/dst.txt");
                StringAssert.Contains(ifHeader, "(<opaquelocktoken:dst-1>)");
                Assert.IsFalse(ifHeader.Contains("src"), "source URI should not appear when only destination is locked.");
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            await harness.Client.MoveFile("src.txt", "dst.txt", destinationLockToken: "opaquelocktoken:dst-1");
        }

        [TestMethod]
        public async Task MoveFile_sends_tagged_If_header_with_both_source_and_destination_tokens()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                var ifHeader = GetIfHeader(req);
                // Two tagged productions, source first then destination, space-separated.
                Assert.AreEqual(
                    "<http://example.com/webdav/src.txt> (<opaquelocktoken:src-1>) <http://example.com/webdav/dst.txt> (<opaquelocktoken:dst-1>)",
                    ifHeader);
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            await harness.Client.MoveFile("src.txt", "dst.txt",
                sourceLockToken: "opaquelocktoken:src-1",
                destinationLockToken: "opaquelocktoken:dst-1");
        }

        [TestMethod]
        public async Task MoveFolder_uses_collection_uris_with_trailing_slash_in_If_header()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                var ifHeader = GetIfHeader(req);
                // Collections keep their trailing slash both in Destination and the If resource-tag,
                // so server-side URI comparison stays consistent.
                StringAssert.Contains(ifHeader, "<http://example.com/webdav/src/>");
                StringAssert.Contains(ifHeader, "<http://example.com/webdav/dst/>");
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            await harness.Client.MoveFolder("src", "dst",
                sourceLockToken: "opaquelocktoken:s",
                destinationLockToken: "opaquelocktoken:d");
        }

        // -------------------- COPY --------------------

        [TestMethod]
        public async Task CopyFile_sends_tagged_If_header_for_destination_lock_token()
        {
            // RFC 4918 §7.5.1: COPY does not modify the source so only the destination needs a token.
            // The CopyFile signature deliberately exposes only `destinationLockToken`.
            using var harness = new ClientHarness(Responder(req =>
            {
                var ifHeader = GetIfHeader(req);
                StringAssert.Contains(ifHeader, "<http://example.com/webdav/dst.txt>");
                StringAssert.Contains(ifHeader, "(<opaquelocktoken:dst-1>)");
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            await harness.Client.CopyFile("src.txt", "dst.txt", destinationLockToken: "opaquelocktoken:dst-1");
        }

        [TestMethod]
        public async Task CopyFile_omits_If_header_when_no_destination_lock_token()
        {
            using var harness = new ClientHarness(Responder(req =>
            {
                Assert.IsFalse(req.Headers.Contains("If"));
                return StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created);
            }), Server, BasePath);

            await harness.Client.CopyFile("src.txt", "dst.txt");
        }

        // -------------------- 423 Locked surfacing --------------------

        [TestMethod]
        public async Task Upload_surfaces_423_Locked_as_WebDAVException()
        {
            // When callers forget to supply a lock token (or supply a stale one), the server
            // returns 423 Locked. The library must surface it as WebDAVException with the
            // status code preserved, not silently drop the failure.
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly((HttpStatusCode)423)), Server, BasePath);

            using var content = new MemoryStream(new byte[1]);
            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => harness.Client.Upload("/", content, "x"));
            Assert.AreEqual(423, ex.GetHttpCode());
        }

        [TestMethod]
        public async Task DeleteFile_surfaces_423_Locked_as_WebDAVException()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly((HttpStatusCode)423)), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => harness.Client.DeleteFile("x"));
            Assert.AreEqual(423, ex.GetHttpCode());
        }

        // -------------------- Token validation --------------------

        [TestMethod]
        public async Task Upload_throws_ArgumentException_on_malformed_lock_token()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.Created)), Server, BasePath);

            using var content = new MemoryStream(new byte[1]);
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.Upload("/", content, "f.txt", lockToken: "bad\r\ninjection"));
        }

        [TestMethod]
        public async Task DeleteFile_throws_ArgumentException_on_empty_lock_token()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.NoContent)), Server, BasePath);

            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.DeleteFile("x", lockToken: "   "));
        }
    }
}
