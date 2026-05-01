using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebDAVClient.UnitTests.Helpers
{
    /// <summary>
    /// Minimal HttpMessageHandler test double. Captures every outgoing request and lets the
    /// test decide how to respond via a delegate. Implemented by hand on purpose so the test
    /// project pulls no third-party mocking dependency.
    /// </summary>
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> m_responder;

        public List<CapturedRequest> Requests { get; } = new List<CapturedRequest>();

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            m_responder = responder ?? throw new ArgumentNullException(nameof(responder));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Materialize headers + body now because the real HttpClient may dispose the request before the
            // test gets a chance to inspect it. ReadAsByteArrayAsync is safe even when no content is set.
            byte[] body = null;
            if (request.Content != null)
            {
                body = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }

            var captured = new CapturedRequest
            {
                Method = request.Method,
                RequestUri = request.RequestUri,
                Headers = CloneHeaders(request),
                ContentHeaders = CloneContentHeaders(request),
                Body = body
            };
            Requests.Add(captured);

            return m_responder(request);
        }

        private static Dictionary<string, string[]> CloneHeaders(HttpRequestMessage request)
        {
            var dict = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                dict[header.Key] = new List<string>(header.Value).ToArray();
            }
            return dict;
        }

        private static Dictionary<string, string[]> CloneContentHeaders(HttpRequestMessage request)
        {
            var dict = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (request.Content == null) return dict;
            foreach (var header in request.Content.Headers)
            {
                dict[header.Key] = new List<string>(header.Value).ToArray();
            }
            return dict;
        }

        public static HttpResponseMessage Multistatus(string xml)
        {
            return new HttpResponseMessage((HttpStatusCode)207)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/xml")
            };
        }

        public static HttpResponseMessage StatusOnly(HttpStatusCode status, string body = null)
        {
            var response = new HttpResponseMessage(status);
            if (body != null)
            {
                response.Content = new StringContent(body, Encoding.UTF8, "text/plain");
            }
            return response;
        }

        public static HttpResponseMessage Stream(HttpStatusCode status, byte[] payload)
        {
            return new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(payload)
            };
        }

        internal sealed class CapturedRequest
        {
            public HttpMethod Method { get; set; }
            public Uri RequestUri { get; set; }
            public Dictionary<string, string[]> Headers { get; set; }
            public Dictionary<string, string[]> ContentHeaders { get; set; }
            public byte[] Body { get; set; }

            public string BodyAsString()
            {
                return Body == null ? null : Encoding.UTF8.GetString(Body);
            }
        }
    }
}
