using System.Linq;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Model;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DocumentationExamplesTests
    {
        [DataTestMethod]
        [DataRow("reports/file.txt")]
        [DataRow("/reports/file.txt")]
        [DataRow("/dav/reports/file.txt")]
        [DataRow("http://example.com/dav/reports/file.txt")]
        public async Task Quickstart_path_forms_match_the_consumption_guide(string path)
        {
            int calls = 0;
            using var harness = new ClientHarness(_ => StubHttpMessageHandler.Multistatus(
                ++calls == 1 ? WebDAVResponses.Root("/dav/") : WebDAVResponses.SingleFile("/dav/reports/file.txt")),
                basePath: "/dav/");
            IClient client = harness.Client;
            await client.GetFile(path);
            Assert.AreEqual("/dav/reports/file.txt", harness.Handler.Requests.Last().RequestUri.AbsolutePath);
        }

        [TestMethod]
        public async Task Partial_transfer_example_matches_exclusive_upload_and_inclusive_download_ends()
        {
            byte[] bytes = { 0xF0, 0x0B, 0xA5 };
            using var harness = new ClientHarness(request =>
            {
                if (request.Method.Method == "PROPFIND")
                    return StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/"));
                if (request.Method == HttpMethod.Put)
                {
                    Assert.AreEqual("bytes 2-4/*", request.Content.Headers.ContentRange.ToString());
                    Assert.AreEqual(3L, request.Content.Headers.ContentLength);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                Assert.AreEqual("bytes=2-4", request.Headers.Range.ToString());
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentRange = new ContentRangeHeaderValue(2, 4, 8);
                return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
            });
            using var input = new MemoryStream(bytes);
            Assert.IsTrue(await harness.Client.UploadPartial("/", input, "sample.bin", 2, 5));
            using var part = await harness.Client.DownloadPartial("sample.bin", 2, 4);
            using var result = new MemoryStream();
            await part.CopyToAsync(result);
            CollectionAssert.AreEqual(bytes, result.ToArray());
            CollectionAssert.AreEqual(bytes, harness.Handler.Requests.Single(r => r.Method == HttpMethod.Put).Body);
        }

        [TestMethod]
        public async Task Targeted_propfind_example_uses_qualified_names_and_dictionary_keys()
        {
            const string xml = """
                <D:multistatus xmlns:D="DAV:" xmlns:X="https://example.com/ns">
                  <D:response><D:href>/webdav/report.txt</D:href><D:propstat><D:prop>
                    <D:getetag>tag</D:getetag><D:getcontentlength>5</D:getcontentlength>
                    <X:author>Alice &amp; Bob</X:author>
                  </D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>
                </D:multistatus>
                """;
            int calls = 0;
            using var harness = new ClientHarness(_ => StubHttpMessageHandler.Multistatus(
                ++calls == 1 ? WebDAVResponses.Root("/webdav/") : xml));
            var props = new[]
            {
                new PropertyName("DAV:", "getetag"),
                new PropertyName("DAV:", "getcontentlength"),
                new PropertyName("https://example.com/ns", "author")
            };
            var items = await harness.Client.List("/", depth: 1, properties: props);
            var item = items.Single();
            Assert.IsTrue(item.FoundProperties.TryGetValue(new PropertyName("https://example.com/ns", "author"), out var author));
            Assert.AreEqual("Alice & Bob", author);
            Assert.AreEqual(5L, item.ContentLength);
        }
    }
}
