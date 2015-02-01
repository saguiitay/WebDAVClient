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
using System.Xml.Serialization;
using WebDAVClient.Model;
using WebDAVClient.Model.Internal;

namespace WebDAVClient
{
    public class Client
    {
        private static readonly HttpMethod PropFind = new HttpMethod("PROPFIND");
        private static readonly HttpMethod MkCol = new HttpMethod(WebRequestMethods.Http.MkCol);

        private const int HttpStatusCode_MultiStatus = 207;

        private static readonly XmlSerializer PropFindResponseSerializer = new XmlSerializer(typeof(PROPFINDResponse));

        private readonly HttpClient _client;

        #region WebDAV connection parameters
        private String _server;
        /// <summary>
        /// Specify the WebDAV hostname (required).
        /// </summary>
        public String Server
        {
            get { return _server; }
            set
            {
                value = value.TrimEnd('/');
                _server = value;
            }
        }
        private String _basePath = "/";

        /// <summary>
        /// Specify the path of a WebDAV directory to use as 'root' (default: /)
        /// </summary>
        public String BasePath
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

        #endregion


        public Client(NetworkCredential credential)
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
            // http://webdav.org/specs/rfc4918.html#METHOD_PROPFIND
            const string requestContent = "<?xml version=\"1.0\" encoding=\"utf-8\" ?><propfind xmlns=\"DAV:\"><propname/></propfind>";

            Uri listUri = GetServerUrl(path, true);


            // Depth header: http://webdav.org/specs/rfc4918.html#rfc.section.9.1.4
            IDictionary<string, string> headers = new Dictionary<string, string>();
            if (depth != null)
            {
                headers.Add("Depth", depth.ToString());
            }


            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(listUri, PropFind, headers, Encoding.UTF8.GetBytes(requestContent)).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    (int) response.StatusCode != HttpStatusCode_MultiStatus)
                {
                    throw new WebDAVException((int) response.StatusCode, "Failed retrieving items in folder.");
                }

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var result = (PROPFINDResponse) PropFindResponseSerializer.Deserialize(stream);

                    if (result == null)
                    {
                        throw new WebDAVException("Failed deserializing data returned from server.");
                    }

                    var listUrl = listUri.ToString();
                    var listPath = listUri.PathAndQuery;

                    return result.Response
                        .Where(r => !string.Equals(r.href, listUrl, StringComparison.CurrentCultureIgnoreCase) &&
                                    !string.Equals(r.href, listPath, StringComparison.CurrentCultureIgnoreCase))
                        .Select(r => new Item
                            {
                                Href = HttpUtility.UrlDecode(r.href),
                                ContentType = r.propstat.Prop.ContentType,
                                CreationDate =
                                    r.propstat.Prop.CreationDate != null
                                        ? r.propstat.Prop.CreationDate.Value
                                        : (DateTime?) null,
                                Etag = r.propstat.Prop.Etag,
                                IsCollection =
                                    r.propstat.Prop.ResourceType.collection != null ||
                                    (r.propstat.Prop.IsCollection != null && r.propstat.Prop.IsCollection.Value == 1),
                                IsHidden = r.propstat.Prop.IsHidden != null && r.propstat.Prop.IsHidden.Value == 1
                            })
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
        public async Task<Item> Get(string path = "/")
        {
            // http://webdav.org/specs/rfc4918.html#METHOD_PROPFIND
            const string requestContent = "<?xml version=\"1.0\" encoding=\"utf-8\" ?><propfind xmlns=\"DAV:\"><propname/></propfind>";

            Uri listUri = GetServerUrl(path, true);


            // Depth header: http://webdav.org/specs/rfc4918.html#rfc.section.9.1.4
            IDictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add("Depth", "0");


            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(listUri, PropFind, headers, Encoding.UTF8.GetBytes(requestContent)) .ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    (int) response.StatusCode != HttpStatusCode_MultiStatus)
                {
                    throw new WebDAVException((int) response.StatusCode, "Failed retrieving item/folder.");
                }

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var result = (PROPFINDResponse) PropFindResponseSerializer.Deserialize(stream);

                    if (result == null)
                    {
                        throw new WebDAVException("Failed deserializing data returned from server.");
                    }

                    return result.Response
                        .Select(r => new Item
                            {
                                Href = HttpUtility.UrlDecode(r.href),
                                ContentType = r.propstat.Prop.ContentType,
                                CreationDate =
                                    r.propstat.Prop.CreationDate != null
                                        ? r.propstat.Prop.CreationDate.Value
                                        : (DateTime?) null,
                                Etag = r.propstat.Prop.Etag,
                                IsCollection =
                                    r.propstat.Prop.IsCollection != null && r.propstat.Prop.IsCollection.Value == 1,
                                IsHidden = r.propstat.Prop.IsHidden != null && r.propstat.Prop.IsHidden.Value == 1
                            }).FirstOrDefault();
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
            Uri downloadUri = GetServerUrl(remoteFilePath, false);

            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(downloadUri, HttpMethod.Get).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new WebDAVException((int)response.StatusCode, "Failed retrieving file.");
                }
                return await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
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
        /// <param name="content"></param>
        /// <param name="name"></param>
        public async Task<bool> Upload(string remoteFilePath, Stream content, string name)
        {
            // Should not have a trailing slash.
            Uri uploadUri = GetServerUrl(remoteFilePath + name, false);

            HttpResponseMessage response = null;

            try
            {
                response = await HttpUploadRequest(uploadUri, HttpMethod.Put, content).ConfigureAwait(false);

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
            Uri dirUri = GetServerUrl(remotePath + name, false);

            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(dirUri, MkCol).ConfigureAwait(false);

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

                return await _client.SendAsync(request).ConfigureAwait(false);
            }
        }

        private Uri GetServerUrl(String path, Boolean appendTrailingSlash)
        {
            string completePath = "";

            if (path != null)
            {
                if (!path.StartsWith(_basePath))
                    completePath += _basePath;
                if (!path.StartsWith(_server, StringComparison.InvariantCultureIgnoreCase))
                {
                    completePath += path.Trim('/');
                }
                else
                {
                    completePath += path.Substring(_server.Length + 1).Trim('/');
                }
            }
            else
            {
                completePath += _basePath;
            }

            if (completePath.StartsWith("/") == false) { completePath = '/' + completePath; }
            if (appendTrailingSlash && completePath.EndsWith("/") == false) { completePath += '/'; }

            if (Port.HasValue)
                return new Uri(_server + ":" + Port + completePath);

            return new Uri(_server + completePath);
        }


        #endregion
    }
}
