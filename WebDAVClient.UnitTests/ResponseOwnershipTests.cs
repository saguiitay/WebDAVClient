using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class ResponseOwnershipTests
    {
        [DataTestMethod]
        [DataRow("MOVE", 200)]
        [DataRow("MOVE", 201)]
        [DataRow("MOVE", 204)]
        [DataRow("MOVE", 403)]
        [DataRow("COPY", 200)]
        [DataRow("COPY", 201)]
        [DataRow("COPY", 204)]
        [DataRow("COPY", 403)]
        public async Task Mutation_disposes_response_and_nonempty_content(string method, int status)
        {
            var content = new TrackingContent();
            var response = new TrackingResponse((HttpStatusCode)status) { Content = content };
            using var harness = new ClientHarness(request =>
                request.Method.Method == "PROPFIND"
                    ? StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/"))
                    : response);

            Task<bool> Invoke() => method == "MOVE"
                ? harness.Client.MoveFile("source", "destination")
                : harness.Client.CopyFile("source", "destination");

            if (status == 403)
                await Assert.ThrowsExceptionAsync<WebDAVException>(() => Invoke());
            else
                Assert.IsTrue(await Invoke());

            Assert.AreEqual(1, response.DisposeCount);
            Assert.AreEqual(1, content.DisposeCount);
        }

        private sealed class TrackingResponse : HttpResponseMessage
        {
            public int DisposeCount { get; private set; }

            public TrackingResponse(HttpStatusCode status) : base(status) { }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    DisposeCount++;
                base.Dispose(disposing);
            }
        }

        private sealed class TrackingContent : StringContent
        {
            public int DisposeCount { get; private set; }

            public TrackingContent() : base("unread response content") { }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    DisposeCount++;
                base.Dispose(disposing);
            }
        }
    }
}
