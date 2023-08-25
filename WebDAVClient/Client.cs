using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebDAVClient.Helpers;
using WebDAVClient.HttpClient;
using WebDAVClient.Model;

namespace WebDAVClient
{
    public class Client : IClient
    {
        private static readonly HttpMethod m_propFindMethod = new HttpMethod("PROPFIND");
        private static readonly HttpMethod m_moveMethod = new HttpMethod("MOVE");
        private static readonly HttpMethod m_copyMethod = new HttpMethod("COPY");

        private static readonly HttpMethod m_mkColMethod = new HttpMethod(WebRequestMethods.Http.MkCol);

        private static readonly string m_assemblyVersion = typeof(IClient).Assembly.GetName().Version.ToString();
        private static readonly ProductInfoHeaderValue s_defaultUserAgent = new ProductInfoHeaderValue("WebDAVClient", m_assemblyVersion);

        private const int c_httpStatusCode_MultiStatus = 207;

        // http://webdav.org/specs/rfc4918.html#METHOD_PROPFIND
        private const string c_propFindRequestContent =
            "<?xml version=\"1.0\" encoding=\"utf-8\" ?>" +
            "<propfind xmlns=\"DAV:\">" +
            "<allprop/>" +
            //"  <propname/>" +
            //"  <prop>" +
            //"    <creationdate/>" +
            //"    <getlastmodified/>" +
            //"    <displayname/>" +
            //"    <getcontentlength/>" +
            //"    <getcontenttype/>" +
            //"    <getetag/>" +
            //"    <resourcetype/>" +
            //"  </prop> " +
            "</propfind>";
        private static readonly byte[] s_propFindRequestContentBytes = Encoding.UTF8.GetBytes(c_propFindRequestContent);

        private IHttpClientWrapper m_httpClientWrapper;
        private readonly bool m_shouldDispose;
        private string m_server;
        private string m_basePath = "/";
        private string m_encodedBasePath;
        private bool m_disposedValue;

        #region WebDAV connection parameters

        /// <summary>
        /// Specify the WebDAV hostname (required).
        /// </summary>
        public string Server
        {
            get { return m_server; }
            set
            {
                value = value.TrimEnd('/');
                m_server = value;
            }
        }

        /// <summary>
        /// Specify the path of a WebDAV directory to use as 'root' (default: /)
        /// </summary>
        public string BasePath
        {
            get { return m_basePath; }
            set
            {
                value = value.Trim('/');
                if (string.IsNullOrEmpty(value))
                    m_basePath = "/";
                else
                    m_basePath = "/" + value + "/";
            }
        }

        /// <summary>
        /// Specify a port to use
        /// </summary>
        public int? Port { get; set; }

        /// <summary>
        /// Specify the UserAgent (and UserAgent version) string to use in requests
        /// </summary>
        public string UserAgent { get; set; }

        /// <summary>
        /// Specify the UserAgent (and UserAgent version) string to use in requests
        /// </summary>
        public string UserAgentVersion { get; set; }

        /// <summary>
        /// Specify additional headers to be sent with every request
        /// </summary>
        public ICollection<KeyValuePair<string, string>> CustomHeaders { get; set; }

        /// <summary>
        /// Specify the certificates validation logic
        /// </summary>
        public RemoteCertificateValidationCallback ServerCertificateValidationCallback { get; set; }
        #endregion

        public Client(NetworkCredential credential = null, TimeSpan? uploadTimeout = null, IWebProxy proxy = null)
        {
            var handler = new HttpClientHandler();
            if (proxy != null && handler.SupportsProxy)
            {
                handler.Proxy = proxy;
            }
            if (handler.SupportsAutomaticDecompression)
            {
                handler.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
            }
            if (credential != null)
            {
                handler.Credentials = credential;
                handler.PreAuthenticate = true;
            }
            else
            {
                handler.UseDefaultCredentials = true;
            }

            var client = new System.Net.Http.HttpClient(handler, disposeHandler: true);
            client.DefaultRequestHeaders.ExpectContinue = false;

            System.Net.Http.HttpClient uploadClient = null;
            if (uploadTimeout != null)
            {
                uploadClient = new System.Net.Http.HttpClient(handler, disposeHandler: true);
                uploadClient.DefaultRequestHeaders.ExpectContinue = false;
                uploadClient.Timeout = uploadTimeout.Value;
            }

            m_httpClientWrapper = new HttpClientWrapper(client, uploadClient ?? client);
            m_shouldDispose = true;
        }

