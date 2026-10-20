using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.Model;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class ListingPerformanceTests
    {
        public TestContext TestContext { get; set; }

        [DataTestMethod]
        [DataRow(1000)]
        [DataRow(10000)]
        [DataRow(100000)]
        public async Task Generated_listing_preserves_order_and_records_workload_cost(int count)
        {
            var xml = GenerateListing(count);
            using var harness = new ClientHarness(request =>
                request.Headers.GetValues("Depth").Single() == "0"
                    ? StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/"))
                    : new HttpResponseMessage((HttpStatusCode)207) { Content = new ByteArrayContent(xml) });
            await harness.Client.GetFolder();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var before = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            var items = await harness.Client.List();
            timer.Stop();
            var allocated = GC.GetTotalAllocatedBytes(true) - before;
            using var process = Process.GetCurrentProcess();
            TestContext.WriteLine($"LIST entries={count}; allocated_bytes={allocated}; process_peak_bytes={process.PeakWorkingSet64}; first_item_ms={timer.Elapsed.TotalMilliseconds:F3}");
            Assert.AreEqual(count, items.Count());
            Assert.AreEqual("/webdav/file0", items.First().Href);
            Assert.AreEqual("/webdav/file" + (count - 1), items.Last().Href);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Propname_skips_large_text_and_structured_values_without_materializing_them(bool structured)
        {
            var value = new string('x', 2 * 1024 * 1024);
            if (structured)
            {
                value = "<X:nested>" + value + "</X:nested>";
            }

            var xml = Encoding.UTF8.GetBytes(
                "<D:multistatus xmlns:D=\"DAV:\" xmlns:X=\"urn:test\"><D:response><D:href>/webdav/file</D:href>"
                + "<D:propstat><D:prop><X:large>" + value + "</X:large><D:displayname/></D:prop>"
                + "<D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response></D:multistatus>");
            using var stream = new MemoryStream(xml, writable: false);
            var before = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            var items = await ResponseParser.ParseItemsAsync(stream, PropFindMode.PropName);
            timer.Stop();
            var allocated = GC.GetTotalAllocatedBytes(true) - before;
            using var process = Process.GetCurrentProcess();
            TestContext.WriteLine($"PROPNAME structured={structured}; input_value_bytes={2 * 1024 * 1024}; allocated_bytes={allocated}; process_peak_bytes={process.PeakWorkingSet64}; first_item_ms={timer.Elapsed.TotalMilliseconds:F3}");
            Assert.AreEqual(1, items.Count);
            CollectionAssert.AreEquivalent(new[] { new PropertyName("urn:test", "large"), new PropertyName("DAV:", "displayname") },
                items[0].AvailablePropertyNames.ToArray());
            Assert.IsTrue(allocated < 512 * 1024, $"Discarding a 2 MiB property allocated {allocated} bytes; expected less than 512 KiB.");
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(2)]
        public async Task Single_item_retrieval_still_validates_later_resource_failures(int mode)
        {
            var xml = Encoding.UTF8.GetBytes(
                "<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>/webdav/first</D:href></D:response>"
                + "<D:response><D:href>/webdav/denied</D:href><D:status>HTTP/1.1 403 Forbidden</D:status></D:response></D:multistatus>");
            using var stream = new MemoryStream(xml, writable: false);
            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(
                () => ResponseParser.ParseItemAsync(stream, (PropFindMode)mode));
            Assert.AreEqual(403, ex.GetHttpCode());
        }

        [TestMethod]
        public async Task Single_item_retrieval_still_validates_malformed_trailing_xml()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
                "<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>/webdav/first</D:href></D:response><broken>"));
            await Assert.ThrowsExceptionAsync<XmlException>(() => ResponseParser.ParseItemAsync(stream, PropFindMode.AllProp));
        }

        private static byte[] GenerateListing(int count)
        {
            var xml = new StringBuilder("<D:multistatus xmlns:D=\"DAV:\">");
            for (int i = 0; i < count; i++)
            {
                if (i == count / 2)
                {
                    xml.Append("<D:response><D:href>/webdav/</D:href><D:propstat><D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>");
                }

                xml.Append("<D:response><D:href>/webdav/file").Append(i)
                    .Append("</D:href><D:propstat><D:prop><D:displayname>file").Append(i)
                    .Append("</D:displayname><D:getcontentlength>1</D:getcontentlength></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>");
            }
            return Encoding.UTF8.GetBytes(xml.Append("</D:multistatus>").ToString());
        }
    }
}
