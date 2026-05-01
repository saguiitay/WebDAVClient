using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.Model;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests.ClientTests
{
    /// <summary>
    /// Behavioural tests for the PROPFIND request-body variants added per RFC 4918 §9.1
    /// (<c>&lt;allprop/&gt;</c>, <c>&lt;prop&gt;</c>, <c>&lt;propname/&gt;</c>):
    /// the request body sent on the wire and the property-bag collections populated on
    /// each returned <see cref="Item"/>.
    /// </summary>
    [TestClass]
    public class ClientPropFindTests
    {
        private const string Server = "http://example.com";
        private const string BasePath = "/webdav/";
        private const string CustomNs = "http://example.com/ns";

        // The first request out of a fresh Client is the base-path PROPFIND used to resolve
        // m_encodedBasePath. Intercept it so individual tests only reason about the second
        // (test-specific) call.
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

        // -------------------- AllProp (default) regression --------------------

        [TestMethod]
        public async Task List_default_sends_allprop_body_and_does_not_populate_property_bag()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.FolderListing())), Server, BasePath);

            var items = (await harness.Client.List()).ToList();

            // Body shape: <allprop/>
            var body = harness.Handler.Requests.Last().BodyAsString();
            StringAssert.Contains(body, "<allprop");
            Assert.IsFalse(body.Contains("<prop>"), "Default List() must not send a <prop> body");
            Assert.IsFalse(body.Contains("<propname"), "Default List() must not send a <propname> body");

            // Backward-compat: typed fields populated, property bag stays null.
            Assert.IsTrue(items.Count > 0);
            foreach (var item in items)
            {
                Assert.IsNull(item.FoundProperties, "AllProp must not populate FoundProperties");
                Assert.IsNull(item.NotFoundProperties, "AllProp must not populate NotFoundProperties");
                Assert.IsNull(item.AvailablePropertyNames, "AllProp must not populate AvailablePropertyNames");
            }
        }

        // -------------------- <prop> (targeted) --------------------

        [TestMethod]
        public async Task GetFile_with_properties_sends_prop_body_with_each_property()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(SingleFilePropResponse())), Server, BasePath);

            var props = new[]
            {
                new PropertyName("DAV:", "getetag"),
                new PropertyName(CustomNs, "author"),
            };

            await harness.Client.GetFile("file.txt", props);

            var req = harness.Handler.Requests.Last();
            Assert.AreEqual("PROPFIND", req.Method.Method);
            var body = req.BodyAsString();
            StringAssert.Contains(body, "propfind");
            StringAssert.Contains(body, "<D:prop");
            StringAssert.Contains(body, "getetag");
            StringAssert.Contains(body, "author");
            StringAssert.Contains(body, CustomNs);
            // Crucial: not allprop, not propname.
            Assert.IsFalse(body.Contains("<allprop"));
            Assert.IsFalse(body.Contains("<propname"));
        }

        [TestMethod]
        public async Task GetFile_with_properties_populates_FoundProperties_and_NotFoundProperties()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(SingleFilePropResponse())), Server, BasePath);

            var item = await harness.Client.GetFile("file.txt", new[]
            {
                new PropertyName("DAV:", "getetag"),
                new PropertyName(CustomNs, "author"),
                new PropertyName(CustomNs, "missing"),
            });

            Assert.IsNotNull(item.FoundProperties);
            Assert.IsTrue(item.FoundProperties.ContainsKey(new PropertyName("DAV:", "getetag")));
            Assert.AreEqual("\"abc123\"", item.FoundProperties[new PropertyName("DAV:", "getetag")]);
            Assert.IsTrue(item.FoundProperties.ContainsKey(new PropertyName(CustomNs, "author")));
            Assert.AreEqual("Jane Doe", item.FoundProperties[new PropertyName(CustomNs, "author")]);

            Assert.IsNotNull(item.NotFoundProperties);
            Assert.IsTrue(item.NotFoundProperties.Contains(new PropertyName(CustomNs, "missing")));

            // Typed field still populated for the standard DAV: live property.
            Assert.AreEqual("\"abc123\"", item.Etag);
        }

        [TestMethod]
        public async Task List_with_properties_overload_sends_prop_body()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.FolderListing())), Server, BasePath);

            await harness.Client.List("/", 1, new[] { new PropertyName("DAV:", "displayname") });

            var body = harness.Handler.Requests.Last().BodyAsString();
            StringAssert.Contains(body, "<D:prop");
            StringAssert.Contains(body, "displayname");
            Assert.IsFalse(body.Contains("<allprop"));
        }

        [TestMethod]
        public async Task List_with_null_properties_throws()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.FolderListing())), Server, BasePath);

            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                harness.Client.List("/", 1, null));
        }

        [TestMethod]
        public async Task GetFile_with_empty_properties_throws()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.FolderListing())), Server, BasePath);

            // Forces the first PROPFIND (base-path resolve) to actually fire, then the
            // empty properties list trips PropFindRequestBuilder.
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                harness.Client.GetFile("file.txt", new PropertyName[0]));
        }

        // -------------------- <propname/> --------------------

        [TestMethod]
        public async Task GetFilePropertyNames_sends_propname_body()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(PropNameResponse())), Server, BasePath);

            await harness.Client.GetFilePropertyNames("file.txt");

            var body = harness.Handler.Requests.Last().BodyAsString();
            StringAssert.Contains(body, "<propname");
            Assert.IsFalse(body.Contains("<allprop"));
            Assert.IsFalse(body.Contains("<prop>"));
        }

        [TestMethod]
        public async Task GetFilePropertyNames_populates_AvailablePropertyNames()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(PropNameResponse())), Server, BasePath);

            var item = await harness.Client.GetFilePropertyNames("file.txt");

            Assert.IsNotNull(item.AvailablePropertyNames);
            Assert.IsTrue(item.AvailablePropertyNames.Contains(new PropertyName("DAV:", "getetag")));
            Assert.IsTrue(item.AvailablePropertyNames.Contains(new PropertyName("DAV:", "displayname")));
            Assert.IsTrue(item.AvailablePropertyNames.Contains(new PropertyName(CustomNs, "author")));
            // Values are not requested in propname mode.
            Assert.IsNull(item.FoundProperties);
        }

        [TestMethod]
        public async Task ListPropertyNames_sends_propname_body()
        {
            using var harness = new ClientHarness(Responder(_ =>
                StubHttpMessageHandler.Multistatus(PropNameResponse())), Server, BasePath);

            await harness.Client.ListPropertyNames("/");

            var body = harness.Handler.Requests.Last().BodyAsString();
            StringAssert.Contains(body, "<propname");
        }

        // -------------------- Test fixtures --------------------

        private static string SingleFilePropResponse() =>
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
<D:multistatus xmlns:D=""DAV:"" xmlns:X=""{CustomNs}"">
    <D:response>
        <D:href>/webdav/file.txt</D:href>
        <D:propstat>
            <D:prop>
                <D:getetag>""abc123""</D:getetag>
                <X:author>Jane Doe</X:author>
            </D:prop>
            <D:status>HTTP/1.1 200 OK</D:status>
        </D:propstat>
        <D:propstat>
            <D:prop>
                <X:missing/>
            </D:prop>
            <D:status>HTTP/1.1 404 Not Found</D:status>
        </D:propstat>
    </D:response>
</D:multistatus>";

        private static string PropNameResponse() =>
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
<D:multistatus xmlns:D=""DAV:"" xmlns:X=""{CustomNs}"">
    <D:response>
        <D:href>/webdav/file.txt</D:href>
        <D:propstat>
            <D:prop>
                <D:getetag/>
                <D:displayname/>
                <D:getcontentlength/>
                <X:author/>
            </D:prop>
            <D:status>HTTP/1.1 200 OK</D:status>
        </D:propstat>
    </D:response>
</D:multistatus>";
    }
}
