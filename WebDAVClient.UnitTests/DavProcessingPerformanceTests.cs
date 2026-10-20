using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DavProcessingPerformanceTests
    {
        public TestContext TestContext { get; set; }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void Resource_projection_records_allocations_and_throughput(bool legacy)
        {
            var element = XElement.Parse("""
                <D:response xmlns:D="DAV:" xmlns:X="urn:test">
                  <D:href>/dav/file</D:href>
                  <D:propstat><D:prop>
                    <D:displayname>file</D:displayname><D:getcontentlength>123</D:getcontentlength>
                    <D:getetag>"tag"</D:getetag><D:resourcetype/><X:custom>value</X:custom>
                  </D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
                  <D:propstat><D:prop><X:denied/></D:prop><D:status>HTTP/1.1 403 Forbidden</D:status></D:propstat>
                </D:response>
                """);
            Measure("PROJECT legacy=" + legacy, () =>
            {
                var resource = DavXml.ParseResponse(element, legacy);
                return resource.PropertyStatuses[0].Properties.Count + resource.PropertyStatuses[1].Properties.Count;
            }, expected: 6, allocationLimit: 4000);
        }

        [TestMethod]
        public void Repeated_property_lookup_preserves_duplicate_order_with_bounded_allocations()
        {
            var resource = DavXml.ParseResponse(XElement.Parse("""
                <D:response xmlns:D="DAV:"><D:href>/dav/file</D:href>
                <D:propstat><D:prop><D:displayname>first</D:displayname><D:getetag>"one"</D:getetag>
                <D:displayname>second</D:displayname></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
                <D:propstat><D:prop><D:displayname/></D:prop><D:status>HTTP/1.1 403 Forbidden</D:status></D:propstat>
                </D:response>
                """), false);
            var name = new PropertyName("DAV:", "displayname");
            Measure("LOOKUP", () =>
            {
                int count = 0;
                foreach (var property in resource.GetProperties(name)) { count += property.StatusCode.Value; }
                return count;
            }, expected: 803, allocationLimit: 96);
            var values = new System.Collections.Generic.List<string>();
            foreach (var property in resource.GetProperties(name))
            {
                property.Value.TryGetText(out var text);
                values.Add(text);
            }
            CollectionAssert.AreEqual(new[] { "first", "second", "" }, values);
            Assert.ThrowsException<ArgumentNullException>(() => resource.GetProperties(null));
        }

        [TestMethod]
        public void Successful_listing_completion_check_records_allocations()
        {
            var resource = DavXml.ParseResponse(XElement.Parse("""
                <D:response xmlns:D="DAV:"><D:href>/dav/file</D:href>
                <D:propstat><D:prop><D:displayname>file</D:displayname></D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
                </D:response>
                """), false);
            var resources = new DavResourceResponse[1000];
            Array.Fill(resources, resource);
            using var response = new HttpResponseMessage((HttpStatusCode)207);
            var result = new DavMultiStatusResult(response, DavClientTests.Endpoint, "PROPFIND", Array.AsReadOnly(resources));
            Measure("ENSURE resources=1000", () =>
            {
                result.EnsureSucceeded();
                return result.Resources.Count;
            }, expected: 1000, allocationLimit: 0, iterations: 100);
        }

        private void Measure(string label, Func<int> operation, int expected, long allocationLimit, int iterations = 10000)
        {
            for (int i = 0; i < 1000; i++) { Assert.AreEqual(expected, operation()); }
            var allocations = new long[5];
            var timings = new double[5];
            for (int sample = 0; sample < allocations.Length; sample++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                long total = 0;
                for (int i = 0; i < iterations; i++) { total += operation(); }
                timings[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.AreEqual((long)expected * iterations, total);
            }
            Array.Sort(allocations);
            Array.Sort(timings);
            long bytesPerOperation = allocations[allocations.Length / 2] / iterations;
            TestContext.WriteLine($"{label}; iterations={iterations}; median_ms={timings[timings.Length / 2]:F3}; allocated_bytes_per_operation={bytesPerOperation}");
            Assert.IsTrue(bytesPerOperation <= allocationLimit, $"{label} allocated {bytesPerOperation} bytes per operation; budget is {allocationLimit}.");
        }
    }
}
