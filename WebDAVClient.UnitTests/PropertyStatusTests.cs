using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Model;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class PropertyStatusTests
    {
        private const string c_response = """
            <D:multistatus xmlns:D="DAV:" xmlns:A="urn:a" xmlns:B="urn:b">
              <D:response><D:href>/webdav/file</D:href>
                <D:propstat><D:prop><A:author>Alice</A:author></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
                <D:propstat><D:prop><B:author/></D:prop><D:status>HTTP/1.1 403 Forbidden</D:status></D:propstat>
                <D:propstat><D:prop><A:missing/></D:prop><D:status>HTTP/1.1 404 Not Found</D:status></D:propstat>
                <D:propstat><D:prop><A:dependent/></D:prop><D:status>HTTP/1.1 424 Failed Dependency</D:status></D:propstat>
                <D:propstat><D:prop><A:unknown/></D:prop></D:propstat>
              </D:response>
            </D:multistatus>
            """;

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Qualified_statuses_distinguish_denied_missing_dependency_and_omission(bool namesOnly)
        {
            int calls = 0;
            using var harness = new ClientHarness(_ => StubHttpMessageHandler.Multistatus(
                ++calls == 1 ? WebDAVResponses.Root("/webdav/") : c_response));
            var author = new PropertyName("urn:a", "author");
            var deniedAuthor = new PropertyName("urn:b", "author");
            var item = namesOnly
                ? await harness.Client.GetFilePropertyNames("file")
                : await harness.Client.GetFile("file", new[] { author, deniedAuthor });
            Assert.AreEqual(5, item.PropertyStatuses.Count);
            Assert.AreEqual(200, item.PropertyStatuses[author]);
            Assert.AreEqual(403, item.PropertyStatuses[deniedAuthor]);
            Assert.AreEqual(404, item.PropertyStatuses[new PropertyName("urn:a", "missing")]);
            Assert.AreEqual(424, item.PropertyStatuses[new PropertyName("urn:a", "dependent")]);
            Assert.IsNull(item.PropertyStatuses[new PropertyName("urn:a", "unknown")]);
            if (namesOnly)
            {
                Assert.AreEqual(2, item.AvailablePropertyNames.Count);
                Assert.IsNull(item.NotFoundProperties);
            }
            else
            {
                Assert.AreEqual("Alice", item.FoundProperties[author]);
                Assert.AreEqual(1, item.FoundProperties.Count);
                Assert.AreEqual(4, item.NotFoundProperties.Count);
                Assert.IsTrue(item.NotFoundProperties.Contains(deniedAuthor));
            }
        }

        [TestMethod]
        public async Task Allprop_keeps_legacy_null_bags_and_malformed_explicit_status_fails()
        {
            int calls = 0;
            using var harness = new ClientHarness(_ => StubHttpMessageHandler.Multistatus(
                ++calls == 1 ? WebDAVResponses.Root("/webdav/") : c_response));
            var item = await harness.Client.GetFile("file");
            Assert.IsNull(item.PropertyStatuses);
            Assert.IsNull(item.FoundProperties);
            using var malformed = new ClientHarness(_ => StubHttpMessageHandler.Multistatus(
                c_response.Replace("HTTP/1.1 403 Forbidden", "invalid status")));
            await Assert.ThrowsExceptionAsync<XmlException>(() =>
                malformed.Client.GetFile("file", new[] { new PropertyName("urn:a", "author") }));
        }
    }
}
