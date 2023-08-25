using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WebDAVClient.HttpClient
{
    public interface IHttpClientWrapper
    {
        Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption responseHeadersRead, CancellationToken cancellationToken = default);
        Task<HttpResponseMessage> SendUploadAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
    }
}