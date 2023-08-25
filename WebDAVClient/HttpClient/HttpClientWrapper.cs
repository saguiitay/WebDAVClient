using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WebDAVClient.HttpClient
{
    public class HttpClientWrapper : IHttpClientWrapper, IDisposable
    {
        private System.Net.Http.HttpClient m_httpClient;
        private System.Net.Http.HttpClient m_uploadHttpClient;
        private bool m_disposedValue;

        public HttpClientWrapper(System.Net.Http.HttpClient httpClient, System.Net.Http.HttpClient uploadHttpClient = null)
        {
            m_httpClient = httpClient;
            m_uploadHttpClient = uploadHttpClient ?? httpClient;
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption responseHeadersRead, CancellationToken cancellationToken = default)
        {
            return m_httpClient.SendAsync(request, responseHeadersRead, cancellationToken);
        }

        public Task<HttpResponseMessage> SendUploadAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            return m_uploadHttpClient.SendAsync(request, cancellationToken);
        }


        #region IDisposable methods
        protected virtual void Dispose(bool disposing)
        {
            if (!m_disposedValue)
            {
                if (disposing)
                {
                    m_httpClient.Dispose();
                    if (m_httpClient != m_uploadHttpClient)
                    {
                        m_uploadHttpClient.Dispose();
                    }
                    m_httpClient = null;
                    m_uploadHttpClient = null;
                }

                m_disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}