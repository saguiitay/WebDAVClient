using System.Net.Http;
using System.Threading.Tasks;

namespace WebDAVClient.HttpClient
{
    public class HttpClientWrapper : IHttpClientWrapper
    {
        private readonly System.Net.Http.HttpClient m_httpClient;
        private readonly System.Net.Http.HttpClient m_uploadHttpClient;

        public HttpClientWrapper(System.Net.Http.HttpClient httpClient, System.Net.Http.HttpClient uploadHttpClient = null)
        {
            m_httpClient = httpClient;
            m_uploadHttpClient = uploadHttpClient ?? httpClient;
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption responseHeadersRead)
        {
            return m_httpClient.SendAsync(request, responseHeadersRead);
        }

        public Task<HttpResponseMessage> SendUploadAsync(HttpRequestMessage request)
        {
            return m_uploadHttpClient.SendAsync(request);
        }
    }
}