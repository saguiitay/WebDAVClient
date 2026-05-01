using System;
using System.Net.Http;
using WebDAVClient.HttpClient;

namespace WebDAVClient.UnitTests.Helpers
{
    /// <summary>
    /// Builder that produces a fully-wired Client backed by a StubHttpMessageHandler so tests can
    /// inspect requests and inject canned responses without touching the network.
    /// </summary>
    internal sealed class ClientHarness : IDisposable
    {
        public StubHttpMessageHandler Handler { get; }
        public System.Net.Http.HttpClient HttpClient { get; }
        public HttpClientWrapper Wrapper { get; }
        public Client Client { get; }

        public ClientHarness(Func<HttpRequestMessage, HttpResponseMessage> responder, string server = "http://example.com", string basePath = "/webdav/")
        {
            Handler = new StubHttpMessageHandler(responder);
            HttpClient = new System.Net.Http.HttpClient(Handler);
            Wrapper = new HttpClientWrapper(HttpClient);
            Client = new Client(Wrapper)
            {
                Server = server,
                BasePath = basePath
            };
        }

        public void Dispose()
        {
            Client.Dispose();
            HttpClient.Dispose();
            Handler.Dispose();
        }
    }
}
