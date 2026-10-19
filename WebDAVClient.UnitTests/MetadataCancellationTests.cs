using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Model;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class MetadataCancellationTests
    {
        private const string Response = "<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>/webdav/file</D:href>";
        private const string Property = Response + "<D:propstat><D:prop>";
        private const string ActiveLock = "<D:prop xmlns:D=\"DAV:\"><D:lockdiscovery><D:activelock>";

        [DataTestMethod]
        [DataRow("Discovery", "<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>/webdav/")]
        [DataRow("List", Property + "<D:displayname>name")]
        [DataRow("List", Property + "<x:custom xmlns:x=\"urn:test\"><x:nested/>")]
        [DataRow("ListNamed", Property + "<x:custom xmlns:x=\"urn:test\">value")]
        [DataRow("ListNames", Property + "<D:displayname>value")]
        [DataRow("GetFile", Response + "<D:status>HTTP/1.1 ")]
        [DataRow("GetFolder", Property + "<D:resourcetype><D:collection/>")]
        [DataRow("Lock", ActiveLock + "<D:locktype><D:write/>")]
        [DataRow("Lock", ActiveLock + "<D:lockscope><D:exclusive/>")]
        [DataRow("Lock", ActiveLock + "<D:depth>infinity")]
        [DataRow("Lock", ActiveLock + "<D:owner><x:owner xmlns:x=\"urn:test\">value")]
        [DataRow("Lock", ActiveLock + "<D:locktoken><D:href>opaquelocktoken:test")]
        [DataRow("Refresh", ActiveLock + "<D:timeout>Second-")]
        [DataRow("PropPatch", Response + "<D:propstat><D:status>HTTP/1.1 ")]
        [DataRow("PropPatch", Response + "<D:propstat><D:status>HTTP/1.1 200 OK</D:status><D:responsedescription>text")]
        public async Task Stalled_metadata_observes_cancellation_and_independent_body_deadline(string operation, string prefix)
        {
            foreach (bool useDeadline in new[] { false, true })
            {
                using var stream = new PrefixThenStallStream(prefix);
                var response = new TrackingResponse(operation is "Lock" or "Refresh" ? HttpStatusCode.OK : (HttpStatusCode)207)
                {
                    Content = new StreamContent(stream)
                };
                bool stallNext = operation == "Discovery";
                using var handler = new AsyncHttpMessageHandler((_, _) =>
                {
                    if (stallNext)
                    {
                        stallNext = false;
                        return Task.FromResult<HttpResponseMessage>(response);
                    }
                    return Task.FromResult(StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/")));
                });
                using var http = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
                using var client = new Client(http) { Server = "http://example.com", BasePath = "/webdav/" };
                if (operation != "Discovery")
                {
                    await client.GetFolder().WaitAsync(TimeSpan.FromSeconds(5));
                    stallNext = true;
                }
                client.ResponseBodyTimeout = useDeadline ? TimeSpan.FromMilliseconds(250) : Timeout.InfiniteTimeSpan;
                using var cancellation = new CancellationTokenSource();
                var pending = Invoke(client, operation, cancellation.Token);
                var first = await Task.WhenAny(stream.Stalled.Task, pending).WaitAsync(TimeSpan.FromSeconds(5));
                if (first == pending)
                    await pending;
                await stream.Stalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (!useDeadline)
                    cancellation.Cancel();
                try
                {
                    await pending.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Fail("A stalled metadata body must not report success.");
                }
                catch (OperationCanceledException)
                {
                    Assert.AreEqual(!useDeadline, cancellation.IsCancellationRequested);
                }
                Assert.AreEqual(1, response.DisposeCount);
                Assert.IsTrue(stream.IsDisposed);

                if (operation == "Discovery")
                {
                    await client.GetFolder().WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.AreEqual(3, handler.Requests.Count, "Cancelled discovery must be retried, not cached.");
                }
            }
        }

        [TestMethod]
        public void Body_timeout_defaults_and_validation_are_explicit()
        {
            using var client = new Client();
            Assert.AreEqual(TimeSpan.FromSeconds(100), client.ResponseBodyTimeout);
            foreach (var invalid in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(-2), TimeSpan.MaxValue })
                Assert.ThrowsException<ArgumentOutOfRangeException>(() => client.ResponseBodyTimeout = invalid);
            client.ResponseBodyTimeout = Timeout.InfiniteTimeSpan;
            Assert.AreEqual(Timeout.InfiniteTimeSpan, client.ResponseBodyTimeout);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Stream_acquisition_is_cancellable_and_disposes_response(bool useDeadline)
        {
            using var content = new StallingStreamFactoryContent();
            var response = new TrackingResponse((HttpStatusCode)207) { Content = content };
            int requests = 0;
            using var handler = new AsyncHttpMessageHandler((_, _) => Task.FromResult(
                Interlocked.Increment(ref requests) <= 2
                    ? StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/")) : response));
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new Client(http) { Server = "http://example.com", BasePath = "/webdav/" };
            await client.GetFolder().WaitAsync(TimeSpan.FromSeconds(5));
            client.ResponseBodyTimeout = useDeadline ? TimeSpan.FromMilliseconds(250) : Timeout.InfiniteTimeSpan;
            using var cancellation = new CancellationTokenSource();
            var pending = client.GetFile("file", cancellationToken: cancellation.Token);
            await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!useDeadline)
                cancellation.Cancel();
            try
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Fail("Expected stream acquisition cancellation.");
            }
            catch (OperationCanceledException) { }
            Assert.AreEqual(1, response.DisposeCount);
            Assert.IsTrue(content.IsDisposed);
        }

        private static Task Invoke(Client client, string operation, CancellationToken token) => operation switch
        {
            "Discovery" or "GetFolder" => client.GetFolder(cancellationToken: token),
            "GetFile" => client.GetFile("file", cancellationToken: token),
            "List" => client.List(cancellationToken: token),
            "ListNamed" => client.List("/", 1, new[] { new PropertyName("urn:test", "custom") }, token),
            "ListNames" => client.ListPropertyNames(cancellationToken: token),
            "Lock" => client.LockFile("file", cancellationToken: token),
            "Refresh" => client.RefreshLock("file", "opaquelocktoken:test", cancellationToken: token),
            "PropPatch" => client.SetProperty("file", "name", "urn:test", "value", token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

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

        private sealed class StallingStreamFactoryContent : HttpContent
        {
            public TaskCompletionSource Started { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool IsDisposed { get; private set; }

            protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new AssertFailedException("The factory must be cancelled.");
            }

            protected override bool TryComputeLength(out long length) { length = 0; return false; }
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => throw new NotSupportedException();
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    IsDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
