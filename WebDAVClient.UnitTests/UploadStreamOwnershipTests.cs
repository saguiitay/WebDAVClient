using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.Helpers;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class UploadStreamOwnershipTests
    {
        [DataTestMethod]
        [DataRow(false, 201)]
        [DataRow(true, 201)]
        [DataRow(false, 403)]
        [DataRow(true, 403)]
        public async Task Created_upload_request_disposes_input_on_success_and_failure(bool partial, int status)
        {
            using var input = new MemoryStream(new byte[1]);
            using var harness = new ClientHarness(request =>
                request.Method.Method == "PROPFIND"
                    ? StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/"))
                    : StubHttpMessageHandler.StatusOnly((HttpStatusCode)status));
            var pending = Upload(harness.Client, input, partial);
            if (status == 403)
                await Assert.ThrowsExceptionAsync<WebDAVException>(() => pending);
            else
                Assert.IsTrue(await pending);
            Assert.IsFalse(input.CanRead);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Cancelled_upload_disposes_owned_input(bool partial)
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                if (request.Method.Method == "PUT")
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                return StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/"));
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new Client(http) { Server = "http://example.com", BasePath = "/webdav/" };
            using var input = new MemoryStream(new byte[1]);
            using var cancellation = new CancellationTokenSource();
            var pending = Upload(client, input, partial, cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            try
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Fail("Expected cancellation.");
            }
            catch (OperationCanceledException) { }
            Assert.IsFalse(input.CanRead);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Discovery_failure_leaves_input_with_caller(bool partial)
        {
            using var input = new MemoryStream(new byte[1]);
            using var harness = new ClientHarness(_ => StubHttpMessageHandler.StatusOnly(HttpStatusCode.Forbidden));
            await Assert.ThrowsExceptionAsync<WebDAVException>(() => Upload(harness.Client, input, partial));
            Assert.IsTrue(input.CanRead);
        }

        private static Task<bool> Upload(Client client, Stream stream, bool partial, CancellationToken token = default)
            => partial ? client.UploadPartial("/", stream, "file", 0, 1, cancellationToken: token)
                : client.Upload("/", stream, "file", cancellationToken: token);
    }
}
