using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Authentication;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class TokenProviderConcurrencyTests
    {
        [TestMethod]
        public async Task Concurrent_requests_share_one_refresh_without_serializing_transport()
        {
            using var gate = new SemaphoreSlim(1, 1);
            var started = Signal();
            var release = Signal();
            var allTransportsStarted = Signal();
            var releaseTransports = Signal();
            int refreshes = 0;
            int providerCalls = 0;
            int activeTransports = 0;
            var cached = CachedProvider(gate, async token =>
            {
                Interlocked.Increment(ref refreshes);
                started.TrySetResult();
                await release.Task.WaitAsync(token);
                return ("shared", DateTimeOffset.UtcNow.AddHours(1));
            });
            using var transport = new AsyncHttpMessageHandler(async (request, token) =>
            {
                Assert.AreEqual("shared", request.Headers.Authorization?.Parameter);
                if (Interlocked.Increment(ref activeTransports) == 24)
                    allTransportsStarted.TrySetResult();
                await releaseTransports.Task.WaitAsync(token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var auth = new BearerTokenAuthenticationHandler(token =>
            {
                Interlocked.Increment(ref providerCalls);
                return cached(token);
            }) { InnerHandler = transport };
            using var http = new System.Net.Http.HttpClient(auth);
            var requests = Enumerable.Range(0, 24).Select(async i =>
            {
                using var response = await http.GetAsync("https://example.com/" + i);
            }).ToArray();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, refreshes);
            release.TrySetResult();
            await allTransportsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releaseTransports.TrySetResult();
            await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(24, providerCalls);
            Assert.AreEqual(1, refreshes);
            Assert.AreEqual(24, transport.Requests.Count);
        }

        [TestMethod]
        public async Task Cancelling_waiter_does_not_cancel_another_requests_refresh()
        {
            using var gate = new SemaphoreSlim(1, 1);
            var started = Signal();
            var release = Signal();
            int refreshes = 0;
            var provider = CachedProvider(gate, async token =>
            {
                Interlocked.Increment(ref refreshes);
                started.TrySetResult();
                await release.Task.WaitAsync(token);
                return ("shared", DateTimeOffset.UtcNow.AddHours(1));
            });
            using var transport = Transport();
            using var auth = new BearerTokenAuthenticationHandler(provider) { InnerHandler = transport };
            using var http = new System.Net.Http.HttpClient(auth);
            var first = http.GetAsync("https://example.com/first");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var cancellation = new CancellationTokenSource();
            var waiting = http.GetAsync("https://example.com/waiting", cancellation.Token);
            cancellation.Cancel();
            await ExpectCancellation(waiting);
            Assert.AreEqual(0, transport.Requests.Count);
            release.TrySetResult();
            using var firstResponse = await first.WaitAsync(TimeSpan.FromSeconds(5));
            using var laterResponse = await http.GetAsync("https://example.com/later");
            Assert.AreEqual(1, refreshes);
            Assert.AreEqual(2, transport.Requests.Count);
        }

        [TestMethod]
        public async Task Refresh_failure_propagates_without_anonymous_send_and_can_be_retried()
        {
            using var gate = new SemaphoreSlim(1, 1);
            int refreshes = 0;
            var provider = CachedProvider(gate, _ => Interlocked.Increment(ref refreshes) == 1
                ? Task.FromException<(string, DateTimeOffset)>(new InvalidOperationException("Refresh unavailable."))
                : Task.FromResult(("shared", DateTimeOffset.UtcNow.AddHours(1))));
            using var transport = Transport();
            using var auth = new BearerTokenAuthenticationHandler(provider) { InnerHandler = transport };
            using var http = new System.Net.Http.HttpClient(auth);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => http.GetAsync("https://example.com/first"));
            Assert.AreEqual(0, transport.Requests.Count);
            using var response = await http.GetAsync("https://example.com/retry").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(2, refreshes);
            Assert.AreEqual(1, transport.Requests.Count);
        }

        [TestMethod]
        public async Task Cancelled_refresh_releases_gate_for_other_callers()
        {
            using var gate = new SemaphoreSlim(1, 1);
            var started = Signal();
            int refreshes = 0;
            var provider = CachedProvider(gate, async token =>
            {
                if (Interlocked.Increment(ref refreshes) == 1)
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                return ("shared", DateTimeOffset.UtcNow.AddHours(1));
            });
            using var transport = Transport();
            using var auth = new BearerTokenAuthenticationHandler(provider) { InnerHandler = transport };
            using var http = new System.Net.Http.HttpClient(auth);
            using var cancellation = new CancellationTokenSource();
            var first = http.GetAsync("https://example.com/first", cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var waiting = http.GetAsync("https://example.com/waiting");
            cancellation.Cancel();
            await ExpectCancellation(first);
            using var response = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(2, refreshes);
            Assert.AreEqual(1, transport.Requests.Count);
        }

        private static Func<CancellationToken, Task<string>> CachedProvider(SemaphoreSlim gate,
            Func<CancellationToken, Task<(string AccessToken, DateTimeOffset ExpiresAt)>> refresh)
        {
            string cachedToken = null;
            DateTimeOffset expiresAt = DateTimeOffset.MinValue;
            return async ct =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (cachedToken != null && expiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
                        return cachedToken;
                    var fresh = await refresh(ct).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(fresh.AccessToken) || fresh.ExpiresAt <= DateTimeOffset.UtcNow)
                        throw new InvalidOperationException("Refresh returned an empty or expired token.");
                    cachedToken = fresh.AccessToken;
                    expiresAt = fresh.ExpiresAt;
                    return cachedToken;
                }
                finally
                {
                    gate.Release();
                }
            };
        }

        private static AsyncHttpMessageHandler Transport() => new AsyncHttpMessageHandler(async (request, token) =>
        {
            Assert.AreEqual("shared", request.Headers.Authorization?.Parameter);
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        private static TaskCompletionSource Signal() => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

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