        public Client(System.Net.Http.HttpClient httpClient)
        {
            m_httpClientWrapper = new HttpClientWrapper(httpClient, httpClient);
        }

        public Client(IHttpClientWrapper httpClientWrapper)
        {
            m_httpClientWrapper = httpClientWrapper;
        }

        #region WebDAV operations

        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <param name="path">List only files in this path</param>
        /// <param name="depth">Recursion depth</param>
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
        public async Task<IEnumerable<Item>> List(string path = "/", int? depth = 1, CancellationToken cancellationToken = default)
        {
            var listUri = await GetServerUrl(path, true).ConfigureAwait(false);

            // Depth header: http://webdav.org/specs/rfc4918.html#rfc.section.9.1.4
            IDictionary<string, string> headers = new Dictionary<string, string>();
            if (depth != null)
            {
                headers.Add("Depth", depth.ToString());
            }

            if (CustomHeaders != null)
            {
                foreach (var keyValuePair in CustomHeaders)
                {
                    headers.Add(keyValuePair);
                }
            }

            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(listUri.Uri, m_propFindMethod, headers, s_propFindRequestContentBytes, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    (int) response.StatusCode != c_httpStatusCode_MultiStatus)
                {
                    throw new WebDAVException((int) response.StatusCode, "Failed retrieving items in folder.");
                }

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var items = ResponseParser.ParseItems(stream);

                    if (items == null)
                    {
                        throw new WebDAVException("Failed deserializing data returned from server.");
                    }

                    var listUrl = listUri.ToString();

                    var result = new List<Item>(items.Count());
                    foreach (var item in items)
                    {
                        // If it's not a collection, add it to the result
                        if (!item.IsCollection)
                        {
                            result.Add(item);
                        }
                        else
                        {
                            // If it's not the requested parent folder, add it to the result
                            var fullHref = await GetServerUrl(item.Href, true).ConfigureAwait(false);
                            if (!string.Equals(fullHref.ToString(), listUrl, StringComparison.CurrentCultureIgnoreCase))
                            {
                                result.Add(item);
                            }
                        }
                    }
                    return result;
                }
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <returns>An item representing the retrieved folder</returns>
        public async Task<Item> GetFolder(string path = "/", CancellationToken cancellationToken = default)
        {
            var listUri = await GetServerUrl(path, true).ConfigureAwait(false);
            return await Get(listUri.Uri, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <returns>An item representing the retrieved file</returns>
        public async Task<Item> GetFile(string path = "/", CancellationToken cancellationToken = default)
        {
            var listUri = await GetServerUrl(path, false).ConfigureAwait(false);
            return await Get(listUri.Uri, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Download a file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <returns>A stream with the content of the downloaded file</returns>
        public Task<Stream> Download(string remoteFilePath, CancellationToken cancellationToken = default)
        {
            var headers = new Dictionary<string, string> { { "translate", "f" } };
            if (CustomHeaders != null)
            {
                foreach (var keyValuePair in CustomHeaders)
                {
                    headers.Add(keyValuePair.Key, keyValuePair.Value);
                }
            }
            return DownloadFile(remoteFilePath, headers, cancellationToken);
        }

        /// <summary>
        /// Download a part of file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="startBytes">Start bytes of content</param>
        /// <param name="endBytes">End bytes of content</param>
        /// <returns>A stream with the partial content of the downloaded file</returns>
        public Task<Stream> DownloadPartial(string remoteFilePath, long startBytes, long endBytes, CancellationToken cancellationToken = default)
        {
            var headers = new Dictionary<string, string> { { "translate", "f" }, { "Range", "bytes=" + startBytes + "-" + endBytes } };
            if (CustomHeaders != null)
            {
                foreach (var keyValuePair in CustomHeaders)
                {
                    headers.Add(keyValuePair.Key, keyValuePair.Value);
                }
            }
            return DownloadFile(remoteFilePath, headers, cancellationToken);
        }

        /// <summary>
        /// Upload a file to the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="content"></param>
        /// <param name="name"></param>
        /// <returns>True if the file was uploaded successfully. False otherwise</returns>
        public async Task<bool> Upload(string remoteFilePath, Stream content, string name, CancellationToken cancellationToken = default)
        {
            // Should not have a trailing slash.
            var uploadUri = await GetServerUrl(remoteFilePath.TrimEnd('/') + "/" + name.TrimStart('/'), false).ConfigureAwait(false);

            IDictionary<string, string> headers = new Dictionary<string, string>();
            if (CustomHeaders != null)
            {
                foreach (var keyValuePair in CustomHeaders)
                {
                    headers.Add(keyValuePair);
                }
            }

            HttpResponseMessage response = null;

            try
            {
                response = await HttpUploadRequest(uploadUri.Uri, HttpMethod.Put, content, headers, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    response.StatusCode != HttpStatusCode.NoContent &&
                    response.StatusCode != HttpStatusCode.Created)
                {
                    throw new WebDAVException((int) response.StatusCode, "Failed uploading file.");
                }

                return response.IsSuccessStatusCode;
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// Partial upload a part of file to the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="content">Partial content to update</param>
        /// <param name="name">Name of the file to update</param>
        /// <param name="startBytes">Start byte position of the target content</param>
        /// <param name="endBytes">End bytes of the target content. Must match the length of <paramref name="content"/> plus <paramref name="startBytes"/></param>
        /// <returns>True if the file part was uploaded successfully. False otherwise</returns>
        public async Task<bool> UploadPartial(string remoteFilePath, Stream content, string name, long startBytes, long endBytes, CancellationToken cancellationToken = default)
        {
            if (startBytes + content.Length != endBytes)
            {
                throw new InvalidOperationException("The length of the given content plus the startBytes must match the endBytes.");
            }

            // Should not have a trailing slash.
            var uploadUri = await GetServerUrl(remoteFilePath.TrimEnd('/') + "/" + name.TrimStart('/'), false).ConfigureAwait(false);

            HttpResponseMessage response = null;

            try
            {
                response = await HttpUploadRequest(uploadUri.Uri, HttpMethod.Put, content, null, startBytes, endBytes, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    response.StatusCode != HttpStatusCode.NoContent &&
                    response.StatusCode != HttpStatusCode.Created)
                {
                    throw new WebDAVException((int)response.StatusCode, "Failed uploading file.");
                }

                return response.IsSuccessStatusCode;
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// Create a directory on the server
        /// </summary>
        /// <param name="remotePath">Destination path of the directory on the server</param>
        /// <param name="name">The name of the folder to create</param>
        /// <returns>True if the folder was created successfully. False otherwise</returns>
        public async Task<bool> CreateDir(string remotePath, string name, CancellationToken cancellationToken = default)
        {
            // Should not have a trailing slash.
            var dirUri = await GetServerUrl(remotePath.TrimEnd('/') + "/" + name.TrimStart('/'), false).ConfigureAwait(false);

            IDictionary<string, string> headers = new Dictionary<string, string>();
            if (CustomHeaders != null)
            {
                foreach (var keyValuePair in CustomHeaders)
                {
                    headers.Add(keyValuePair);
                }
            }

            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(dirUri.Uri, m_mkColMethod, headers, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Conflict)
                    throw new WebDAVConflictException((int) response.StatusCode, "Failed creating folder.");

                if (response.StatusCode != HttpStatusCode.OK &&
                    response.StatusCode != HttpStatusCode.NoContent &&
                    response.StatusCode != HttpStatusCode.Created)
                {
                    throw new WebDAVException((int)response.StatusCode, "Failed creating folder.");
                }

                return response.IsSuccessStatusCode;
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// Deletes a folder from the server.
        /// </summary>
        public async Task DeleteFolder(string href, CancellationToken cancellationToken = default)
        {
            var listUri = await GetServerUrl(href, true).ConfigureAwait(false);
            await Delete(listUri.Uri, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes a file from the server.
        /// </summary>
        public async Task DeleteFile(string href, CancellationToken cancellationToken = default)
        {
            var listUri = await GetServerUrl(href, false).ConfigureAwait(false);
            await Delete(listUri.Uri, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Move a folder on the server
        /// </summary>
        /// <param name="srcFolderPath">Source path of the folder on the server</param>
        /// <param name="dstFolderPath">Destination path of the folder on the server</param>
        /// <returns>True if the folder was moved successfully. False otherwise</returns>
        public async Task<bool> MoveFolder(string srcFolderPath, string dstFolderPath, CancellationToken cancellationToken = default)
        {
            // Should have a trailing slash.
            var srcUri = await GetServerUrl(srcFolderPath, true).ConfigureAwait(false);
            var dstUri = await GetServerUrl(dstFolderPath, true).ConfigureAwait(false);

            return await Move(srcUri.Uri, dstUri.Uri, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Move a file on the server
        /// </summary>
        /// <param name="srcFilePath">Source path and filename of the file on the server</param>
        /// <param name="dstFilePath">Destination path and filename of the file on the server</param>
        /// <returns>True if the file was moved successfully. False otherwise</returns>
        public async Task<bool> MoveFile(string srcFilePath, string dstFilePath, CancellationToken cancellationToken = default)
        {
            // Should not have a trailing slash.
            var srcUri = await GetServerUrl(srcFilePath, false).ConfigureAwait(false);
            var dstUri = await GetServerUrl(dstFilePath, false).ConfigureAwait(false);

            return await Move(srcUri.Uri, dstUri.Uri, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Copies a folder on the server
        /// </summary>
        /// <param name="srcFolderPath">Source path of the folder on the server</param>
        /// <param name="dstFolderPath">Destination path of the folder on the server</param>
        /// <returns>True if the folder was copied successfully. False otherwise</returns>
        public async Task<bool> CopyFolder(string srcFolderPath, string dstFolderPath, CancellationToken cancellationToken = default)
        {
            // Should have a trailing slash.
            var srcUri = await GetServerUrl(srcFolderPath, true).ConfigureAwait(false);
            var dstUri = await GetServerUrl(dstFolderPath, true).ConfigureAwait(false);

            return await Copy(srcUri.Uri, dstUri.Uri, cancellationToken).ConfigureAwait(false);
        }
        
        /// <summary>
        /// Copies a file on the server
        /// </summary>
        /// <param name="srcFilePath">Source path and filename of the file on the server</param>
        /// <param name="dstFilePath">Destination path and filename of the file on the server</param>
        /// <returns>True if the file was copied successfully. False otherwise</returns>
        public async Task<bool> CopyFile(string srcFilePath, string dstFilePath, CancellationToken cancellationToken = default)
        {
            // Should not have a trailing slash.
            var srcUri = await GetServerUrl(srcFilePath, false).ConfigureAwait(false);
            var dstUri = await GetServerUrl(dstFilePath, false).ConfigureAwait(false);

            return await Copy(srcUri.Uri, dstUri.Uri, cancellationToken).ConfigureAwait(false);
        }

        #endregion
        
        #region Private methods
        private async Task Delete(Uri listUri, CancellationToken cancellationToken = default)
        {
            IDictionary<string, string> headers = new Dictionary<string, string>();
            if (CustomHeaders != null)
            {
                foreach (var keyValuePair in CustomHeaders)
                {
                    headers.Add(keyValuePair);
                }
            }

            var response = await HttpRequest(listUri, HttpMethod.Delete, headers, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK &&
                response.StatusCode != HttpStatusCode.NoContent)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed deleting item.");
            }
        }

        private async Task<Item> Get(Uri listUri, CancellationToken cancellationToken = default)
        {
            // Depth header: http://webdav.org/specs/rfc4918.html#rfc.section.9.1.4
            IDictionary<string, string> headers = new Dictionary<string, string>
            {
                { "Depth", "0" }
            };

            if (CustomHeaders != null)
            {
                foreach (var keyValuePair in CustomHeaders)
                {
                    headers.Add(keyValuePair);
                }
            }

            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(listUri, m_propFindMethod, headers, s_propFindRequestContentBytes, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    (int) response.StatusCode != c_httpStatusCode_MultiStatus)
                {
                    throw new WebDAVException((int)response.StatusCode, string.Format("Failed retrieving item/folder (Status Code: {0})", response.StatusCode));
                }

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var result = ResponseParser.ParseItem(stream);

                    if (result == null)
                    {
                        throw new WebDAVException("Failed deserializing data returned from server.");
                    }

                    return result;
                }
            }
            finally
            {
                response?.Dispose();
            }
        }

        private async Task<bool> Move(Uri srcUri, Uri dstUri, CancellationToken cancellationToken = default)
        {
            const string requestContent = "MOVE";

            IDictionary<string, string> headers = new Dictionary<string, string>
            {
                { "Destination", dstUri.ToString() }
            };

            if (CustomHeaders != null)
            {
                foreach (var keyValuePair in CustomHeaders)
                {
                    headers.Add(keyValuePair);
                }
            }

            var response = await HttpRequest(srcUri, m_moveMethod, headers, Encoding.UTF8.GetBytes(requestContent), cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK &&
                response.StatusCode != HttpStatusCode.Created)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed moving file.");
            }

            return response.IsSuccessStatusCode;
        }

        private async Task<bool> Copy(Uri srcUri, Uri dstUri, CancellationToken cancellationToken = default)
        {
            const string requestContent = "COPY";

            IDictionary<string, string> headers = new Dictionary<string, string>
            {
                { "Destination", dstUri.ToString() }
            };

            if (CustomHeaders != null)
            {
                foreach (var keyValuePair in CustomHeaders)
                {
                    headers.Add(keyValuePair);
                }
            }

            var response = await HttpRequest(srcUri, m_copyMethod, headers, Encoding.UTF8.GetBytes(requestContent), cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK &&
                response.StatusCode != HttpStatusCode.Created)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed copying file.");
            }

            return response.IsSuccessStatusCode;
        }

        private async Task<Stream> DownloadFile(string remoteFilePath, Dictionary<string, string> header, CancellationToken cancellationToken = default)
        {
            // Should not have a trailing slash.
            var downloadUri = await GetServerUrl(remoteFilePath, false).ConfigureAwait(false);
            var response = await HttpRequest(downloadUri.Uri, HttpMethod.Get, header, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.PartialContent)
            {
                return await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            }
            throw new WebDAVException((int)response.StatusCode, "Failed retrieving file.");
        }
        
        #region Server communication

        /// <summary>
        /// Perform the WebDAV call and fire the callback when finished.
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="method"></param>
        /// <param name="headers"></param>
        /// <param name="content"></param>
        private async Task<HttpResponseMessage> HttpRequest(Uri uri, HttpMethod method, IDictionary<string, string> headers = null, byte[] content = null, CancellationToken cancellationToken = default)
        {
            using (var request = new HttpRequestMessage(method, uri))
            {
                request.Headers.Connection.Add("Keep-Alive");
                if (!string.IsNullOrWhiteSpace(UserAgent))
                    request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, UserAgentVersion));
                else
                    request.Headers.UserAgent.Add(s_defaultUserAgent);

                if (headers != null)
                {
                    foreach (string key in headers.Keys)
                    {
                        request.Headers.Add(key, headers[key]);
                    }
                }

                // Need to send along content?
                if (content != null)
                {
                    request.Content = new ByteArrayContent(content);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
                }

                return await m_httpClientWrapper.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Perform the WebDAV call and fire the callback when finished.
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="method"></param>
        /// <param name="content"></param>
        /// <param name="headers"></param>
        /// <param name="startBytes"></param>
        /// <param name="endBytes"></param>
        private async Task<HttpResponseMessage> HttpUploadRequest(Uri uri, HttpMethod method, Stream content, IDictionary<string, string> headers = null, long? startBytes = null, long? endBytes = null, CancellationToken cancellationToken = default)
        {
            using (var request = new HttpRequestMessage(method, uri))
            {
                request.Headers.Connection.Add("Keep-Alive");
                if (!string.IsNullOrWhiteSpace(UserAgent))
                    request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, UserAgentVersion));
                else
                    request.Headers.UserAgent.Add(s_defaultUserAgent);

                if (headers != null)
                {
                    foreach (string key in headers.Keys)
                    {
                        request.Headers.Add(key, headers[key]);
                    }
                }

                // Need to send along content?
                if (content != null)
                {
                    request.Content = new StreamContent(content);
                    if (startBytes.HasValue && endBytes.HasValue)
                    {
                        request.Content.Headers.ContentRange = ContentRangeHeaderValue.Parse($"bytes {startBytes}-{endBytes}/*");
                        request.Content.Headers.ContentLength = endBytes - startBytes;
                    }
                }

                return await m_httpClientWrapper.SendUploadAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Try to create an Uri with kind UriKind.Absolute
        /// This particular implementation also works on Mono/Linux
        /// It seems that on Mono it is expected behavior that URIs
        /// of kind /a/b are indeed absolute URIs since it refers to a file in /a/b.
        /// https://bugzilla.xamarin.com/show_bug.cgi?id=30854
        /// </summary>
        /// <param name="uriString"></param>
        /// <param name="uriResult"></param>
        private static bool TryCreateAbsolute(string uriString, out Uri uriResult)
        {
            return Uri.TryCreate(uriString, UriKind.Absolute, out uriResult) && uriResult.Scheme != Uri.UriSchemeFile;
        }

        private async Task<UriBuilder> GetServerUrl(string path, bool appendTrailingSlash)
        {
            // Resolve the base path on the server
            if (m_encodedBasePath == null)
            {
                var baseUri = new UriBuilder(m_server) 
                {
                    Path = m_basePath
                };
                if (Port != null)
                {
                    baseUri.Port = (int)Port;
                }
                var root = await Get(baseUri.Uri).ConfigureAwait(false);

                m_encodedBasePath = root.Href;
            }

            // If we've been asked for the "root" folder
            if (string.IsNullOrEmpty(path))
            {
                // If the resolved base path is an absolute URI, use it
                if (TryCreateAbsolute(m_encodedBasePath, out Uri absoluteBaseUri))
                {
                    return new UriBuilder(absoluteBaseUri);
                }

                // Otherwise, use the resolved base path relatively to the server
                var baseUri = new UriBuilder(m_server)
                {
                    Path = m_encodedBasePath
                };
                if (Port != null)
                {
                    baseUri.Port = (int)Port;
                }
                return baseUri;
            }

            // If the requested path is absolute, use it
            if (TryCreateAbsolute(path, out Uri absoluteUri))
            {
                return new UriBuilder(absoluteUri);
            }
            else
            {
                // Otherwise, create a URI relative to the server
                UriBuilder baseUri;
                if (TryCreateAbsolute(m_encodedBasePath, out absoluteUri))
                {
                    baseUri = new UriBuilder(absoluteUri);

                    baseUri.Path = baseUri.Path.TrimEnd('/') + "/" + path.TrimStart('/');

                    if (appendTrailingSlash && !baseUri.Path.EndsWith("/"))
                        baseUri.Path += "/";
                }
                else
                {
                    baseUri = new UriBuilder(m_server);
                    if (Port!= null)
                    {
                        baseUri.Port = (int)Port;
                    }

                    // Ensure we don't add the base path twice
                    var finalPath = path;
                    if (!finalPath.StartsWith(m_encodedBasePath, StringComparison.InvariantCultureIgnoreCase))
                    {
                        finalPath = m_encodedBasePath.TrimEnd('/') + "/" + path;
                    }
                    if (appendTrailingSlash)
                        finalPath = finalPath.TrimEnd('/') + "/";

                    baseUri.Path = finalPath;
                }

                return baseUri;
            }
        }

        #endregion

        #endregion

        #region WebDAV Connection Helpers

        public bool ServerCertificateValidation(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certification, System.Security.Cryptography.X509Certificates.X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (ServerCertificateValidationCallback != null)
            {
                return ServerCertificateValidationCallback(sender, certification, chain, sslPolicyErrors);
            }
            return false;
        }
        #endregion

        #region IDisposable methods
        protected virtual void Dispose(bool disposing)
        {
            if (!m_disposedValue)
            {
                if (disposing)
                {
                    if (m_shouldDispose)
                    {
                        if (m_httpClientWrapper is IDisposable httpClientWrapperDisposable)
                        {
                            httpClientWrapperDisposable.Dispose();
                            m_httpClientWrapper = null;
                        }
                    }
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
