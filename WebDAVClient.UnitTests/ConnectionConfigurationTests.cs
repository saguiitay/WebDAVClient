using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebDAVClient.UnitTests.Helpers;

namespace WebDAVClient.UnitTests
{
    [TestClass]
    public class ConnectionConfigurationTests
    {
        [DataTestMethod]
        [DataRow("Server", false)]
        [DataRow("BasePath", false)]
        [DataRow("Port", false)]
        [DataRow("Server", true)]
        [DataRow("BasePath", true)]
        [DataRow("Port", true)]
        public async Task Reconfiguration_resolves_the_new_root(string setting, bool absolute)
        {
            var calls = 0;
            using var harness = new ClientHarness(request =>
            {
                var href = absolute ? request.RequestUri.AbsoluteUri : request.RequestUri.AbsolutePath;
                return StubHttpMessageHandler.Multistatus(++calls % 2 == 1
                    ? WebDAVResponses.Root(href) : WebDAVResponses.SingleFile(href));
            }, "https://example.com", "/webdav/");
            await harness.Client.GetFile("file.txt");

            Change(harness.Client, setting);
            await harness.Client.GetFile("file.txt");

            var expected = ExpectedRoot(setting);
            Assert.AreEqual(4, harness.Handler.Requests.Count);
            Assert.AreEqual(expected, harness.Handler.Requests[2].RequestUri.AbsoluteUri);
            Assert.AreEqual(expected + "file.txt", harness.Handler.Requests[3].RequestUri.AbsoluteUri);
        }

        [DataTestMethod]
        [DataRow("Server")]
        [DataRow("BasePath")]
        [DataRow("Port")]
        public async Task Inflight_discovery_cannot_publish_an_obsolete_root(string setting)
        {
            using var handler = new DelayedDiscoveryHandler();
            using var http = new System.Net.Http.HttpClient(handler);
            using var client = new Client(http) { Server = "https://example.com", BasePath = "/webdav/" };
            var operation = client.GetFile("file.txt");
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Change(client, setting);
            handler.Release.TrySetResult(true);
            await operation.WaitAsync(TimeSpan.FromSeconds(5));

            var expected = ExpectedRoot(setting);
            Assert.AreEqual(3, handler.Requests.Count);
            Assert.AreEqual(expected, handler.Requests[1].AbsoluteUri);
            Assert.AreEqual(expected + "file.txt", handler.Requests[2].AbsoluteUri);
        }

        [TestMethod]
        public async Task Unchanged_normalized_configuration_keeps_the_cached_root()
        {
            using var harness = new ClientHarness(request =>
                StubHttpMessageHandler.Multistatus(WebDAVResponses.Root(request.RequestUri.AbsolutePath)),
                "https://example.com", "/webdav/");
            await harness.Client.GetFile("file.txt");
            harness.Client.Server = "https://example.com/";
            harness.Client.BasePath = "webdav";
            harness.Client.Port = null;

            await harness.Client.GetFile("file.txt");

            Assert.AreEqual(3, harness.Handler.Requests.Count);
        }

        private static void Change(Client client, string setting)
        {
            switch (setting)
            {
                case "Server": client.Server = "https://other.example"; break;
                case "BasePath": client.BasePath = "/other/"; break;
                case "Port": client.Port = 8443; break;
                default: throw new ArgumentException("Unknown setting.", nameof(setting));
            }
        }

        private static string ExpectedRoot(string setting) => setting switch
        {
            "Server" => "https://other.example/webdav/",
            "BasePath" => "https://example.com/other/",
            "Port" => "https://example.com:8443/webdav/",
            _ => throw new ArgumentException("Unknown setting.", nameof(setting))
        };

        private sealed class DelayedDiscoveryHandler : HttpMessageHandler
        {
            public TaskCompletionSource<bool> Started { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Release { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public List<Uri> Requests { get; } = new List<Uri>();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request.RequestUri);
                if (Requests.Count == 1)
                {
                    Started.TrySetResult(true);
                    await Release.Task.WaitAsync(cancellationToken);
                }
                return StubHttpMessageHandler.Multistatus(
                    WebDAVResponses.Root(request.RequestUri.AbsoluteUri));
            }
        }
    }
}
