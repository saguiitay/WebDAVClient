using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WebDAVClient.UnitTests.Helpers
{
    internal sealed class AsyncHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> m_responder;
        public ConcurrentQueue<RequestSnapshot> Requests { get; } = new ConcurrentQueue<RequestSnapshot>();

        public AsyncHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            m_responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue(new RequestSnapshot(request.Method.Method, request.RequestUri,
                request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase)));
            return m_responder(request, cancellationToken);
        }

        internal sealed record RequestSnapshot(string Method, Uri Uri, IReadOnlyDictionary<string, string[]> Headers);
    }
}
