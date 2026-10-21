using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class ClientEdgeContractTests
    {
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Uploads_preserve_custom_user_agent_headers_lock_submission_and_exact_content(bool partial)
        {
            using var harness = new ClientHarness(request => request.Method.Method == "PROPFIND"
                ? StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/"))
                : new HttpResponseMessage(HttpStatusCode.NoContent));
            harness.Client.UserAgent = "CoverageTests";
            harness.Client.UserAgentVersion = "1.0";
            harness.Client.CustomHeaders = new[] { new KeyValuePair<string, string>("X-Tenant", "coverage") };
            using var content = new MemoryStream(new byte[] { 1, 2, 3 });
            Assert.IsTrue(partial
                ? await harness.Client.UploadPartial("/", content, "file", 5, 8, "opaquelocktoken:test")
                : await harness.Client.Upload("/", content, "file", "opaquelocktoken:test"));
            var request = harness.Handler.Requests.Last();
            Assert.AreEqual(HttpMethod.Put, request.Method);
            Assert.AreEqual("CoverageTests/1.0", request.Headers["User-Agent"].Single());
            Assert.AreEqual("coverage", request.Headers["X-Tenant"].Single());
            StringAssert.Contains(request.Headers["If"].Single(), "opaquelocktoken:test");
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, request.Body);
            Assert.IsFalse(content.CanRead);
        }

        [TestMethod]
        public async Task Empty_single_item_response_has_context_instead_of_a_null_resource()
        {
            int calls = 0;
            using var harness = new ClientHarness(_ => StubHttpMessageHandler.Multistatus(
                ++calls == 1 ? WebDAVResponses.Root("/webdav/") : "<D:multistatus xmlns:D=\"DAV:\"/>"));
            var error = await Assert.ThrowsExceptionAsync<WebDAVException>(() => harness.Client.GetFile("file"));
            Assert.AreEqual(0, error.GetHttpCode());
            Assert.AreEqual("PROPFIND", error.Method);
            Assert.AreEqual("/webdav/file", error.ResourceUri.AbsolutePath);
            Assert.AreEqual(2, calls);
        }

        [TestMethod]
        public async Task Refresh_keeps_the_submitted_token_when_a_sparse_lock_body_omits_it()
        {
            using var harness = new ClientHarness(request => request.Method.Method == "PROPFIND"
                ? StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/"))
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        <D:prop xmlns:D="DAV:"><D:lockdiscovery><D:activelock>
                        <D:depth>0</D:depth><D:timeout>Second-123</D:timeout>
                        </D:activelock></D:lockdiscovery></D:prop>
                        """)
                });
            var info = await harness.Client.RefreshLock("file", "<opaquelocktoken:existing>");
            Assert.AreEqual("opaquelocktoken:existing", info.Token);
            Assert.AreEqual("0", info.Depth);
            Assert.AreEqual(123, info.TimeoutSeconds);
        }
    }
}
