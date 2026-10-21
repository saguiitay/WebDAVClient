using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    public class MetadataConcurrencyTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public async Task Delayed_metadata_bodies_leave_workers_available_for_unrelated_tasks()
        {
            const int concurrency = 32;
            var streams = new ConcurrentBag<PrefixThenStallStream>();
            var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool delayBodies = false;
            int started = 0;
            using var handler = new AsyncHttpMessageHandler((_, _) =>
            {
                if (!delayBodies)
                {
                    return Task.FromResult(StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/")));
                }

                var stream = new PrefixThenStallStream("<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>/webdav/file");
                streams.Add(stream);
                if (Interlocked.Increment(ref started) == concurrency)
                {
                    allStarted.TrySetResult();
                }

                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)207) { Content = new StreamContent(stream) });
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new Client(http) { Server = "http://example.com", BasePath = "/webdav/" };
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await client.GetFolder(cancellationToken: cancellation.Token);
            ThreadPool.GetAvailableThreads(out int workersBefore, out _);
            delayBodies = true;
            var pending = Enumerable.Range(0, concurrency)
                .Select(i => client.GetFile("file" + i, cancellationToken: cancellation.Token)).ToArray();
            try
            {
                await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.WhenAll(streams.Select(s => s.Stalled.Task)).WaitAsync(TimeSpan.FromSeconds(5));
                ThreadPool.GetAvailableThreads(out int workersWhileWaiting, out _);
                var timer = Stopwatch.StartNew();
                await Task.Run(() => { }).WaitAsync(TimeSpan.FromSeconds(5));
                timer.Stop();
                TestContext.WriteLine($"METADATA concurrency={concurrency}; available_workers_before={workersBefore}; available_workers_waiting={workersWhileWaiting}; thread_pool_threads={ThreadPool.ThreadCount}; unrelated_task_ms={timer.Elapsed.TotalMilliseconds:F3}");
            }
            finally
            {
                cancellation.Cancel();
                await Task.WhenAll(pending.Select(async operation =>
                {
                    try
                    {
                        await operation.WaitAsync(TimeSpan.FromSeconds(5));
                        Assert.Fail("A delayed response should require cancellation.");
                    }
                    catch (OperationCanceledException) { }
                }));
            }
            Assert.AreEqual(concurrency, streams.Count);
            Assert.IsTrue(streams.All(s => s.IsDisposed));
        }
    }
}
