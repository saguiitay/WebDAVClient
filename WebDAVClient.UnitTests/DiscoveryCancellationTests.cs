using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DiscoveryCancellationTests
    {
        [TestMethod]
        public async Task Already_cancelled_first_operation_sends_nothing()
        {
            using var handler = new AsyncHttpMessageHandler((_, _) => throw new AssertFailedException("No request expected."));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new Client(http) { Server = "http://example.com", BasePath = "/webdav/" };
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await ExpectCancellation(client.List(cancellationToken: cancellation.Token));
            Assert.AreEqual(0, handler.Requests.Count);
        }

        [TestMethod]
        public async Task Cancelled_discovery_headers_are_not_cached_and_next_operation_retries()
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            using var handler = new AsyncHttpMessageHandler(async (_, token) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                return StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/"));
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new Client(http) { Server = "http://example.com", BasePath = "/webdav/" };
            using var cancellation = new CancellationTokenSource();
            var operation = client.GetFolder(cancellationToken: cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await ExpectCancellation(operation);
            await client.GetFolder().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(3, calls, "The second operation must discover again before retrieving the folder.");
        }

        private static async Task ExpectCancellation(Task operation)
        {
            try
            {
                await operation.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Fail("Expected cancellation.");
            }
            catch (OperationCanceledException) { }
        }
    }
}
