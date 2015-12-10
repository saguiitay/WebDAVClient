using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using WebDAVClient.Helpers;
using WebDAVClient.Model;

namespace WebDAVClient
{
    public class Client : IClient
    {
        private static readonly HttpMethod PropFind = new HttpMethod("PROPFIND");
        private static readonly HttpMethod MoveMethod = new HttpMethod("MOVE");

        private static readonly HttpMethod MkCol = new HttpMethod(WebRequestMethods.Http.MkCol);

        private const int HttpStatusCode_MultiStatus = 207;

        // http://webdav.org/specs/rfc4918.html#METHOD_PROPFIND
        private const string PropFindRequestContent =
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

        private static readonly string AssemblyVersion = typeof (IClient).Assembly.GetName().Version.ToString();

        private readonly HttpClient _client;
        private readonly HttpClient _uploadClient;
        private string _server;
        private string _basePath = "/";


        #region WebDAV connection parameters

        /// <summary>
        /// Specify the WebDAV hostname (required).
        /// </summary>
        public string Server
        {
            get { return _server; }
            set
            {
                value = value.TrimEnd('/');
                _server = value;
            }
        }

        /// <summary>
        /// Specify the path of a WebDAV directory to use as 'root' (default: /)
        /// </summary>
        public string BasePath
        {
            get { return _basePath; }
            set
            {
                value = value.Trim('/');
                if (string.IsNullOrEmpty(value))
                    _basePath = "/";
                else
                    _basePath = "/" + value + "/";
            }
        }

        /// <summary>
        /// Specify an port (default: null = auto-detect)
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

        #endregion


        public Client(NetworkCredential credential = null, TimeSpan? uploadTimeout = null)
        {
            var handler = new HttpClientHandler();
            if (handler.SupportsAutomaticDecompression)
                handler.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;
            if (credential != null)
            {
                handler.Credentials = credential;
                handler.PreAuthenticate = true;
            }

            _client = new HttpClient(handler);
            _client.DefaultRequestHeaders.ExpectContinue = false;

            if (uploadTimeout != null)
            {
                _uploadClient = new HttpClient(handler);
                _uploadClient.DefaultRequestHeaders.ExpectContinue = false;
                _uploadClient.Timeout = uploadTimeout.Value;
            }

        }

        #region WebDAV operations

        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <param name="path">List only files in this path</param>
        /// <param name="depth">Recursion depth</param>
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
        public async Task<IEnumerable<Item>> List(string path = "/", int? depth = 1)
        {
            var listUri = GetServerUrl(path, true);

            // Depth header: http://webdav.org/specs/rfc4918.html#rfc.section.9.1.4
            IDictionary<string, string> headers = new Dictionary<string, string>();
            if (depth != null)
            {
                headers.Add("Depth", depth.ToString());
            }


            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(listUri.Uri, PropFind, headers, Encoding.UTF8.GetBytes(PropFindRequestContent)).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    (int) response.StatusCode != HttpStatusCode_MultiStatus)
                {
                    throw new WebDAVException((int) response.StatusCode, "Failed retrieving items in folder.");
                }

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var result = ResponseParser.ParseItems(path, stream);

                    if (result == null)
                    {
                        throw new WebDAVException("Failed deserializing data returned from server.");
                    }

                    var listUrl = listUri.ToString();
                    var listUriStr = listUri.Uri.ToString();
                    var listUrlDecoded = listUri.ToString().Replace(listUri.Uri.PathAndQuery, HttpUtility.UrlDecode(listUri.Uri.PathAndQuery));
                    var listUriDecoded = listUri.Uri.ToString().Replace(listUri.Uri.PathAndQuery, HttpUtility.UrlDecode(listUri.Uri.PathAndQuery));
                    var listPath = listUri.Uri.PathAndQuery;
                    var listPathTrimmed = listPath.TrimEnd('/');
                    var listPathDecoded = HttpUtility.UrlDecode(listUri.Uri.PathAndQuery);
                    var listPathDecodedTrimmed = listPathDecoded.TrimEnd('/');

