using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WebDAVClient.HttpClient
{
    /// <summary>
    /// Default <see cref="IHttpClientWrapper"/> implementation that delegates
    /// to one or two <see cref="System.Net.Http.HttpClient"/> instances —
    /// the primary client for regular requests and an optional second
    /// client for uploads (so an upload-specific timeout can be applied
    /// without affecting the primary client).
    /// </summary>
    public class HttpClientWrapper : IHttpClientWrapper, IDisposable
    {
        private System.Net.Http.HttpClient m_httpClient;
        private System.Net.Http.HttpClient m_uploadHttpClient;
        private bool m_disposedValue;

        /// <summary>
        /// Initialises a new <see cref="HttpClientWrapper"/>.
        /// </summary>
        /// <param name="httpClient">The primary <see cref="System.Net.Http.HttpClient"/> used for non-upload requests.</param>
        /// <param name="uploadHttpClient">Optional secondary <see cref="System.Net.Http.HttpClient"/> used for upload requests. When <c>null</c>, <paramref name="httpClient"/> is used for uploads as well.</param>
        public HttpClientWrapper(System.Net.Http.HttpClient httpClient, System.Net.Http.HttpClient uploadHttpClient = null)
        {
            m_httpClient = httpClient;
            m_uploadHttpClient = uploadHttpClient ?? httpClient;
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption responseHeadersRead, CancellationToken cancellationToken = default)
        {
            return m_httpClient.SendAsync(request, responseHeadersRead, cancellationToken);
        }

        /// <inheritdoc />
        public Task<HttpResponseMessage> SendUploadAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            return m_uploadHttpClient.SendAsync(request, cancellationToken);
        }


        #region IDisposable methods
        /// <summary>
        /// Releases the unmanaged resources used by the
        /// <see cref="HttpClientWrapper"/> and, when <paramref name="disposing"/>
        /// is <c>true</c>, the managed <see cref="System.Net.Http.HttpClient"/>
        /// instances it owns.
        /// </summary>
        /// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>; <c>false</c> when called from a finaliser.</param>
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

        /// <summary>
        /// Disposes the wrapper and the underlying
        /// <see cref="System.Net.Http.HttpClient"/> instance(s) it owns.
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}