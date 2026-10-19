using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Model;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DocumentationExamplesTests
    {
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