                    return result
                        .Where(r => !string.Equals(r.Href, listUrl, StringComparison.CurrentCultureIgnoreCase) &&
                                    !string.Equals(r.Href, listUriStr, StringComparison.CurrentCultureIgnoreCase) &&
                                    !string.Equals(r.Href, listUrlDecoded, StringComparison.CurrentCultureIgnoreCase) &&
                                    !string.Equals(r.Href, listUriDecoded, StringComparison.CurrentCultureIgnoreCase) &&
                                    !string.Equals(r.Href, listPath, StringComparison.CurrentCultureIgnoreCase) &&
                                    !string.Equals(r.Href, listPathTrimmed, StringComparison.CurrentCultureIgnoreCase) &&
                                    !string.Equals(r.Href, listPathDecoded, StringComparison.CurrentCultureIgnoreCase) &&
                                    !string.Equals(r.Href, listPathDecodedTrimmed, StringComparison.CurrentCultureIgnoreCase))
                        .ToList();
                }

            }
            finally
            {
                if (response != null)
                    response.Dispose();
            }
        }

        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
        public async Task<Item> GetFolder(string path = "/")
        {
            var listUri = GetServerUrl(path, true);
            return await Get(listUri.Uri, path).ConfigureAwait(false);
        }

        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
        public async Task<Item> GetFile(string path = "/")
        {
            var listUri = GetServerUrl(path, false);
            return await Get(listUri.Uri, path).ConfigureAwait(false);
        }


        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
        private async Task<Item> Get(Uri listUri, string path)
        {

            // Depth header: http://webdav.org/specs/rfc4918.html#rfc.section.9.1.4
            IDictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add("Depth", "0");


            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(listUri, PropFind, headers, Encoding.UTF8.GetBytes(PropFindRequestContent)).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    (int) response.StatusCode != HttpStatusCode_MultiStatus)
                {
                    throw new WebDAVException((int)response.StatusCode, string.Format("Failed retrieving item/folder (Status Code: {0})", response.StatusCode));
                }

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var result = ResponseParser.ParseItem(path, stream);

                    if (result == null)
                    {
                        throw new WebDAVException("Failed deserializing data returned from server.");
                    }

                    return result;
                }
            }
            finally
            {
                if (response != null)
                    response.Dispose();
            }
        }

        /// <summary>
        /// Download a file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        public async Task<Stream> Download(string remoteFilePath)
        {
            // Should not have a trailing slash.
            var downloadUri = GetServerUrl(remoteFilePath, false);

            var dictionary = new Dictionary<string, string> { { "translate", "f" } };
            var response = await HttpRequest(downloadUri.Uri, HttpMethod.Get, dictionary).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed retrieving file.");
            }
            return await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Download a file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="content"></param>
        /// <param name="name"></param>
        public async Task<bool> Upload(string remoteFilePath, Stream content, string name)
        {
            // Should not have a trailing slash.
            var uploadUri = GetServerUrl(remoteFilePath.TrimEnd('/') + "/" + name.TrimStart('/'), false);

            HttpResponseMessage response = null;

            try
            {
                response = await HttpUploadRequest(uploadUri.Uri, HttpMethod.Put, content).ConfigureAwait(false);

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
                if (response != null)
                    response.Dispose();
            }

        }


        /// <summary>
        /// Create a directory on the server
        /// </summary>
        /// <param name="remotePath">Destination path of the directory on the server</param>
        /// <param name="name"></param>
        public async Task<bool> CreateDir(string remotePath, string name)
        {
            // Should not have a trailing slash.
            var dirUri = GetServerUrl(remotePath.TrimEnd('/') + "/" + name.TrimStart('/'), false);

            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(dirUri.Uri, MkCol).ConfigureAwait(false);

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
                if (response!= null)
                    response.Dispose();
            }
        }

        public async Task DeleteFolder(string href)
        {
            var listUri = GetServerUrl(href, true);
            await Delete(listUri.Uri).ConfigureAwait(false);
        }

        public async Task DeleteFile(string href)
        {
            var listUri = GetServerUrl(href, false);
            await Delete(listUri.Uri).ConfigureAwait(false);
        }


        private async Task Delete(Uri listUri)
        {
            var response = await HttpRequest(listUri, HttpMethod.Delete).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK &&
                response.StatusCode != HttpStatusCode.NoContent)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed deleting item.");
            }
        }

        public async Task<bool> MoveFolder(string srcFolderPath, string dstFolderPath)
        {
            // Should have a trailing slash.
            var srcUri = GetServerUrl(srcFolderPath, true);
            var dstUri = GetServerUrl(dstFolderPath, true);

            return await Move(srcUri.Uri, dstUri.Uri).ConfigureAwait(false);

        }

        public async Task<bool> MoveFile(string srcFilePath, string dstFilePath)
        {
            // Should not have a trailing slash.
            var srcUri = GetServerUrl(srcFilePath, false);
            var dstUri = GetServerUrl(dstFilePath, false);

            return await Move(srcUri.Uri, dstUri.Uri).ConfigureAwait(false);
        }


        private async Task<bool> Move(Uri srcUri, Uri dstUri)
        {
            const string requestContent = "MOVE";

            IDictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add("Destination", dstUri.ToString());

            var response = await HttpRequest(srcUri, MoveMethod, headers, Encoding.UTF8.GetBytes(requestContent)).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK &&
                response.StatusCode != HttpStatusCode.Created)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed moving file.");
            }

            return response.IsSuccessStatusCode;
        }

        #endregion

        #region Server communication

        /// <summary>
        /// Perform the WebDAV call and fire the callback when finished.
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="method"></param>
        /// <param name="headers"></param>
        /// <param name="content"></param>
        private async Task<HttpResponseMessage> HttpRequest(Uri uri, HttpMethod method, IDictionary<string, string> headers = null, byte[] content = null)
        {
            using (var request = new HttpRequestMessage(method, uri))
            {
                request.Headers.Connection.Add("Keep-Alive");
                if (!string.IsNullOrWhiteSpace(UserAgent))
                    request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, UserAgentVersion));
                else
                    request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WebDAVClient", AssemblyVersion));

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

                return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Perform the WebDAV call and fire the callback when finished.
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="headers"></param>
        /// <param name="method"></param>
        /// <param name="content"></param>
        private async Task<HttpResponseMessage> HttpUploadRequest(Uri uri, HttpMethod method, Stream content, IDictionary<string, string> headers = null)
        {
            using (var request = new HttpRequestMessage(method, uri))
            {
                request.Headers.Connection.Add("Keep-Alive");
                if (!string.IsNullOrWhiteSpace(UserAgent))
                    request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, UserAgentVersion));
                else
                    request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WebDAVClient", AssemblyVersion));

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
                }

                var client = _uploadClient ?? _client;
                return await client.SendAsync(request).ConfigureAwait(false);
            }
        }

        private UriBuilder GetServerUrl(string path, bool appendTrailingSlash)
        {
            string completePath = "";

            if (path != null)
            {
                path = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));

                var uri = new Uri(path, UriKind.RelativeOrAbsolute);
                if (uri.IsAbsoluteUri)
                {
                    completePath = uri.PathAndQuery;
                }
                else
                {
                    var relativePath = path;
                    if (!relativePath.StartsWith(_basePath))
                        completePath += _basePath;
                    completePath = completePath.TrimEnd('/') + '/' + relativePath.TrimStart('/');
                }
            }
            else
            {
                completePath += _basePath;
            }

            if (completePath.StartsWith("/") == false)
            {
                completePath = '/' + completePath;
            }
            if (appendTrailingSlash && completePath.EndsWith("/") == false)
            {
                completePath += '/';
            }

            if (Port.HasValue)
                return new UriBuilder(_server + ":" + Port + completePath);

            return new UriBuilder(_server + completePath);
        }


        #endregion

      
    }
}
