using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class MutationMultiStatusTests
    {
        [DataTestMethod]
        [DataRow("DELETE")]
        [DataRow("MOVE")]
        [DataRow("COPY")]
        public async Task Partial_mutation_failure_preserves_member_outcomes_without_retrying(string method)
        {
            const string xml = """
                <D:multistatus xmlns:D="DAV:" xmlns:x="urn:test">
                  <D:response><D:href>/webdav/a&amp;b</D:href><D:status>HTTP/1.1 204 No Content</D:status></D:response>
                  <D:response><D:href>/webdav/locked</D:href><D:status>HTTP/1.1 423 Locked</D:status>
                    <D:responsedescription>Needs owner &amp; lock</D:responsedescription>
                    <D:error><D:response><D:href>https://other.example/</D:href><D:status>HTTP/1.1 500 Error</D:status></D:response></D:error>
                  </D:response>
                  <D:response><D:href>/webdav/conflict</D:href><D:status>HTTP/1.1 409 Conflict</D:status></D:response>
                  <x:response><D:href>/ignored</D:href><D:status>HTTP/1.1 404 Not Found</D:status></x:response>
                </D:multistatus>
                """;
            var response = new TrackingResponse { Content = new StringContent(xml) };
            int mutations = 0;
            using var harness = Harness(response, () => mutations++);
            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(() => Invoke(harness.Client, method));
            Assert.AreEqual(207, ex.GetHttpCode());
            Assert.AreEqual(3, ex.ResourceStatuses.Count);
            Assert.AreEqual("/webdav/a&b", ex.ResourceStatuses[0].Href);
            Assert.AreEqual(204, ex.ResourceStatuses[0].StatusCode);
            Assert.AreEqual(423, ex.ResourceStatuses[1].StatusCode);
            Assert.AreEqual("Needs owner & lock", ex.ResourceStatuses[1].ResponseDescription);
            Assert.AreEqual(409, ex.ResourceStatuses[2].StatusCode);
            Assert.AreEqual(1, mutations);
            Assert.AreEqual(1, response.DisposeCount);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("<D:multistatus xmlns:D=\"DAV:\"/>")]
        [DataRow("<D:multistatus xmlns:D=\"DAV:\"><D:response/></D:multistatus>")]
        [DataRow("<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>/file</D:href><D:status>invalid</D:status></D:response></D:multistatus>")]
        [DataRow("<!DOCTYPE x [<!ENTITY x SYSTEM \"file:///missing\">]><x>&x;</x>")]
        public async Task Malformed_recovery_details_keep_outer_status_and_explicit_parser_error(string xml)
        {
            var response = new TrackingResponse { Content = new StringContent(xml) };
            using var harness = Harness(response, () => { });
            var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(() => Invoke(harness.Client, "DELETE"));
            Assert.AreEqual(207, ex.GetHttpCode());
            Assert.AreEqual(0, ex.ResourceStatuses.Count);
            Assert.IsInstanceOfType(ex.InnerException, typeof(XmlException));
            StringAssert.Contains(ex.Message, "could not be parsed");
            Assert.AreEqual(1, response.DisposeCount);
        }

        private static ClientHarness Harness(HttpResponseMessage response, Action mutation)
            => new ClientHarness(request =>
            {
                if (request.Method.Method == "PROPFIND")
                    return StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/"));
                mutation();
                return response;
            });

        private static Task Invoke(Client client, string method) => method switch
        {
            "DELETE" => client.DeleteFolder("folder"),
            "MOVE" => client.MoveFolder("source", "destination"),
            "COPY" => client.CopyFolder("source", "destination"),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };

        private sealed class TrackingResponse : HttpResponseMessage
        {
            public int DisposeCount { get; private set; }
            public TrackingResponse() : base((HttpStatusCode)207) { }
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    DisposeCount++;
                base.Dispose(disposing);
            }
        }
    }
}
