using System;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.Model;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class PropFindStatusTests
    {
        [DataTestMethod]
        [DataRow(403)]
        [DataRow(404)]
        [DataRow(409)]
        [DataRow(500)]
        [DataRow(302)]
        public async Task Failed_resources_throw_the_embedded_status_in_every_mode(int status)
        {
            foreach (var mode in new[] { 0, 1, 2 })
            {
                var calls = 0;
                using var harness = new ClientHarness(_ => StubHttpMessageHandler.Multistatus(++calls == 1
                    ? WebDAVResponses.Root() : Multistatus($@"<D:response><D:href>/webdav/file.txt</D:href>
<D:status>HTTP/1.1 {status} Failed</D:status></D:response>")));
                Func<Task> operation = mode switch
                {
                    0 => () => harness.Client.GetFile("file.txt"),
                    1 => () => harness.Client.GetFile("file.txt", new[] { new PropertyName("DAV:", "displayname") }),
                    _ => () => harness.Client.GetFilePropertyNames("file.txt")
                };

                WebDAVException exception = status == 409
                    ? await Assert.ThrowsExceptionAsync<WebDAVConflictException>(operation)
                    : await Assert.ThrowsExceptionAsync<WebDAVException>(operation);

                Assert.AreEqual(status, exception.GetHttpCode());
            }
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public async Task Mixed_listings_fail_instead_of_returning_partial_success(int mode)
        {
            var calls = 0;
            var xml = Multistatus(@"<D:response><D:href>/webdav/good.txt</D:href>
<D:propstat><D:prop><D:displayname>good</D:displayname></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>
<D:response><D:href>/webdav/denied.txt</D:href><D:status>HTTP/1.1 403 Forbidden</D:status></D:response>");
            using var harness = new ClientHarness(_ => StubHttpMessageHandler.Multistatus(
                ++calls == 1 ? WebDAVResponses.Root() : xml));
            Func<Task> operation = mode switch
            {
                0 => () => harness.Client.List(),
                1 => () => harness.Client.List("/", 1, new[] { new PropertyName("DAV:", "displayname") }),
                _ => () => harness.Client.ListPropertyNames()
            };

            var exception = await Assert.ThrowsExceptionAsync<WebDAVException>(operation);

            Assert.AreEqual(403, exception.GetHttpCode());
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public async Task Failed_propstats_cannot_overwrite_successful_metadata(int mode)
        {
            const string success = @"<D:propstat><D:prop><D:displayname>good</D:displayname>
<D:getcontentlength>5</D:getcontentlength><D:resourcetype><D:collection/></D:resourcetype></D:prop>
<D:status>HTTP/1.1 200 OK</D:status></D:propstat>";
            const string failure = @"<D:propstat><D:status>HTTP/1.1 403 Forbidden</D:status><D:prop>
<D:displayname>wrong</D:displayname><D:getcontentlength>999</D:getcontentlength><D:resourcetype/>
<X:failed xmlns:X=""urn:custom"">private</X:failed></D:prop></D:propstat>";
            foreach (var failureFirst in new[] { false, true })
            {
                var calls = 0;
                var xml = Multistatus("<D:response><D:href>/webdav/folder/</D:href>"
                    + (failureFirst ? failure + success : success + failure) + "</D:response>");
                using var harness = new ClientHarness(_ => StubHttpMessageHandler.Multistatus(
                    ++calls == 1 ? WebDAVResponses.Root() : xml));
                var item = mode switch
                {
                    0 => await harness.Client.GetFolder("folder"),
                    1 => await harness.Client.GetFolder("folder", new[]
                    {
                        new PropertyName("DAV:", "displayname"), new PropertyName("DAV:", "getcontentlength"),
                        new PropertyName("DAV:", "resourcetype"), new PropertyName("urn:custom", "failed")
                    }),
                    _ => await harness.Client.GetFolderPropertyNames("folder")
                };

                Assert.IsTrue(item.IsCollection);
                Assert.AreEqual("/webdav/folder/", item.Href);
                if (mode != 2)
                {
                    Assert.AreEqual("good", item.DisplayName);
                    Assert.AreEqual(5L, item.ContentLength);
                }
                if (mode == 0)
                {
                    Assert.IsNull(item.FoundProperties);
                }
                else if (mode == 1)
                {
                    Assert.AreEqual("good", item.FoundProperties[new PropertyName("DAV:", "displayname")]);
                    Assert.IsFalse(item.FoundProperties.ContainsKey(new PropertyName("urn:custom", "failed")));
                    Assert.IsTrue(item.NotFoundProperties.Contains(new PropertyName("urn:custom", "failed")));
                }
                else
                {
                    Assert.IsFalse(item.AvailablePropertyNames.Contains(new PropertyName("urn:custom", "failed")));
                }
            }
        }

        [DataTestMethod]
        [DataRow(true, "invalid")]
        [DataRow(false, "invalid")]
        [DataRow(true, "")]
        [DataRow(false, "")]
        public async Task Explicit_malformed_status_is_not_treated_as_omitted(bool resourceLevel, string status)
        {
            var body = resourceLevel ? $"<D:status>{status}</D:status>"
                : $"<D:propstat><D:prop><D:displayname>wrong</D:displayname></D:prop><D:status>{status}</D:status></D:propstat>";
            var calls = 0;
            using var harness = new ClientHarness(_ => StubHttpMessageHandler.Multistatus(++calls == 1
                ? WebDAVResponses.Root() : Multistatus("<D:response><D:href>/webdav/file.txt</D:href>" + body + "</D:response>")));

            await Assert.ThrowsExceptionAsync<XmlException>(() => harness.Client.GetFile("file.txt"));
        }

        private static string Multistatus(string responses) =>
            @"<D:multistatus xmlns:D=""DAV:"">" + responses + "</D:multistatus>";
    }
}
