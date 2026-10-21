using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.HttpClient;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class UploadResponseTests
    {
        [DataTestMethod]
        [DataRow(false, 201, false)]
        [DataRow(false, 201, true)]
        [DataRow(true, 201, false)]
        [DataRow(true, 201, true)]
        [DataRow(false, 403, false)]
        [DataRow(false, 403, true)]
        [DataRow(true, 403, false)]
        [DataRow(true, 403, true)]
        public async Task Upload_inspects_status_and_disposes_response_without_buffering(bool partial, int status, bool separateUploadClient)
        {
            var content = new UnbufferedResponseContent();
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = content };
            using var primaryHandler = new StubHttpMessageHandler(request =>
                request.Method.Method == "PROPFIND"
                    ? StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/")) : response);
            using var uploadHandler = new StubHttpMessageHandler(_ => response);
            using var primary = new System.Net.Http.HttpClient(primaryHandler);
            using var upload = new System.Net.Http.HttpClient(uploadHandler);
            using var wrapper = new HttpClientWrapper(primary, separateUploadClient ? upload : null);
            using var client = new Client(wrapper) { Server = "http://example.com", BasePath = "/webdav/" };
            using var input = new MemoryStream(new byte[1]);

            var operation = partial ? client.UploadPartial("/", input, "file", 0, 1) : client.Upload("/", input, "file");
            if (status == 403)
            {
                var ex = await Assert.ThrowsExceptionAsync<WebDAVException>(() => operation);
                Assert.AreEqual(403, ex.GetHttpCode());
            }
            else
            {
                Assert.IsTrue(await operation);
            }

            Assert.AreEqual(0, content.SerializeCount, "Status-only upload handling must not read the body.");
            Assert.AreEqual(1, content.DisposeCount);
            Assert.AreEqual(separateUploadClient ? 1 : 2, primaryHandler.Requests.Count);
            Assert.AreEqual(separateUploadClient ? 1 : 0, uploadHandler.Requests.Count);
        }

        private sealed class UnbufferedResponseContent : HttpContent
        {
            public int SerializeCount;
            public int DisposeCount;

            protected override bool TryComputeLength(out long length)
            {
                length = 32 * 1024 * 1024;
                return true;
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                Interlocked.Increment(ref SerializeCount);
                throw new AssertFailedException("PUT response buffering is unnecessary.");
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Interlocked.Increment(ref DisposeCount);
                }

                base.Dispose(disposing);
            }
        }
    }
}
