using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Authentication;
using WebDAVClient.Helpers;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class SecurityRegressionTests
    {
        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public void Property_contents_cannot_replace_resource_identity(int mode)
        {
            const string xml = @"<D:multistatus xmlns:D=""DAV:"" xmlns:X=""urn:custom"">
<D:response>
  <D:href>/webdav/file.txt</D:href>
  <X:href>http://example.com:8123/foreign-namespace/</X:href>
  <D:propstat><D:prop>
    <D:displayname>file.txt</D:displayname>
    <D:lockdiscovery><D:activelock>
      <D:lockroot><D:href>http://example.com/lockroot/</D:href></D:lockroot>
      <D:locktoken><D:href>opaquelocktoken:abc</D:href></D:locktoken>
    </D:activelock></D:lockdiscovery>
    <X:payload>
      <D:response><D:href>http://example.com:8123/nested-response/</D:href></D:response>
      <D:propstat><D:prop><D:href>http://example.com/nested-prop/</D:href></D:prop></D:propstat>
      <X:href>http://example.com/custom/</X:href>
      <D:displayname>nested</D:displayname>
      <D:resourcetype><D:collection/></D:resourcetype>
    </X:payload>
    <X:displayname>wrong</X:displayname>
  </D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
</D:response>
<X:response><D:href>http://example.com/foreign-response/</D:href></X:response>
<D:response><D:href>/webdav/next.txt</D:href>
  <D:propstat><D:prop><D:displayname>next.txt</D:displayname></D:prop>
    <D:status>HTTP/1.1 200 OK</D:status></D:propstat>
</D:response>
</D:multistatus>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

            var items = ResponseParser.ParseItems(stream, (PropFindMode)mode);

            Assert.AreEqual(2, items.Count);
            Assert.AreEqual("/webdav/file.txt", items[0].Href);
            Assert.AreEqual("file.txt", items[0].DisplayName);
            Assert.IsFalse(items[0].IsCollection);
            Assert.AreEqual("/webdav/next.txt", items[1].Href);
            Assert.AreEqual("next.txt", items[1].DisplayName);
        }

        [DataTestMethod]
        [DataRow("urn:custom")]
        [DataRow("dav:")]
        [DataRow("")]
        public void Foreign_namespace_href_cannot_supply_resource_identity(string ns)
        {
            var xml = $@"<D:multistatus xmlns:D=""DAV:""><D:response>
<href xmlns=""{ns}"">http://example.com/stolen/</href>
</D:response></D:multistatus>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

            Assert.ThrowsException<System.Xml.XmlException>(() => ResponseParser.ParseItems(stream));
        }

        [TestMethod]
        public async Task Nested_href_cannot_downgrade_authenticated_discovery()
        {
            var poisoned = WebDAVResponses.Root().Replace("</D:prop>",
                @"<X:payload xmlns:X=""urn:custom""><D:href>http://example.com:8123/stolen/</D:href></X:payload></D:prop>");
            var stub = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Multistatus(poisoned));
            using var http = AuthenticatedHttpClient(stub);
            using var client = new Client(http) { Server = "https://example.com", BasePath = "/webdav/" };

            await client.List();

            Assert.AreEqual(2, stub.Requests.Count);
            foreach (var request in stub.Requests)
            {
                Assert.AreEqual("https://example.com/webdav/", request.RequestUri.AbsoluteUri);
                Assert.IsTrue(request.Headers.ContainsKey("Authorization"));
            }
        }

        [DataTestMethod]
        [DataRow("http://example.com/webdav/", null)]
        [DataRow("http://example.com:8123/webdav/", null)]
        [DataRow("https://example.com:8443/webdav/", null)]
        [DataRow("https://other.example/webdav/", null)]
        [DataRow("ftp://example.com/webdav/", null)]
        [DataRow("https://example.com/webdav/", 8443)]
        [DataRow("https://example.com:9443/webdav/", 8443)]
        [DataRow("https://user:test-secret@other.example/webdav/", null)]
        public async Task Unsafe_discovered_root_is_rejected_before_caching_or_followup(string href, int? port)
        {
            var stub = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Multistatus(WebDAVResponses.Root(href)));
            using var http = AuthenticatedHttpClient(stub);
            using var client = new Client(http) { Server = "https://example.com", Port = port, BasePath = "/webdav/" };

            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => client.List("https://example.com/webdav/"));
            Assert.AreEqual(1, stub.Requests.Count);
            var expected = new UriBuilder("https://example.com/webdav/") { Port = port ?? 443 };
            Assert.AreEqual(expected.Uri, stub.Requests[0].RequestUri);
            Assert.IsTrue(stub.Requests[0].Headers.ContainsKey("Authorization"));
            Assert.IsFalse(exception.Message.Contains("test-secret"));
        }

        [TestMethod]
        public async Task Rejected_discovered_root_is_not_cached()
        {
            var calls = 0;
            var stub = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Multistatus(
                WebDAVResponses.Root(++calls == 1 ? "http://example.com/stolen/" : "/webdav/")));
            using var http = AuthenticatedHttpClient(stub);
            using var client = new Client(http) { Server = "https://example.com", BasePath = "/webdav/" };

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.List(null));
            await client.List(null);

            Assert.AreEqual(3, stub.Requests.Count);
            Assert.IsTrue(stub.Requests.All(r => r.RequestUri.Scheme == "https"));
        }

        [DataTestMethod]
        [DataRow("http://example.com/stolen")]
        [DataRow("https://example.com:8123/stolen")]
        [DataRow("https://other.example/stolen")]
        public async Task Unsafe_child_href_never_reaches_authenticated_transport_or_upload_body(string href)
        {
            var calls = 0;
            var stub = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Multistatus(
                ++calls == 1 ? WebDAVResponses.Root() : WebDAVResponses.SingleFile(href)));
            using var http = AuthenticatedHttpClient(stub);
            using var client = new Client(http) { Server = "https://example.com", BasePath = "/webdav/" };
            var item = await client.GetFile("file.txt");
            using var upload = new MemoryStream(new byte[] { 1, 2, 3 });

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.Download(item.Href));
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.Upload(item.Href, upload, "file.txt"));
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.List(item.Href));
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.CopyFile("file.txt", item.Href));
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.MoveFile(item.Href, "file.txt"));

            Assert.AreEqual(2, stub.Requests.Count);
            Assert.AreEqual(0L, upload.Position);
            Assert.IsTrue(stub.Requests.All(r => r.RequestUri.Scheme == "https"));
        }

        [DataTestMethod]
        [DataRow("https://example.com", null, "https://EXAMPLE.com:443/webdav/")]
        [DataRow("https://example.com:9443", null, "https://EXAMPLE.com:9443/webdav/")]
        [DataRow("https://example.com:9443", 8443, "https://EXAMPLE.com:8443/webdav/")]
        [DataRow("http://example.com", null, "http://EXAMPLE.com:80/webdav/")]
        [DataRow("https://example.com", 8443, "/webdav/")]
        public async Task Same_origin_absolute_and_relative_hrefs_remain_supported(string server, int? port, string href)
        {
            var stub = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Multistatus(WebDAVResponses.Root(href)));
            using var http = AuthenticatedHttpClient(stub);
            using var client = new Client(http) { Server = server, Port = port, BasePath = "/webdav/" };

            await client.List(null);
            await client.List(href);

            Assert.AreEqual(3, stub.Requests.Count);
            var origin = new UriBuilder(server);
            if (port.HasValue)
            {
                origin.Port = port.Value;
            }

            foreach (var request in stub.Requests)
            {
                Assert.AreEqual(origin.Scheme, request.RequestUri.Scheme);
                Assert.AreEqual(origin.Host, request.RequestUri.Host);
                Assert.AreEqual(origin.Uri.Port, request.RequestUri.Port);
                Assert.IsTrue(request.Headers.ContainsKey("Authorization"));
            }
        }

        [DataTestMethod]
        [DataRow("https://example.com/webdav/")]
        [DataRow("https://example.com:9443/webdav/")]
        public async Task Port_override_rejects_other_ports(string href)
        {
            var stub = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Multistatus(WebDAVResponses.Root()));
            using var http = AuthenticatedHttpClient(stub);
            using var client = new Client(http)
            {
                Server = "https://example.com:9443",
                Port = 8443,
                BasePath = "/webdav/"
            };
            await client.List();

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => client.List(href));

            Assert.AreEqual(2, stub.Requests.Count);
            Assert.IsTrue(stub.Requests.All(r => r.RequestUri.Port == 8443));
        }

        private static System.Net.Http.HttpClient AuthenticatedHttpClient(StubHttpMessageHandler stub)
        {
            return new System.Net.Http.HttpClient(new BearerTokenAuthenticationHandler("test-token")
            {
                InnerHandler = stub
            });
        }
    }
}
