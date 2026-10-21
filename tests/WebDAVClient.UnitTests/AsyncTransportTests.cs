using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class AsyncTransportTests
    {
        [TestMethod]
        public async Task Handler_returns_headers_without_buffering_stream_and_preserves_cancellation()
        {
            using var stream = new PrefixThenStallStream("prefix");
            using var handler = new AsyncHttpMessageHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }));
            using var http = new System.Net.Http.HttpClient(handler);
            using var response = await http.GetAsync("http://example.com", HttpCompletionOption.ResponseHeadersRead);
            Assert.IsFalse(stream.Stalled.Task.IsCompleted);
            using var body = await response.Content.ReadAsStreamAsync();
            Assert.AreEqual(6, await body.ReadAsync(new byte[10]));
            using var cancellation = new CancellationTokenSource();
            var read = body.ReadAsync(new byte[10], cancellation.Token).AsTask();
            await stream.Stalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            try
            {
                await read.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Fail("The stalled read should be cancelled.");
            }
            catch (OperationCanceledException) { }
        }

        [TestMethod]
        public async Task Concurrent_requests_are_captured_without_eager_body_reads()
        {
            using var handler = new AsyncHttpMessageHandler(async (_, token) =>
            {
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            await Task.WhenAll(Enumerable.Range(0, 32).Select(async i =>
            {
                using var response = await http.GetAsync("http://example.com/" + i);
            }));
            Assert.AreEqual(32, handler.Requests.Count);
            Assert.AreEqual(32, handler.Requests.Select(r => r.Uri).Distinct().Count());
        }
    }
}
