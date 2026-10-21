using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests.Parser
{
    [TestClass]
    public class MetadataParserEdgeTests
    {
        [DataTestMethod]
        [DataRow("", 0)]
        [DataRow(" \t ", 0)]
        [DataRow("HTTP/1.1", 0)]
        [DataRow("HTTP/1.1   ", 0)]
        [DataRow("HTTP/1.1 not-a-code Failed", 0)]
        [DataRow("HTTP/1.1 424 Failed Dependency", 424)]
        [DataRow("\t HTTP/1.1\t200\t OK", 200)]
        public void Proppatch_preserves_unknown_statuses_and_parses_whitespace_without_guessing_success(string statusLine, int expected)
        {
            XNamespace dav = "DAV:";
            var xml = new XElement(dav + "multistatus", new XElement(dav + "response",
                new XElement(dav + "propstat", new XElement(dav + "prop"),
                    new XElement(dav + "status", statusLine),
                    new XElement(dav + "responsedescription", "failed & retry"))));
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml.ToString()));
            var result = PropPatchResponseParser.Parse(stream).Single();
            Assert.AreEqual(expected, result.StatusCode);
            Assert.AreEqual("failed & retry", result.ResponseDescription);
        }

        [TestMethod]
        public void Empty_proppatch_groups_are_not_confirmed_outcomes_and_invalid_xml_is_explicit()
        {
            using var empty = XmlStream("<D:multistatus xmlns:D=\"DAV:\"><D:response><D:propstat/></D:response></D:multistatus>");
            Assert.AreEqual(0, PropPatchResponseParser.Parse(empty).Count);
            using var malformed = XmlStream("<D:multistatus xmlns:D=\"DAV:\"><D:response>");
            Assert.ThrowsException<XmlException>(() => PropPatchResponseParser.Parse(malformed));
        }

        [DataTestMethod]
        [DataRow(403, 200)]
        [DataRow(200, 403)]
        public async Task Nested_property_status_cannot_override_the_actual_proppatch_outcome(int actualStatus, int nestedStatus)
        {
            var xml = $"""
                <D:multistatus xmlns:D="DAV:" xmlns:X="urn:test"><D:response>
                <D:href>/webdav/file</D:href><D:propstat>
                <D:status>HTTP/1.1 {actualStatus} Result</D:status>
                <D:responsedescription>actual result</D:responsedescription>
                <D:prop><X:metadata><D:status>HTTP/1.1 {nestedStatus} Nested</D:status>
                <D:responsedescription>nested value</D:responsedescription></X:metadata></D:prop>
                </D:propstat></D:response></D:multistatus>
                """;
            using var stream = XmlStream(xml);
            var result = PropPatchResponseParser.Parse(stream).Single();
            Assert.AreEqual(actualStatus, result.StatusCode);
            Assert.AreEqual("actual result", result.ResponseDescription);
            int requests = 0;
            using var harness = new ClientHarness(_ => StubHttpMessageHandler.Multistatus(
                ++requests == 1 ? WebDAVResponses.Root("/webdav/") : xml));
            if (actualStatus == 200)
            {
                Assert.IsTrue(await harness.Client.SetProperty("file", "metadata", "urn:test", "value"));
            }
            else
            {
                var error = await Assert.ThrowsExceptionAsync<WebDAVException>(() =>
                    harness.Client.SetProperty("file", "metadata", "urn:test", "value"));
                Assert.AreEqual(actualStatus, error.GetHttpCode());
            }
        }

        [TestMethod]
        public void Sparse_lock_elements_are_missing_information_not_fabricated_values()
        {
            using var stream = XmlStream("""
                <D:prop xmlns:D="DAV:"><D:lockdiscovery><D:activelock>
                <D:locktype/><D:lockscope/><D:locktoken/><D:lockroot/>
                <D:depth>0</D:depth><D:timeout>Second-123</D:timeout>
                </D:activelock></D:lockdiscovery></D:prop>
                """);
            var info = LockResponseParser.Parse(stream);
            Assert.IsNotNull(info);
            Assert.IsNull(info.LockType);
            Assert.IsNull(info.LockScope);
            Assert.IsNull(info.Token);
            Assert.IsNull(info.LockRoot);
            Assert.AreEqual("0", info.Depth);
            Assert.AreEqual(123, info.TimeoutSeconds);
        }

        [DataTestMethod]
        [DataRow("<D:response><D:href> </D:href><D:status>HTTP/1.1 403 Forbidden</D:status></D:response>")]
        [DataRow("<D:response><D:href>/file</D:href></D:response>")]
        public async Task Mutation_recovery_rejects_members_without_an_identity_or_status(string member)
        {
            using var stream = XmlStream("<D:multistatus xmlns:D=\"DAV:\">" + member + "</D:multistatus>");
            await Assert.ThrowsExceptionAsync<XmlException>(() => MultiStatusResponseParser.ParseAsync(stream, CancellationToken.None));
        }

        private static MemoryStream XmlStream(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }
}
