using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class SampleTests
    {
        [DataTestMethod]
        [DataRow("empty", 0)]
        [DataRow("success", 0)]
        [DataRow("missing-target", 1)]
        [DataRow("upload-failure", 1)]
        [DataRow("locked-failure", 1)]
        [DataRow("not-created", 1)]
        [DataRow("cancelled", 130)]
        [DataRow("cleanup-failure", 1)]
        public async Task Sample_is_readonly_by_default_and_cleans_only_confirmed_owned_resources(string scenario, int expectedExit)
        {
            using var cancellation = new CancellationTokenSource();
            var uploading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool mutate = scenario != "empty" && scenario != "missing-target";
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                string method = request.Method.Method;
                if (method == "PROPFIND")
                    return StubHttpMessageHandler.Multistatus(request.Headers.GetValues("Depth").Single() == "0"
                        ? WebDAVResponses.Root("/webdav/") : "<D:multistatus xmlns:D=\"DAV:\"/>");
                if (method == "OPTIONS")
                {
                    var options = new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new StringContent("") };
                    if (scenario == "locked-failure")
                        options.Content.Headers.Allow.Add("LOCK");
                    return options;
                }
                if (method == "MKCOL" && scenario == "not-created")
                    return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
                if (method == "PUT")
                {
                    if (scenario == "cancelled")
                    {
                        uploading.TrySetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    }
                    if (scenario == "upload-failure" || (scenario == "locked-failure" && request.Content.Headers.ContentRange != null))
                        return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }
                if (method == "LOCK")
                {
                    var locked = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
                    locked.Headers.Add("Lock-Token", "<opaquelocktoken:sample>");
                    return locked;
                }
                if (method == "UNLOCK" || method == "DELETE")
                {
                    Assert.IsFalse(token.IsCancellationRequested, "Cleanup must not reuse the cancelled operation token.");
                    return new HttpResponseMessage(scenario == "cleanup-failure" ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent);
                }
                if (method == "GET")
                {
                    if (scenario == "missing-target")
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    var content = new ByteArrayContent(new byte[] { 0xF0, 0x0B, 0xA5 });
                    content.Headers.ContentRange = new ContentRangeHeaderValue(2, 4, 8);
                    return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
                }
                return new HttpResponseMessage(HttpStatusCode.Created);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new Client(http) { Server = "http://example.com", BasePath = "/webdav/" };
            using var output = new StringWriter();
            using var error = new StringWriter();
            var pending = TestWebDAVClient.Program.RunAsync(client, mutate,
                scenario == "missing-target" ? "missing" : null, output, error, cancellation.Token);
            if (scenario == "cancelled")
            {
                await uploading.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancellation.Cancel();
            }
            Assert.AreEqual(expectedExit, await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            var requests = handler.Requests.ToArray();
            Assert.IsTrue(output.ToString().Contains("No items found"));
            var deletes = requests.Where(r => r.Method == "DELETE").ToArray();
            if (!mutate || scenario == "not-created")
                Assert.AreEqual(0, deletes.Length);
            else
            {
                Assert.AreEqual(1, deletes.Length);
                var created = requests.Single(r => r.Method == "MKCOL").Uri.AbsolutePath.TrimEnd('/');
                StringAssert.Contains(created, "/webdav/webdavclient-sample-");
                Assert.AreEqual(created, deletes[0].Uri.AbsolutePath.TrimEnd('/'));
            }
            if (scenario == "locked-failure")
                Assert.IsTrue(Array.FindIndex(requests, r => r.Method == "UNLOCK") < Array.FindIndex(requests, r => r.Method == "DELETE"));
            if (scenario == "missing-target")
                StringAssert.Contains(error.ToString(), "not found");
            if (scenario == "cleanup-failure")
                StringAssert.Contains(error.ToString(), "manual cleanup");
        }
    }
}
