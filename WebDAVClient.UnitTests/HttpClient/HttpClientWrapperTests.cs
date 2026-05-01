using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.HttpClient;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests.HttpClientTests
{
    [TestClass]
    public class HttpClientWrapperTests
    {
        [TestMethod]
        public async Task SendAsync_routes_to_primary_client()
        {
            using var primaryHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            using var primary = new System.Net.Http.HttpClient(primaryHandler);
            using var wrapper = new HttpClientWrapper(primary);

            using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
            using var response = await wrapper.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(1, primaryHandler.Requests.Count);
        }

        [TestMethod]
        public async Task SendUploadAsync_routes_to_upload_client_when_provided()
        {
            using var primaryHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            using var uploadHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Created));
            using var primary = new System.Net.Http.HttpClient(primaryHandler);
            using var upload = new System.Net.Http.HttpClient(uploadHandler);
            using var wrapper = new HttpClientWrapper(primary, upload);

            using var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/file");
            using var response = await wrapper.SendUploadAsync(request);

            Assert.AreEqual(System.Net.HttpStatusCode.Created, response.StatusCode);
            Assert.AreEqual(0, primaryHandler.Requests.Count, "Upload must not hit the primary client");
            Assert.AreEqual(1, uploadHandler.Requests.Count);
        }

        [TestMethod]
        public async Task SendUploadAsync_falls_back_to_primary_when_no_upload_client()
        {
            using var primaryHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
            using var primary = new System.Net.Http.HttpClient(primaryHandler);
            using var wrapper = new HttpClientWrapper(primary);

            using var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/file");
            using var response = await wrapper.SendUploadAsync(request);

            Assert.AreEqual(System.Net.HttpStatusCode.NoContent, response.StatusCode);
            Assert.AreEqual(1, primaryHandler.Requests.Count);
        }

        [TestMethod]
        public void Dispose_disposes_only_primary_when_uploadClient_is_same_instance()
        {
            // Reusing the same HttpClient for both roles is the default — verify Dispose doesn't
            // try to dispose it twice (which would throw ObjectDisposedException internally).
            var handler = new TrackingHandler();
            var client = new System.Net.Http.HttpClient(handler);
            var wrapper = new HttpClientWrapper(client);

            wrapper.Dispose();

            Assert.AreEqual(1, handler.DisposeCount, "Inner handler should be disposed exactly once");
        }

        [TestMethod]
        public void Dispose_disposes_both_clients_when_distinct()
        {
            var primaryHandler = new TrackingHandler();
            var uploadHandler = new TrackingHandler();
            var primary = new System.Net.Http.HttpClient(primaryHandler);
            var upload = new System.Net.Http.HttpClient(uploadHandler);
            var wrapper = new HttpClientWrapper(primary, upload);

            wrapper.Dispose();

            Assert.AreEqual(1, primaryHandler.DisposeCount);
            Assert.AreEqual(1, uploadHandler.DisposeCount);
        }

        [TestMethod]
        public void Dispose_is_idempotent()
        {
            var handler = new TrackingHandler();
            var client = new System.Net.Http.HttpClient(handler);
            var wrapper = new HttpClientWrapper(client);

            wrapper.Dispose();
            wrapper.Dispose();

            Assert.AreEqual(1, handler.DisposeCount);
        }

        private sealed class TrackingHandler : HttpMessageHandler
        {
            public int DisposeCount;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));

            protected override void Dispose(bool disposing)
            {
                if (disposing) Interlocked.Increment(ref DisposeCount);
                base.Dispose(disposing);
            }
        }
    }
}
