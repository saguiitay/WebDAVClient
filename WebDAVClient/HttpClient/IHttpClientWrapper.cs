using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WebDAVClient.HttpClient
{
    /// <summary>
    /// Abstraction over <see cref="System.Net.Http.HttpClient"/> used by
    /// <see cref="Client"/> so that a single underlying <c>HttpClient</c>
    /// instance can be reused (avoiding the well-known socket-exhaustion
    /// leak) and so that the client is testable without real network I/O.
    /// Implement this interface to inject a custom HTTP transport.
    /// </summary>
    public interface IHttpClientWrapper
    {
        /// <summary>
        /// Sends a non-upload request (PROPFIND, GET, MKCOL, MOVE, COPY,
        /// DELETE, ...) using the primary <c>HttpClient</c>.
        /// </summary>
        /// <param name="request">The HTTP request to send. The wrapper does not dispose the request — the caller retains ownership.</param>
        /// <param name="responseHeadersRead">When the operation should complete (typically <see cref="HttpCompletionOption.ResponseHeadersRead"/> so that response bodies / streams are read on demand).</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>The HTTP response. The caller is responsible for disposing it (except when its content stream is handed back to the user, as in <see cref="IClient.Download"/>).</returns>
        Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption responseHeadersRead, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends an upload (PUT) request using the upload <c>HttpClient</c>,
        /// which may have a different timeout from the primary client. When
        /// no separate upload client was supplied at construction time, this
        /// falls through to the primary client.
        /// </summary>
        /// <param name="request">The upload HTTP request to send. The wrapper does not dispose the request — the caller retains ownership.</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        /// <returns>The HTTP response. The caller is responsible for disposing it.</returns>
        Task<HttpResponseMessage> SendUploadAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
    }
}