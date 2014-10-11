/*
 * (C) 2010 Kees van den Broek: kvdb@kvdb.net
 *          D-centralize: d-centralize.nl
 *          
 * Latest version and examples on: http://kvdb.net/projects/webdav
 * 
 * Feel free to use this code however you like.
 * http://creativecommons.org/license/zero/
 * 
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
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
        /// List files in the root directory
        /// </summary>
        public async Task<IEnumerable<Item>> List()
        {
            // Set default depth to 1. This would prevent recursion (default is infinity).
            return await List("/", 1);
        }

        /// <summary>
        /// List files in the given directory
        /// </summary>
        /// <param name="path"></param>
        public async Task<IEnumerable<Item>> List(string path)
        {
            // Set default depth to 1. This would prevent recursion.
            return await List(path, 1);
        }

        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <param name="remoteFilePath">List only files in this path</param>
        /// <param name="depth">Recursion depth</param>
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
        public async Task<IEnumerable<Item>> List(string remoteFilePath, int? depth)
        {
            // http://webdav.org/specs/rfc4918.html#METHOD_PROPFIND
            const string requestContent = "<?xml version=\"1.0\" encoding=\"utf-8\" ?><propfind xmlns=\"DAV:\"><propname/></propfind>";
            
            Uri listUri = GetServerUrl(remoteFilePath, true);


            // Depth header: http://webdav.org/specs/rfc4918.html#rfc.section.9.1.4
            IDictionary<string, string> headers = new Dictionary<string, string>();
            if (depth != null)
            {
                headers.Add("Depth", depth.ToString());
            }


            var response = await HttpRequest(listUri, PropFind, headers, Encoding.UTF8.GetBytes(requestContent));

            if (response.StatusCode != HttpStatusCode.OK && 
                (int)response.StatusCode != HttpStatusCode_MultiStatus)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed retrieving items in folder.");
            }

            var stream = await response.Content.ReadAsStreamAsync();
            var result = (PROPFINDResponse)PropFindResponseSerializer.Deserialize(stream);

            if (result == null)
            {
                throw new WebDAVException("Failed deserializing data returned from server.");
            }

            return result.Response
                    .Where(r => !string.Equals(r.href, listUri.ToString(), StringComparison.CurrentCultureIgnoreCase) &&
                                !string.Equals(r.href, remoteFilePath, StringComparison.CurrentCultureIgnoreCase))
                    .Select(r => new Item
                        {
                            Href = r.href,
                            ContentType = r.propstat.Prop.ContentType,
                            CreationDate = r.propstat.Prop.CreationDate.Value,
                            Etag = r.propstat.Prop.Etag,
                            IsCollection = r.propstat.Prop.ResourceType.collection != null || (r.propstat.Prop.IsCollection != null && r.propstat.Prop.IsCollection.Value == 1),
                            IsHidden = r.propstat.Prop.IsHidden != null && r.propstat.Prop.IsHidden.Value == 1
                        })
                        .ToList();
        }

        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
        public async Task<Item> Get()
        {
            return await Get("/");
        }

        /// 
        public async Task<Item> Get(string remoteFilePath)
        {
            // http://webdav.org/specs/rfc4918.html#METHOD_PROPFIND
            const string requestContent = "<?xml version=\"1.0\" encoding=\"utf-8\" ?><propfind xmlns=\"DAV:\"><propname/></propfind>";

            Uri listUri = GetServerUrl(remoteFilePath, true);


            // Depth header: http://webdav.org/specs/rfc4918.html#rfc.section.9.1.4
            IDictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add("Depth", "0");


            var response = await HttpRequest(listUri, PropFind, headers, Encoding.UTF8.GetBytes(requestContent));

            if (response.StatusCode != HttpStatusCode.OK &&
                (int)response.StatusCode != HttpStatusCode_MultiStatus)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed retrieving item/folder.");
            }

            var stream = await response.Content.ReadAsStreamAsync();
            var result = (PROPFINDResponse)PropFindResponseSerializer.Deserialize(stream);

            if (result == null)
            {
                throw new WebDAVException("Failed deserializing data returned from server.");
            }

            return result.Response
                    .Select(r => new Item
                    {
                        Href = r.href,
                        ContentType = r.propstat.Prop.ContentType,
                        CreationDate = r.propstat.Prop.CreationDate.Value,
                        Etag = r.propstat.Prop.Etag,
                        IsCollection = r.propstat.Prop.IsCollection != null && r.propstat.Prop.IsCollection.Value == 1,
                        IsHidden = r.propstat.Prop.IsHidden != null && r.propstat.Prop.IsHidden.Value == 1
                    }).FirstOrDefault();
        }

        /// <summary>
        /// Download a file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        public async Task<Stream> Download(string remoteFilePath)
        {
            // Should not have a trailing slash.
            Uri downloadUri = GetServerUrl(remoteFilePath, false);

            var response = await HttpRequest(downloadUri, HttpMethod.Get);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed retrieving file.");
            }
            return await response.Content.ReadAsStreamAsync();
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

            var response = await HttpUploadRequest(uploadUri, HttpMethod.Put, content);

            if (response.StatusCode != HttpStatusCode.OK &&
                response.StatusCode != HttpStatusCode.Created)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed uploading file.");
            }

            return response.IsSuccessStatusCode;
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

            var response = await HttpRequest(dirUri, MkCol);

            if (response.StatusCode != HttpStatusCode.OK &&
                response.StatusCode != HttpStatusCode.Created)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed creating folder.");
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
            var request = new HttpRequestMessage(method, uri);

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

            return await _client.SendAsync(request);
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
            var request = new HttpRequestMessage(method, uri);

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

            return await _client.SendAsync(request);
        }

        private Uri GetServerUrl(String path, Boolean appendTrailingSlash)
        {
            string completePath = _basePath;
            if (path != null)
            {
                if (!path.StartsWith(_server, StringComparison.InvariantCultureIgnoreCase))
                {
                    completePath += path.Trim('/');
                }
                else
                {
                    completePath += path.Substring(_server.Length + 1).Trim('/');
                }
            }

            if (appendTrailingSlash && completePath.EndsWith("/") == false) { completePath += '/'; }

            if (Port.HasValue)
                return new Uri(_server + ":" + Port + completePath);

            return new Uri(_server + completePath);
        }


        #endregion
    }
}
