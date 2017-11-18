using System.Net.Http;
using System.Threading.Tasks;

namespace WebDAVClient.HttpClient
{
    public class HttpClientWrapper : IHttpClientWrapper
    {
        private readonly System.Net.Http.HttpClient _httpClient;
        private readonly System.Net.Http.HttpClient _uploadHttpClient;

        public HttpClientWrapper(System.Net.Http.HttpClient httpClient, System.Net.Http.HttpClient uploadHttpClient)
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