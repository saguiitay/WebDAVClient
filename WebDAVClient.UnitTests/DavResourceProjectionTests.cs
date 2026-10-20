using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DavResourceProjectionTests
    {
        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public void Single_pass_projection_preserves_direct_children_first_values_and_wire_order(bool legacy, bool uppercase)
        {
            var element = XElement.Parse("""
                <D:response xmlns:D="DAV:" xmlns:X="urn:test">
                  <X:href>extension, not resource identity</X:href>
                  <D:unknown><D:status>HTTP/1.1 500 Ignored</D:status></D:unknown>
                  <D:location><X:href>ignored</X:href><D:unknown/></D:location>
                  <D:location><D:href>/dav/destination</D:href><D:href><X:ignored/></D:href></D:location>
                  <D:location><D:href><X:ignored/></D:href></D:location>
                  <D:responsedescription></D:responsedescription>
                  <D:responsedescription><X:ignored/></D:responsedescription>
                  <D:propstat>
                    <D:status>HTTP/1.1 200 OK</D:status>
                    <D:prop><X:a>first</X:a><X:a>second</X:a></D:prop>
                    <D:responsedescription>first description</D:responsedescription>
                    <D:responsedescription><X:ignored/></D:responsedescription>
                    <D:error><X:one/></D:error><D:error><X:two/></D:error>
                    <X:status>HTTP/1.1 500 Ignored</X:status><D:unknown/>
                  </D:propstat>
                  <D:propstat><D:prop><X:a/></D:prop><D:status>HTTP/1.1 404 Missing</D:status></D:propstat>
                  <D:error><X:outer-one/></D:error><D:error><X:outer-two/></D:error>
                  <X:extension/>
                  <D:href>/dav/file</D:href>
                </D:response>
                """);
            if (uppercase)
            {
                foreach (var node in element.DescendantsAndSelf())
                {
                    if (node.Name.NamespaceName == "DAV:")
                    {
                        node.Name = XName.Get(node.Name.LocalName.ToUpperInvariant(), "DAV:");
                    }
                }
            }
            var resource = DavXml.ParseResponse(element, legacy);
            Assert.AreEqual("/dav/file", resource.Hrefs.Single());
            Assert.IsNull(resource.StatusCode);
            Assert.AreEqual("", resource.Description);
            Assert.AreEqual("/dav/destination", resource.Location);
            CollectionAssert.AreEqual(new[]
                {
                    "outer-one", "outer-two"
                }, resource.Errors.Select(e => e.Name.LocalName).ToArray());
            CollectionAssert.AreEqual(new[]
                {
                    "href", "extension"
                }, resource.Extensions.Select(e => e.Name.LocalName).ToArray());
            Assert.AreEqual("first description", resource.PropertyStatuses[0].Description);
            CollectionAssert.AreEqual(new[]
                {
                    "one", "two"
                }, resource.PropertyStatuses[0].Errors.Select(e => e.Name.LocalName).ToArray());

            var properties = resource.GetProperties(new PropertyName("urn:test", "a"));
            using (var iterator = properties.GetEnumerator())
            {
                Assert.IsTrue(iterator.MoveNext());
                Assert.AreEqual("first", iterator.Current.Value.ToElement().Value);
            }
            CollectionAssert.AreEqual(new[]
                {
                    200, 200, 404
                }, properties.Select(p => p.StatusCode.Value).ToArray());
            CollectionAssert.AreEqual(new[]
                {
                    "first", "second", ""
                }, properties.Select(p => p.Value.ToElement().Value).ToArray());
            Assert.AreEqual(0, resource.GetProperties(new PropertyName("other:namespace", "a")).Count());
            element.RemoveNodes();
            Assert.AreEqual("first", properties.First().Value.ToElement().Value, "Projection must remain detached from its source XML.");
        }

        [TestMethod]
        public void Modern_projection_remains_case_sensitive()
        {
            var element = XElement.Parse("""
                <D:response xmlns:D="DAV:"><D:HREF>/dav/file</D:HREF><D:STATUS>HTTP/1.1 200 OK</D:STATUS></D:response>
                """);
            Assert.ThrowsException<XmlException>(() => DavXml.ParseResponse(element, false));
            Assert.AreEqual(200, DavXml.ParseResponse(element, true).StatusCode);
        }

        [TestMethod]
        public void Legacy_projection_retains_multiple_property_containers_and_missing_statuses()
        {
            var element = XElement.Parse("""
                <D:response xmlns:D="DAV:" xmlns:X="urn:test"><D:href>/dav/file</D:href>
                <D:propstat><D:prop><X:a>one</X:a></D:prop><D:prop><X:b>two</X:b></D:prop></D:propstat>
                <D:propstat/></D:response>
                """);
            var resource = DavXml.ParseResponse(element, true);
            Assert.IsNull(resource.PropertyStatuses[0].StatusCode);
            CollectionAssert.AreEqual(new[]
                {
                    "a", "b"
                }, resource.PropertyStatuses[0].Properties.Select(p => p.Name.LocalName).ToArray());
            Assert.AreEqual(0, resource.PropertyStatuses[1].Properties.Count);
            Assert.ThrowsException<XmlException>(() => DavXml.ParseResponse(element, false));
        }

        [DataTestMethod]
        [DataRow(102, 409)]
        [DataRow(403, 409)]
        [DataRow(200, 409)]
        [DataRow(200, 102)]
        public void Completion_check_retains_first_failure_and_all_resource_level_outcomes(int propertyStatus, int resourceStatus)
        {
            var first = DavXml.ParseResponse(XElement.Parse(
                "<D:response xmlns:D=\"DAV:\"><D:href>/dav/first</D:href><D:propstat><D:prop><D:displayname/></D:prop>"
                + "<D:status>HTTP/1.1 " + propertyStatus + " Result</D:status></D:propstat></D:response>"), false);
            var second = DavXml.ParseResponse(XElement.Parse(
                "<D:response xmlns:D=\"DAV:\"><D:href>/dav/second</D:href><D:href>/dav/alias</D:href>"
                + "<D:status>HTTP/1.1 " + resourceStatus + " Result</D:status></D:response>"), false);
            using var response = new HttpResponseMessage((HttpStatusCode)207);
            var result = new DavMultiStatusResult(response, DavClientTests.Endpoint, "PROPFIND", Array.AsReadOnly(new[]
                {
                    first, second
                }));
            int expected = propertyStatus == 200 ? resourceStatus : propertyStatus;
            WebDAVException error;
            if (expected == 409)
            {
                error = Assert.ThrowsException<WebDAVConflictException>(result.EnsureSucceeded);
            }
            else
            {
                error = Assert.ThrowsException<WebDAVException>(result.EnsureSucceeded);
            }
            Assert.AreEqual(expected, error.FailedStatusCode);
            Assert.AreEqual(207, error.ResponseStatusCode);
            CollectionAssert.AreEqual(new[]
                {
                    "/dav/second", "/dav/alias"
                }, error.ResourceStatuses.Select(s => s.Href).ToArray());
            Assert.IsTrue(error.ResourceStatuses.All(s => s.StatusCode == resourceStatus));
        }
    }
}
