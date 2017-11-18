using System.Net.Http;
using System.Threading.Tasks;

namespace WebDAVClient
{
    public class HttpClientWrapper : IHttpClientWrapper
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClient _uploadHttpClient;

        public HttpClientWrapper(HttpClient httpClient, HttpClient uploadHttpClient)
        {
            _httpClient = httpClient;
            _uploadHttpClient = uploadHttpClient;
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption responseHeadersRead)
        {
            return _httpClient.SendAsync(request, responseHeadersRead);
        }

        public Task<HttpResponseMessage> SendUploadAsync(HttpRequestMessage request)
        {
            return _uploadHttpClient.SendAsync(request);
        }
    }
}