using System.Net.Http;
using System.Threading.Tasks;

namespace WebDAVClient.HttpClient
{
    public interface IHttpClientWrapper
    {
        Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption responseHeadersRead);
        Task<HttpResponseMessage> SendUploadAsync(HttpRequestMessage request);
    }
}