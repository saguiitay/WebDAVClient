using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests.ClientTests
{
    /// <summary>
    /// Behavioural tests for the PROPPATCH surface (SetProperty / RemoveProperty) added per
    /// RFC 4918 §9.2. Cover request shape (method, URL, body), response handling (success vs.
    /// per-property failure inside 207 Multi-Status), and argument validation.
    /// </summary>
    [TestClass]
    public class ClientPropPatchTests
    {
        private const string Server = "http://example.com";
        private const string BasePath = "/webdav/";
        private const string CustomNs = "http://example.com/ns";

        // The first request out of a fresh Client is the base-path PROPFIND used to resolve
        // m_encodedBasePath. Intercept it so individual tests only need to reason about the
        // PROPPATCH call.
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

        // -------------------- SetProperty: request shape --------------------

        [TestMethod]
        public async Task SetProperty_sends_PROPPATCH_with_propertyupdate_set_body()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.PropPatchSuccess())), Server, BasePath);

            var ok = await harness.Client.SetProperty("file.txt", "author", CustomNs, "Jane Doe");

            Assert.IsTrue(ok);
            var req = harness.Handler.Requests.Last();
            Assert.AreEqual("PROPPATCH", req.Method.Method);
            Assert.AreEqual("/webdav/file.txt", req.RequestUri.AbsolutePath);

            var body = req.BodyAsString();
            StringAssert.Contains(body, "<D:propertyupdate");
            StringAssert.Contains(body, "<D:set");
            StringAssert.Contains(body, "<D:prop");
            // The property must be in its custom namespace, not DAV:.
            StringAssert.Contains(body, CustomNs);
            StringAssert.Contains(body, "author");
            StringAssert.Contains(body, "Jane Doe");
        }

        [TestMethod]
        public async Task SetProperty_xml_escapes_value()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.PropPatchSuccess())), Server, BasePath);

            await harness.Client.SetProperty("file.txt", "note", CustomNs, "<script>&\"'");

            var body = harness.Handler.Requests.Last().BodyAsString();
            // No raw <, >, or & should leak into the body. (Single quotes / double quotes are
            // valid in element content and may or may not be escaped — only verify the special
            // ones that XML *requires* to be escaped in element content.)
            Assert.IsFalse(body.Contains("<script>"), "Raw <script> tag must not appear in body.");
            StringAssert.Contains(body, "&lt;script&gt;");
            StringAssert.Contains(body, "&amp;");
        }

        [TestMethod]
        public async Task SetProperty_treats_null_value_as_empty_string()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.PropPatchSuccess())), Server, BasePath);

            var ok = await harness.Client.SetProperty("file.txt", "tag", CustomNs, value: null);

            Assert.IsTrue(ok);
            var body = harness.Handler.Requests.Last().BodyAsString();
            // We expect a self-closing or empty-content property element. Accept either form.
            Assert.IsTrue(
                body.Contains("<X:tag xmlns:X=\"" + CustomNs + "\" />")
                || body.Contains("<X:tag xmlns:X=\"" + CustomNs + "\"/>")
                || body.Contains("<X:tag xmlns:X=\"" + CustomNs + "\"></X:tag>"),
                "Expected an empty <tag/> element in: " + body);
        }

        // -------------------- SetProperty: argument validation --------------------

        [TestMethod]
        public async Task SetProperty_rejects_null_or_empty_property_name()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.PropPatchSuccess())), Server, BasePath);

            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.SetProperty("file.txt", null, CustomNs, "v"));
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.SetProperty("file.txt", "", CustomNs, "v"));
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.SetProperty("file.txt", "   ", CustomNs, "v"));
        }

        [TestMethod]
        public async Task SetProperty_rejects_property_name_with_colon_or_space()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.PropPatchSuccess())), Server, BasePath);

            // NCName forbids colons (those are reserved for prefix delimiters).
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.SetProperty("file.txt", "ns:author", CustomNs, "v"));
            // Whitespace is invalid in any XML name.
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.SetProperty("file.txt", "my author", CustomNs, "v"));
            // Names cannot start with a digit.
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.SetProperty("file.txt", "1author", CustomNs, "v"));
        }

        [TestMethod]
        public async Task SetProperty_rejects_null_or_empty_namespace()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.PropPatchSuccess())), Server, BasePath);

            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.SetProperty("file.txt", "author", null, "v"));
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.SetProperty("file.txt", "author", "", "v"));
        }

        [TestMethod]
        public async Task SetProperty_rejects_DAV_namespace()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.PropPatchSuccess())), Server, BasePath);

            // RFC 4918 §15 — DAV-namespaced properties are protected; refuse them client-side
            // with a clearer error than a 403/409 from the wire.
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.SetProperty("file.txt", "displayname", "DAV:", "v"));
        }

        // -------------------- SetProperty: response handling --------------------

        [TestMethod]
        public async Task SetProperty_returns_true_on_207_with_2xx_propstat()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(
                    WebDAVResponses.PropPatchSuccess(status: "HTTP/1.1 200 OK"))), Server, BasePath);

            Assert.IsTrue(await harness.Client.SetProperty("file.txt", "author", CustomNs, "Jane"));
        }

        [TestMethod]
        public async Task SetProperty_throws_WebDAVException_on_non_207_HTTP_status()
        {
            // Plain 200 OK with no multistatus body must NOT be treated as success — the spec
            // requires per-property reporting, and accepting bare 200 would mask server bugs.
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.StatusOnly(HttpStatusCode.OK, "<ok/>")), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => harness.Client.SetProperty("file.txt", "author", CustomNs, "v"));
            Assert.AreEqual(200, ex.GetHttpCode());
        }

        [TestMethod]
        public async Task SetProperty_throws_WebDAVException_on_207_with_403_propstat()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(
                    WebDAVResponses.PropPatchFailure(status: "HTTP/1.1 403 Forbidden",
                        responseDescription: "Property is protected."))), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => harness.Client.SetProperty("file.txt", "author", CustomNs, "v"));
            Assert.AreEqual(403, ex.GetHttpCode());
            StringAssert.Contains(ex.Message, "author");
            StringAssert.Contains(ex.Message, "Property is protected.");
        }

        [TestMethod]
        public async Task SetProperty_maps_409_propstat_to_WebDAVConflictException()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(
                    WebDAVResponses.PropPatchFailure(status: "HTTP/1.1 409 Conflict"))), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVConflictException>(
                () => harness.Client.SetProperty("file.txt", "author", CustomNs, "v"));
            Assert.AreEqual(409, ex.GetHttpCode());
        }

        [TestMethod]
        public async Task SetProperty_throws_when_207_has_no_propstat()
        {
            const string emptyMultistatus =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>/webdav/file.txt</D:href></D:response></D:multistatus>";
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(emptyMultistatus)), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => harness.Client.SetProperty("file.txt", "author", CustomNs, "v"));
            Assert.AreEqual(207, ex.GetHttpCode());
            StringAssert.Contains(ex.Message, "propstat");
        }

        [TestMethod]
        public async Task SetProperty_throws_when_207_body_is_malformed_xml()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus("<not-valid xml")), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => harness.Client.SetProperty("file.txt", "author", CustomNs, "v"));
            Assert.AreEqual(207, ex.GetHttpCode());
        }

        [TestMethod]
        public async Task SetProperty_rejects_DTD_in_response_body_XXE_hardening()
        {
            // Confirms PropPatchResponseParser uses the hardened XmlReaderSettings — DTD
            // processing must be prohibited so a malicious server can't reach external entities.
            const string dtdBody =
                "<?xml version=\"1.0\"?>\n" +
                "<!DOCTYPE foo [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>\n" +
                "<D:multistatus xmlns:D=\"DAV:\"><D:response><D:propstat><D:status>&x;</D:status></D:propstat></D:response></D:multistatus>";
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(dtdBody)), Server, BasePath);

            await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => harness.Client.SetProperty("file.txt", "author", CustomNs, "v"));
        }

        // -------------------- RemoveProperty --------------------

        [TestMethod]
        public async Task RemoveProperty_sends_PROPPATCH_with_remove_body_and_no_value()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.PropPatchSuccess())), Server, BasePath);

            var ok = await harness.Client.RemoveProperty("file.txt", "author", CustomNs);

            Assert.IsTrue(ok);
            var req = harness.Handler.Requests.Last();
            Assert.AreEqual("PROPPATCH", req.Method.Method);
            var body = req.BodyAsString();
            StringAssert.Contains(body, "<D:remove");
            StringAssert.Contains(body, "<D:prop");
            StringAssert.Contains(body, "author");
            // Remove must not carry a <D:set> instruction.
            Assert.IsFalse(body.Contains("<D:set"), "RemoveProperty body must not contain <D:set>.");
        }

        [TestMethod]
        public async Task RemoveProperty_succeeds_when_property_did_not_exist()
        {
            // Per RFC 4918 §9.2: removing a property that doesn't exist is not an error — the
            // server returns 200 for that property. Our method must report success in that case.
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(
                    WebDAVResponses.PropPatchSuccess(status: "HTTP/1.1 200 OK"))), Server, BasePath);

            Assert.IsTrue(await harness.Client.RemoveProperty("file.txt", "obsolete", CustomNs));
        }

        [TestMethod]
        public async Task RemoveProperty_throws_on_207_with_failed_propstat()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(
                    WebDAVResponses.PropPatchFailure(status: "HTTP/1.1 424 Failed Dependency"))), Server, BasePath);

            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => harness.Client.RemoveProperty("file.txt", "author", CustomNs));
            Assert.AreEqual(424, ex.GetHttpCode());
        }

        [TestMethod]
        public async Task RemoveProperty_validates_arguments()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.PropPatchSuccess())), Server, BasePath);

            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.RemoveProperty("file.txt", null, CustomNs));
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.RemoveProperty("file.txt", "author", ""));
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.RemoveProperty("file.txt", "author", "DAV:"));
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => harness.Client.RemoveProperty("file.txt", "bad name", CustomNs));
        }
    }
}
