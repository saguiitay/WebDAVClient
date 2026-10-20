using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class DiscoveryConcurrencyTests
    {
        [TestMethod]
        public async Task Concurrent_cold_operations_send_one_discovery_plus_one_request_each()
        {
            var started = Signal();
            var release = Signal();
            int discoveries = 0;
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                if (IsDiscovery(request))
                {
                    Interlocked.Increment(ref discoveries);
                    started.TrySetResult();
                    await release.Task.WaitAsync(token);
                }
                return Resource(request);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = CreateClient(http);
            var pending = Enumerable.Range(0, 24).Select(i => client.GetFile("file" + i)).ToArray();
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                release.TrySetResult();
                var items = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(24, items.Select(i => i.Href).Distinct().Count());
                Assert.AreEqual(1, discoveries);
                Assert.AreEqual(25, handler.Requests.Count);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [TestMethod]
        public async Task Cancelling_waiter_does_not_cancel_owner_or_send_another_discovery()
        {
            var started = Signal();
            var release = Signal();
            int discoveries = 0;
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                if (IsDiscovery(request))
                {
                    Interlocked.Increment(ref discoveries);
                    started.TrySetResult();
                    await release.Task.WaitAsync(token);
                }
                return Resource(request);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = CreateClient(http);
            var owner = client.GetFile("owner");
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                using var cancellation = new CancellationTokenSource();
                var waiter = client.GetFile("waiter", cancellationToken: cancellation.Token);
                cancellation.Cancel();
                await ExpectCancellation(waiter);
                release.TrySetResult();
                await owner.WaitAsync(TimeSpan.FromSeconds(5));
                await client.GetFile("later").WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(1, discoveries);
                Assert.AreEqual(3, handler.Requests.Count);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Cancelling_owner_allows_waiter_to_retry_discovery(bool duringBody)
        {
            using var stream = new PrefixThenStallStream("<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>/webdav/");
            var started = Signal();
            int discoveries = 0;
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                if (IsDiscovery(request) && Interlocked.Increment(ref discoveries) == 1)
                {
                    if (duringBody)
                    {
                        return new HttpResponseMessage((HttpStatusCode)207) { Content = new StreamContent(stream) };
                    }

                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                return Resource(request);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = CreateClient(http);
            using var cancellation = new CancellationTokenSource();
            var owner = client.GetFile("owner", cancellationToken: cancellation.Token);
            await (duringBody ? stream.Stalled.Task : started.Task).WaitAsync(TimeSpan.FromSeconds(5));
            var waiter = client.GetFile("waiter");
            cancellation.Cancel();
            await ExpectCancellation(owner);
            var result = await waiter.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual("/webdav/waiter", result.Href);
            Assert.AreEqual(2, discoveries);
            Assert.AreEqual(3, handler.Requests.Count);
            if (duringBody)
            {
                Assert.IsTrue(stream.IsDisposed);
            }
        }

        [TestMethod]
        public async Task Failed_owner_does_not_poison_waiters_or_cache()
        {
            var started = Signal();
            var release = Signal();
            int discoveries = 0;
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                if (IsDiscovery(request) && Interlocked.Increment(ref discoveries) == 1)
                {
                    started.TrySetResult();
                    await release.Task.WaitAsync(token);
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }
                return Resource(request);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = CreateClient(http);
            var owner = client.GetFile("owner");
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var waiter = client.GetFile("waiter");
                release.TrySetResult();
                var error = await Assert.ThrowsExceptionAsync<WebDAVException>(() => owner);
                Assert.AreEqual(500, error.GetHttpCode());
                Assert.AreEqual("/webdav/waiter", (await waiter.WaitAsync(TimeSpan.FromSeconds(5))).Href);
                Assert.AreEqual(2, discoveries);
                Assert.AreEqual(3, handler.Requests.Count);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [TestMethod]
        public async Task Body_deadline_releases_discovery_gate_for_waiter()
        {
            using var stream = new PrefixThenStallStream("<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>/webdav/");
            int discoveries = 0;
            using var handler = new AsyncHttpMessageHandler((request, _) => Task.FromResult(
                IsDiscovery(request) && Interlocked.Increment(ref discoveries) == 1
                    ? new HttpResponseMessage((HttpStatusCode)207) { Content = new StreamContent(stream) } : Resource(request)));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = CreateClient(http);
            client.ResponseBodyTimeout = TimeSpan.FromMilliseconds(250);
            var owner = client.GetFile("owner");
            await stream.Stalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var waiter = client.GetFile("waiter");
            await ExpectCancellation(owner);
            Assert.AreEqual("/webdav/waiter", (await waiter.WaitAsync(TimeSpan.FromSeconds(5))).Href);
            Assert.AreEqual(2, discoveries);
            Assert.IsTrue(stream.IsDisposed);
        }

        [TestMethod]
        public async Task Reconfiguration_does_not_wait_for_or_publish_obsolete_discovery()
        {
            var started = Signal();
            var release = Signal();
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                if (request.RequestUri.Host == "example.com")
                {
                    started.TrySetResult();
                    await release.Task.WaitAsync(token);
                }
                return Resource(request);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = CreateClient(http);
            var old = client.GetFile("old");
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                client.Server = "https://new.example";
                await client.GetFile("new").WaitAsync(TimeSpan.FromSeconds(5));
                release.TrySetResult();
                await old.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(4, handler.Requests.Count);
                Assert.AreEqual(1, handler.Requests.Count(r => r.Uri.Host == "example.com"));
                Assert.AreEqual(3, handler.Requests.Count(r => r.Uri.Host == "new.example"));
            }
            finally
            {
                release.TrySetResult();
            }
        }

        private static bool IsDiscovery(HttpRequestMessage request) => request.RequestUri.AbsolutePath == "/webdav/";

        private static HttpResponseMessage Resource(HttpRequestMessage request) => StubHttpMessageHandler.Multistatus(
            IsDiscovery(request) ? WebDAVResponses.Root("/webdav/") : WebDAVResponses.SingleFile(request.RequestUri.AbsolutePath));

        private static Client CreateClient(System.Net.Http.HttpClient http)
            => new Client(http) { Server = "https://example.com", BasePath = "/webdav/" };

        private static TaskCompletionSource Signal() => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        private static async Task ExpectCancellation(Task task)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Fail("Expected cancellation.");
            }
            catch (OperationCanceledException) { }
        }
    }
}
