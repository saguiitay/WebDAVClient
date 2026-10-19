using System;
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
    public class ExceptionContextTests
    {
        [TestMethod]
        public async Task Concurrent_failures_keep_distinct_context_without_credentials_queries_fragments_or_headers()
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int failures = 0;
            using var handler = new AsyncHttpMessageHandler(async (request, token) =>
            {
                if (request.Method.Method == "PROPFIND")
                    return StubHttpMessageHandler.Multistatus(WebDAVResponses.Root("/webdav/"));
                if (Interlocked.Increment(ref failures) == 2)
                    release.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                return new HttpResponseMessage(request.Method == HttpMethod.Get ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict);
            });
            using var http = new System.Net.Http.HttpClient(handler);
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer example-header-secret");
            using var client = new Client(http) { Server = "http://example.com", BasePath = "/webdav/" };
            await client.GetFolder();
            var exceptions = await Task.WhenAll(
                Capture(() => client.Download("http://example-user:example-password@example.com/webdav/one?token=example-query#example-fragment")),
                Capture(() => client.DeleteFile("http://example-user:example-password@example.com/webdav/two?token=example-query#example-fragment")));
            Assert.AreEqual("Download", exceptions[0].Operation);
            Assert.AreEqual("GET", exceptions[0].Method);
            Assert.AreEqual("http://example.com/webdav/one", exceptions[0].ResourceUri.AbsoluteUri);
            Assert.AreEqual("Delete", exceptions[1].Operation);
            Assert.AreEqual("DELETE", exceptions[1].Method);
            Assert.AreEqual("http://example.com/webdav/two", exceptions[1].ResourceUri.AbsoluteUri);
            Assert.IsInstanceOfType<WebDAVConflictException>(exceptions[1]);
            foreach (var exception in exceptions)
                foreach (var secret in new[] { "example-user", "example-password", "example-query", "example-fragment", "example-header-secret" })
                    Assert.IsFalse(exception.ToString().Contains(secret), secret);
        }

        [TestMethod]
        public async Task Parsed_resource_failure_keeps_subtype_status_and_request_context()
        {
            int calls = 0;
            using var harness = new ClientHarness(_ => StubHttpMessageHandler.Multistatus(++calls == 1
                ? WebDAVResponses.Root("/webdav/")
                : """
                  <D:multistatus xmlns:D="DAV:"><D:response>
                  <D:href>/webdav/file</D:href><D:status>HTTP/1.1 409 Conflict</D:status>
                  </D:response></D:multistatus>
                  """));
            var exception = await Assert.ThrowsExceptionAsync<WebDAVConflictException>(() => harness.Client.GetFile("file"));
            Assert.AreEqual(409, exception.GetHttpCode());
            Assert.AreEqual("PROPFIND", exception.Method);
            Assert.AreEqual("GetMetadata", exception.Operation);
            Assert.AreEqual("/webdav/file", exception.ResourceUri.AbsolutePath);
        }

        [TestMethod]
        public void Existing_exception_constructors_keep_status_and_inner_exception_without_synthesized_context()
        {
            var inner = new InvalidOperationException("example");
            var exception = new WebDAVException(403, "rejected", inner);
            Assert.AreEqual(403, exception.GetHttpCode());
            Assert.AreSame(inner, exception.InnerException);
            Assert.IsNull(exception.Operation);
            Assert.IsNull(exception.Method);
            Assert.IsNull(exception.ResourceUri);
        }

        [TestMethod]
        public void Missing_or_relative_request_context_does_not_invent_a_resource_uri()
        {
            var error = new WebDAVException(403, "rejected");
            Assert.AreSame(error, error.WithRequestContext(null));
            Assert.IsNull(error.Operation);
            using var relative = new HttpRequestMessage(HttpMethod.Get, new Uri("file?token=example", UriKind.Relative));
            Assert.AreSame(error, error.WithRequestContext(relative));
            Assert.AreEqual("GET", error.Operation);
            Assert.AreEqual("GET", error.Method);
            Assert.IsNull(error.ResourceUri);
            using var missing = new HttpRequestMessage { Method = HttpMethod.Delete };
            error.WithRequestContext(missing);
            Assert.AreEqual("DELETE", error.Operation);
            Assert.IsNull(error.ResourceUri);
        }

        private static async Task<WebDAVException> Capture(Func<Task> operation)
        {
            try
            {
                await operation();
                throw new AssertFailedException("Expected a WebDAVException.");
            }
            catch (WebDAVException exception)
            {
                return exception;
            }
        }
    }
}
