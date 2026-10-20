using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.UnitTests.Helpers;
using static WebDAVClient.UnitTests.DavClientTests;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class ModernSampleTests
    {
        [DataTestMethod]
        [DataRow("read-only", 0)]
        [DataRow("read-target", 0)]
        [DataRow("success", 0)]
        [DataRow("bad-listing", 1)]
        [DataRow("not-created", 1)]
        [DataRow("upload-failure", 1)]
        [DataRow("download-failure", 1)]
        [DataRow("bad-bytes", 1)]
        [DataRow("cancelled", 130)]
        [DataRow("cleanup-failure", 1)]
        [DataRow("timeout", 1)]
        [DataRow("transport-failure", 1)]
        public async Task Modern_sample_checks_outcomes_and_cleans_only_confirmed_run_owned_resources(string scenario, int expected)
        {
            using var cancellation = new CancellationTokenSource();
            var uploading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool mutate = scenario != "read-only" && scenario != "read-target";
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                switch (request.Method.Method)
                {
                    case "PROPFIND":
                        if (scenario == "transport-failure")
                        {
                            throw new HttpRequestException("Connection failed.");
                        }
                        if (scenario == "timeout")
                        {
                            throw new OperationCanceledException("Metadata deadline.");
                        }
                        return scenario == "bad-listing" ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                            : XmlResponse(Multi(Resource()));
                    case "MKCOL":
                        return new HttpResponseMessage(scenario == "not-created" ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.Created);
                    case "PUT":
                        Assert.AreEqual("*", request.Headers.IfNoneMatch.Single().Tag);
                        Assert.IsNull(request.Content.Headers.ContentRange);
                        if (scenario == "cancelled")
                        {
                            uploading.TrySetResult();
                            await Task.Delay(Timeout.InfiniteTimeSpan, token);
                        }
                        return new HttpResponseMessage(scenario == "upload-failure" ? HttpStatusCode.Forbidden : HttpStatusCode.Created);
                    case "GET":
                        return new HttpResponseMessage(scenario == "download-failure" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                        {
                            Content = new StringContent(scenario == "bad-bytes" ? "incorrect" : "abcdefgh")
                        };
                    case "DELETE":
                        Assert.IsFalse(token.IsCancellationRequested);
                        return new HttpResponseMessage(scenario == "cleanup-failure" ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent);
                    default:
                        throw new AssertFailedException("Unexpected method: " + request.Method);
                }
            });
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new DavClient(Endpoint, http);
            using var output = new StringWriter();
            using var error = new StringWriter();
            var pending = TestWebDAVClient.Program.RunModernAsync(client, mutate,
                scenario == "read-target" ? "existing" : null, output, error, cancellation.Token);
            if (scenario == "cancelled")
            {
                await uploading.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancellation.Cancel();
            }
            Assert.AreEqual(expected, await pending.WaitAsync(TimeSpan.FromSeconds(5)), error.ToString());
            var requests = handler.Requests.ToArray();
            var deletes = requests.Where(request => request.Method == "DELETE").ToArray();
            if (!mutate || scenario == "not-created" || scenario == "bad-listing" || scenario == "timeout" || scenario == "transport-failure")
            {
                Assert.AreEqual(0, deletes.Length);
            }
            else
            {
                Assert.AreEqual(1, deletes.Length);
                var created = requests.Single(request => request.Method == "MKCOL").Uri;
                Assert.AreEqual(created, deletes[0].Uri);
                StringAssert.Contains(created.AbsolutePath, "/dav/webdavclient-sample-");
            }
            if (expected == 0)
            {
                Assert.AreEqual("", error.ToString());
                StringAssert.Contains(output.ToString(), "Resource:");
            }
            else
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(error.ToString()));
            }
            if (scenario == "read-target")
            {
                StringAssert.Contains(output.ToString(), "Requested target read successfully.");
            }
            if (scenario == "cleanup-failure")
            {
                StringAssert.Contains(error.ToString(), "manual cleanup");
            }
        }
    }
}
