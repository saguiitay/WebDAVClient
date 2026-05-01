using System;
using System.Collections.Generic;
using System.IO;
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
        private static readonly HttpMethod s_lockMethod = new HttpMethod("LOCK");
        private static readonly HttpMethod s_unlockMethod = new HttpMethod("UNLOCK");
        private static readonly HttpMethod s_propPatchMethod = new HttpMethod("PROPPATCH");
        private static readonly HttpMethod s_optionsMethod = HttpMethod.Options;

        private static readonly HttpMethod m_mkColMethod = new HttpMethod(WebRequestMethods.Http.MkCol);

        private static readonly string m_assemblyVersion = typeof(IClient).Assembly.GetName().Version.ToString();
        private static readonly ProductInfoHeaderValue s_defaultUserAgent = new ProductInfoHeaderValue("WebDAVClient", m_assemblyVersion);

        private const int c_httpStatusCode_MultiStatus = 207;

        // http://webdav.org/specs/rfc4918.html#METHOD_PROPFIND
        // The PROPFIND request bodies (allprop / propname / prop) are produced by
        // PropFindRequestBuilder. Static body bytes are cached inside the helper.
        private static readonly byte[] s_moveRequestContentBytes = Encoding.UTF8.GetBytes("MOVE");
        private static readonly byte[] s_copyRequestContentBytes = Encoding.UTF8.GetBytes("COPY");

        // RFC 4918 §9.10 LOCK request body. The owner is interpolated as raw text content
        // of <D:owner> — callers' owner strings are XML-escaped before substitution.
        private const string c_lockRequestContentFormat =
            "<?xml version=\"1.0\" encoding=\"utf-8\" ?>" +
            "<D:lockinfo xmlns:D=\"DAV:\">" +
            "<D:lockscope><D:exclusive/></D:lockscope>" +
            "<D:locktype><D:write/></D:locktype>" +
            "<D:owner>{0}</D:owner>" +
            "</D:lockinfo>";

        private const string c_defaultLockOwner = "WebDAVClient";

        private IHttpClientWrapper m_httpClientWrapper;
        private readonly bool m_shouldDispose;
        private readonly HttpClientHandler m_handler;
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
        /// Specify the certificates validation logic. Wired into the underlying
        /// HttpClientHandler when this Client owns its handler (i.e. when constructed via
        /// the <see cref="Client(ICredentials, TimeSpan?, IWebProxy)"/> constructor). The
        /// callback is invoked lazily on every TLS handshake, so it may be assigned or
        /// reassigned after construction. When constructed with a caller-supplied
        /// HttpClient/IHttpClientWrapper, this property has no effect — configure the
        /// callback on your own handler instead.
        /// </summary>
        public RemoteCertificateValidationCallback ServerCertificateValidationCallback { get; set; }
        #endregion

        /// <summary>
        /// Test-only access to the owned HttpClientHandler so unit tests can verify that
        /// <see cref="ServerCertificateValidationCallback"/> is correctly wired. Null when
        /// the Client was constructed with a caller-supplied HttpClient/IHttpClientWrapper.
        /// </summary>
        internal HttpClientHandler OwnedHandler => m_handler;

        public Client(ICredentials credential = null, TimeSpan? uploadTimeout = null, IWebProxy proxy = null)
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

            // Wire the certificate-validation callback lazily so callers can assign or
            // reassign ServerCertificateValidationCallback after construction. When no
            // user callback is set, fall back to the platform's default trust decision
            // (errors == None) — never deny by default, which would break all HTTPS.
            handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
            {
                var callback = ServerCertificateValidationCallback;
                if (callback != null)
                {
                    return callback(request, cert, chain, errors);
                }
                return errors == SslPolicyErrors.None;
            };

            m_handler = handler;

            var client = new System.Net.Http.HttpClient(handler, disposeHandler: true);
            client.DefaultRequestHeaders.ExpectContinue = false;

            System.Net.Http.HttpClient uploadClient = null;
            if (uploadTimeout != null)
            {
                uploadClient = new System.Net.Http.HttpClient(handler, disposeHandler: false);
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
        /// HTTP <c>OPTIONS</c> against the resource at <paramref name="path"/>
        /// (RFC 4918 §9.1, RFC 9110 §9.3.7). Returns the WebDAV compliance
        /// classes and the methods the server reports it supports.
        /// </summary>
        /// <param name="path">Resource to issue OPTIONS against. Defaults to the configured base path.</param>
        /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
        public async Task<ServerOptions> GetServerOptions(string path = "/", CancellationToken cancellationToken = default)
        {
            var uri = await GetServerUrl(path, true).ConfigureAwait(false);

            HttpResponseMessage response = null;
            try
            {
                response = await HttpRequest(uri.Uri, s_optionsMethod, cancellationToken: cancellationToken).ConfigureAwait(false);

                // OPTIONS is expected to succeed with 200 OK or 204 No Content.
                // Some servers return 200 even when the resource is missing; the
                // DAV header tells us whether it's a WebDAV resource at all.
                if (response.StatusCode != HttpStatusCode.OK
                    && response.StatusCode != HttpStatusCode.NoContent)
                {
                    throw new WebDAVException((int)response.StatusCode, "Failed retrieving server OPTIONS.");
                }

                string rawDav = null;
                if (response.Headers.TryGetValues("DAV", out var davValues))
                {
                    rawDav = OptionsHeaderParser.Join(davValues);
                }

                string rawAllow = null;
                // Allow lives on Content.Headers per RFC, but some servers send it
                // on the response headers directly when the body is empty. Probe both.
                if (response.Content?.Headers?.Allow != null && response.Content.Headers.Allow.Count > 0)
                {
                    rawAllow = OptionsHeaderParser.Join(response.Content.Headers.Allow);
                }
                else if (response.Headers.TryGetValues("Allow", out var allowValues))
                {
                    rawAllow = OptionsHeaderParser.Join(allowValues);
                }

                var compliance = OptionsHeaderParser.Split(rawDav);
                var allow = OptionsHeaderParser.Split(rawAllow);

                return new ServerOptions(compliance, allow, rawDav, rawAllow);
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <param name="path">List only files in this path</param>
        /// <param name="depth">Recursion depth</param>
        /// <returns>A list of files (entries without a trailing slash) and directories (entries with a trailing slash)</returns>
        public async Task<IEnumerable<Item>> List(string path = "/", int? depth = 1, CancellationToken cancellationToken = default)
        {
            return await ListCore(path, depth, PropFindRequestBuilder.BuildAllPropBody(), PropFindMode.AllProp, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// PROPFIND with a caller-supplied list of properties (RFC 4918 §9.1
        /// <c>&lt;prop&gt;</c>). Each returned <see cref="Item"/> has its
        /// <see cref="Item.FoundProperties"/> and <see cref="Item.NotFoundProperties"/>
        /// populated from the per-property propstat in the response. Standard
        /// DAV: live properties (<c>getetag</c>, <c>displayname</c>, etc.) also
        /// continue to populate the corresponding typed fields on
        /// <see cref="Item"/>.
        /// </summary>
        public async Task<IEnumerable<Item>> List(string path, int? depth, IEnumerable<PropertyName> properties, CancellationToken cancellationToken = default)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            var body = PropFindRequestBuilder.BuildPropBody(properties);
            return await ListCore(path, depth, body, PropFindMode.NamedProperties, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// PROPFIND <c>&lt;propname/&gt;</c> — discover which properties the
        /// server reports for each resource (RFC 4918 §9.1). Returned
        /// <see cref="Item"/>s have <see cref="Item.AvailablePropertyNames"/>
        /// populated; values are not requested in this mode.
        /// </summary>
        public async Task<IEnumerable<Item>> ListPropertyNames(string path = "/", int? depth = 1, CancellationToken cancellationToken = default)
        {
            return await ListCore(path, depth, PropFindRequestBuilder.BuildPropNameBody(), PropFindMode.PropName, cancellationToken).ConfigureAwait(false);
        }

        private async Task<IEnumerable<Item>> ListCore(string path, int? depth, byte[] body, PropFindMode mode, CancellationToken cancellationToken)
        {
            var listUri = await GetServerUrl(path, true).ConfigureAwait(false);

            // Depth header: http://webdav.org/specs/rfc4918.html#rfc.section.9.1.4
            IDictionary<string, string> headers = null;
            if (depth != null)
            {
                headers = new Dictionary<string, string>(1)
                {
                    { "Depth", depth.ToString() }
                };
            }

            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(listUri.Uri, m_propFindMethod, headers, body, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    (int) response.StatusCode != c_httpStatusCode_MultiStatus)
                {
                    throw new WebDAVException((int) response.StatusCode, "Failed retrieving items in folder.");
                }

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var items = ResponseParser.ParseItems(stream, mode);

                    if (items == null)
                    {
                        throw new WebDAVException("Failed deserializing data returned from server.");
                    }

                    var listUrl = listUri.ToString();

                    var result = new List<Item>(items.Count);
                    foreach (var item in items)
                    {
                        // If it's not a collection, add it to the result
                        if (!item.IsCollection)
                        {
                            result.Add(item);
                        }
                        else
                        {
                            // If it's not the requested parent folder, add it to the result.
                            // m_encodedBasePath was initialized by the GetServerUrl call at the
                            // top of this method, so the synchronous helper is safe to use here
                            // and avoids the per-item async state machine allocation.
                            var fullHref = BuildServerUrl(item.Href, true);
                            if (!string.Equals(fullHref.ToString(), listUrl, StringComparison.OrdinalIgnoreCase))
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
            return await Get(listUri.Uri, PropFindRequestBuilder.BuildAllPropBody(), PropFindMode.AllProp, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// PROPFIND a folder for a caller-supplied list of properties (RFC 4918
        /// §9.1 <c>&lt;prop&gt;</c>). The returned <see cref="Item"/>'s
        /// <see cref="Item.FoundProperties"/> / <see cref="Item.NotFoundProperties"/>
        /// reflect the per-property propstat from the server.
        /// </summary>
        public async Task<Item> GetFolder(string path, IEnumerable<PropertyName> properties, CancellationToken cancellationToken = default)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            var listUri = await GetServerUrl(path, true).ConfigureAwait(false);
            var body = PropFindRequestBuilder.BuildPropBody(properties);
            return await Get(listUri.Uri, body, PropFindMode.NamedProperties, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// PROPFIND <c>&lt;propname/&gt;</c> on a folder — discover the property
        /// names the server reports for it. The returned <see cref="Item"/>'s
        /// <see cref="Item.AvailablePropertyNames"/> is populated.
        /// </summary>
        public async Task<Item> GetFolderPropertyNames(string path = "/", CancellationToken cancellationToken = default)
        {
            var listUri = await GetServerUrl(path, true).ConfigureAwait(false);
            return await Get(listUri.Uri, PropFindRequestBuilder.BuildPropNameBody(), PropFindMode.PropName, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <returns>An item representing the retrieved file</returns>
        public async Task<Item> GetFile(string path = "/", CancellationToken cancellationToken = default)
        {
            var listUri = await GetServerUrl(path, false).ConfigureAwait(false);
            return await Get(listUri.Uri, PropFindRequestBuilder.BuildAllPropBody(), PropFindMode.AllProp, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// PROPFIND a file for a caller-supplied list of properties (RFC 4918
        /// §9.1 <c>&lt;prop&gt;</c>). See <see cref="GetFolder(string, IEnumerable{PropertyName}, CancellationToken)"/>.
        /// </summary>
        public async Task<Item> GetFile(string path, IEnumerable<PropertyName> properties, CancellationToken cancellationToken = default)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            var listUri = await GetServerUrl(path, false).ConfigureAwait(false);
            var body = PropFindRequestBuilder.BuildPropBody(properties);
            return await Get(listUri.Uri, body, PropFindMode.NamedProperties, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// PROPFIND <c>&lt;propname/&gt;</c> on a file — discover the property
        /// names the server reports for it.
        /// </summary>
        public async Task<Item> GetFilePropertyNames(string path = "/", CancellationToken cancellationToken = default)
        {
            var listUri = await GetServerUrl(path, false).ConfigureAwait(false);
            return await Get(listUri.Uri, PropFindRequestBuilder.BuildPropNameBody(), PropFindMode.PropName, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Download a file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <returns>A stream with the content of the downloaded file</returns>
        public Task<Stream> Download(string remoteFilePath, CancellationToken cancellationToken = default)
        {
            var headers = new Dictionary<string, string>(1)
            {
                { "translate", "f" }
            };
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
            var headers = new Dictionary<string, string>(2)
            { 
                { "translate", "f" }, 
                { "Range", "bytes=" + startBytes + "-" + endBytes } 
            };
            return DownloadFile(remoteFilePath, headers, cancellationToken);
        }

        /// <summary>
        /// Upload a file to the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="content"></param>
        /// <param name="name"></param>
        /// <returns>True if the file was uploaded successfully. False otherwise</returns>
        /// <param name="lockToken">Optional WebDAV lock token (RFC 4918 §10.4) for the resource being written. Pass the token returned by <see cref="LockFile"/> / <see cref="LockFolder"/> when the destination resource (or a parent collection) is locked, otherwise the server will reject the request with <c>423 Locked</c>. Bare (<c>opaquelocktoken:...</c>) and angle-bracket-wrapped forms are both accepted.</param>
        public async Task<bool> Upload(string remoteFilePath, Stream content, string name, string lockToken = null, CancellationToken cancellationToken = default)
        {
            // Should not have a trailing slash.
            var uploadUri = await GetServerUrl(remoteFilePath.TrimEnd('/') + "/" + name.TrimStart('/'), false).ConfigureAwait(false);

            var headers = LockTokenHeaderHelper.BuildLockTokenHeaders(uploadUri.Uri, lockToken);

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
        /// <param name="lockToken">Optional WebDAV lock token (RFC 4918 §10.4) for the resource being written. See <see cref="Upload"/> for details.</param>
        public async Task<bool> UploadPartial(string remoteFilePath, Stream content, string name, long startBytes, long endBytes, string lockToken = null, CancellationToken cancellationToken = default)
        {
            if (startBytes + content.Length != endBytes)
            {
                throw new InvalidOperationException("The length of the given content plus the startBytes must match the endBytes.");
            }

            // Should not have a trailing slash.
            var uploadUri = await GetServerUrl(remoteFilePath.TrimEnd('/') + "/" + name.TrimStart('/'), false).ConfigureAwait(false);

            var headers = LockTokenHeaderHelper.BuildLockTokenHeaders(uploadUri.Uri, lockToken);

            HttpResponseMessage response = null;

            try
            {
                response = await HttpUploadRequest(uploadUri.Uri, HttpMethod.Put, content, headers, startBytes, endBytes, cancellationToken: cancellationToken).ConfigureAwait(false);

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

            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(dirUri.Uri, m_mkColMethod, cancellationToken: cancellationToken).ConfigureAwait(false);

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
        /// <param name="lockToken">Optional WebDAV lock token (RFC 4918 §10.4) for the folder being deleted. Required when the folder (or a locked descendant) is locked; otherwise the server returns <c>423 Locked</c>.</param>
        public async Task DeleteFolder(string href, string lockToken = null, CancellationToken cancellationToken = default)
        {
            var listUri = await GetServerUrl(href, true).ConfigureAwait(false);
            await Delete(listUri.Uri, lockToken, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes a file from the server.
        /// </summary>
        /// <param name="lockToken">Optional WebDAV lock token (RFC 4918 §10.4) for the file being deleted. Required when the file (or its parent collection) is locked; otherwise the server returns <c>423 Locked</c>.</param>
        public async Task DeleteFile(string href, string lockToken = null, CancellationToken cancellationToken = default)
        {
            var listUri = await GetServerUrl(href, false).ConfigureAwait(false);
            await Delete(listUri.Uri, lockToken, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Move a folder on the server
        /// </summary>
        /// <param name="srcFolderPath">Source path of the folder on the server</param>
        /// <param name="dstFolderPath">Destination path of the folder on the server</param>
        /// <param name="overwrite">If <c>true</c> (the default), the server is instructed to overwrite an existing destination resource (<c>Overwrite: T</c>, RFC 4918 §10.6). If <c>false</c>, the request fails with <c>412 Precondition Failed</c> when the destination already exists (<c>Overwrite: F</c>).</param>
        /// <param name="sourceLockToken">Optional WebDAV lock token (RFC 4918 §10.4) for the source folder. Required when the source is locked, since MOVE removes the source.</param>
        /// <param name="destinationLockToken">Optional WebDAV lock token (RFC 4918 §10.4) for the destination folder (or its locked parent collection).</param>
        /// <returns>True if the folder was moved successfully. False otherwise</returns>
        public async Task<bool> MoveFolder(string srcFolderPath, string dstFolderPath, bool overwrite = true, string sourceLockToken = null, string destinationLockToken = null, CancellationToken cancellationToken = default)
        {
            // Should have a trailing slash.
            var srcUri = await GetServerUrl(srcFolderPath, true).ConfigureAwait(false);
            var dstUri = await GetServerUrl(dstFolderPath, true).ConfigureAwait(false);

            return await Move(srcUri.Uri, dstUri.Uri, overwrite, sourceLockToken, destinationLockToken, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Move a file on the server
        /// </summary>
        /// <param name="srcFilePath">Source path and filename of the file on the server</param>
        /// <param name="dstFilePath">Destination path and filename of the file on the server</param>
        /// <param name="overwrite">If <c>true</c> (the default), the server is instructed to overwrite an existing destination resource (<c>Overwrite: T</c>, RFC 4918 §10.6). If <c>false</c>, the request fails with <c>412 Precondition Failed</c> when the destination already exists (<c>Overwrite: F</c>).</param>
        /// <param name="sourceLockToken">Optional WebDAV lock token (RFC 4918 §10.4) for the source file. Required when the source is locked, since MOVE removes the source.</param>
        /// <param name="destinationLockToken">Optional WebDAV lock token (RFC 4918 §10.4) for the destination file (or its locked parent collection).</param>
        /// <returns>True if the file was moved successfully. False otherwise</returns>
        public async Task<bool> MoveFile(string srcFilePath, string dstFilePath, bool overwrite = true, string sourceLockToken = null, string destinationLockToken = null, CancellationToken cancellationToken = default)
        {
            // Should not have a trailing slash.
            var srcUri = await GetServerUrl(srcFilePath, false).ConfigureAwait(false);
            var dstUri = await GetServerUrl(dstFilePath, false).ConfigureAwait(false);

            return await Move(srcUri.Uri, dstUri.Uri, overwrite, sourceLockToken, destinationLockToken, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Copies a folder on the server
        /// </summary>
        /// <param name="srcFolderPath">Source path of the folder on the server</param>
        /// <param name="dstFolderPath">Destination path of the folder on the server</param>
        /// <param name="overwrite">If <c>true</c> (the default), the server is instructed to overwrite an existing destination resource (<c>Overwrite: T</c>, RFC 4918 §10.6). If <c>false</c>, the request fails with <c>412 Precondition Failed</c> when the destination already exists (<c>Overwrite: F</c>).</param>
        /// <param name="destinationLockToken">Optional WebDAV lock token (RFC 4918 §10.4) for the destination folder (or its locked parent collection). For COPY only the destination needs a lock token because the source is not modified (RFC 4918 §7.5.1).</param>
        /// <returns>True if the folder was copied successfully. False otherwise</returns>
        public async Task<bool> CopyFolder(string srcFolderPath, string dstFolderPath, bool overwrite = true, string destinationLockToken = null, CancellationToken cancellationToken = default)
        {
            // Should have a trailing slash.
            var srcUri = await GetServerUrl(srcFolderPath, true).ConfigureAwait(false);
            var dstUri = await GetServerUrl(dstFolderPath, true).ConfigureAwait(false);

            return await Copy(srcUri.Uri, dstUri.Uri, overwrite, destinationLockToken, cancellationToken).ConfigureAwait(false);
        }
        
        /// <summary>
        /// Copies a file on the server
        /// </summary>
        /// <param name="srcFilePath">Source path and filename of the file on the server</param>
        /// <param name="dstFilePath">Destination path and filename of the file on the server</param>
        /// <param name="overwrite">If <c>true</c> (the default), the server is instructed to overwrite an existing destination resource (<c>Overwrite: T</c>, RFC 4918 §10.6). If <c>false</c>, the request fails with <c>412 Precondition Failed</c> when the destination already exists (<c>Overwrite: F</c>).</param>
        /// <param name="destinationLockToken">Optional WebDAV lock token (RFC 4918 §10.4) for the destination file (or its locked parent collection). For COPY only the destination needs a lock token because the source is not modified (RFC 4918 §7.5.1).</param>
        /// <returns>True if the file was copied successfully. False otherwise</returns>
        public async Task<bool> CopyFile(string srcFilePath, string dstFilePath, bool overwrite = true, string destinationLockToken = null, CancellationToken cancellationToken = default)
        {
            // Should not have a trailing slash.
            var srcUri = await GetServerUrl(srcFilePath, false).ConfigureAwait(false);
            var dstUri = await GetServerUrl(dstFilePath, false).ConfigureAwait(false);

            return await Copy(srcUri.Uri, dstUri.Uri, overwrite, destinationLockToken, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Acquire an exclusive write lock on a file (RFC 4918 §9.10, <c>Depth: 0</c>).
        /// </summary>
        public async Task<LockInfo> LockFile(string filePath, int timeoutSeconds = 600, string owner = null, CancellationToken cancellationToken = default)
        {
            var uri = await GetServerUrl(filePath, false).ConfigureAwait(false);
            return await Lock(uri.Uri, depth: "0", timeoutSeconds, owner, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Acquire an exclusive write lock on a folder (RFC 4918 §9.10, <c>Depth: infinity</c>).
        /// </summary>
        public async Task<LockInfo> LockFolder(string folderPath, int timeoutSeconds = 600, string owner = null, CancellationToken cancellationToken = default)
        {
            var uri = await GetServerUrl(folderPath, true).ConfigureAwait(false);
            return await Lock(uri.Uri, depth: "infinity", timeoutSeconds, owner, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Release a lock previously acquired with <see cref="LockFile"/> (RFC 4918 §9.11).
        /// </summary>
        public async Task UnlockFile(string filePath, string lockToken, CancellationToken cancellationToken = default)
        {
            var uri = await GetServerUrl(filePath, false).ConfigureAwait(false);
            await Unlock(uri.Uri, lockToken, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Release a lock previously acquired with <see cref="LockFolder"/> (RFC 4918 §9.11).
        /// </summary>
        public async Task UnlockFolder(string folderPath, string lockToken, CancellationToken cancellationToken = default)
        {
            var uri = await GetServerUrl(folderPath, true).ConfigureAwait(false);
            await Unlock(uri.Uri, lockToken, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Refresh an existing lock (RFC 4918 §9.10.2). Sends a body-less LOCK request with the
        /// <c>If</c> header carrying the current lock token and a new <c>Timeout</c> header.
        /// </summary>
        public async Task<LockInfo> RefreshLock(string path, string lockToken, int timeoutSeconds = 600, CancellationToken cancellationToken = default)
        {
            if (timeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Lock timeout must be greater than zero seconds.");

            var bareToken = LockTokenHeaderHelper.NormalizeLockToken(lockToken);

            // Folder vs file is unknown at this entry point — but the URL shape only differs by a
            // trailing slash, and we want to refresh the same href the caller originally locked.
            // GetServerUrl with appendTrailingSlash=false leaves the caller's path as-is (it only
            // adds a slash when asked to), which matches how the original Lock URL was built.
            var uri = await GetServerUrl(path, false).ConfigureAwait(false);

            IDictionary<string, string> headers = new Dictionary<string, string>(2)
            {
                { "If", "(<" + bareToken + ">)" },
                { "Timeout", "Second-" + timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            };

            HttpResponseMessage response = null;
            try
            {
                response = await HttpRequest(uri.Uri, s_lockMethod, headers, content: null, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new WebDAVException((int)response.StatusCode, "Failed refreshing lock.");
                }

                LockInfo info = null;
                if (response.Content != null)
                {
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        info = LockResponseParser.Parse(stream);
                    }
                }

                if (info == null)
                {
                    // Some servers omit the body on refresh. Carry the caller's token through so the
                    // returned LockInfo is still actionable for the next refresh / unlock.
                    info = new LockInfo { Token = bareToken };
                }
                else if (string.IsNullOrEmpty(info.Token))
                {
                    info.Token = bareToken;
                }

                return info;
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// Set a single dead property on a resource (RFC 4918 §9.2 PROPPATCH).
        /// </summary>
        public Task<bool> SetProperty(string path, string propertyName, string propertyNamespace, string value, CancellationToken cancellationToken = default)
        {
            return PropPatch(path, propertyName, propertyNamespace, value, isRemove: false, cancellationToken);
        }

        /// <summary>
        /// Remove a single dead property from a resource (RFC 4918 §9.2 PROPPATCH).
        /// </summary>
        public Task<bool> RemoveProperty(string path, string propertyName, string propertyNamespace, CancellationToken cancellationToken = default)
        {
            return PropPatch(path, propertyName, propertyNamespace, value: null, isRemove: true, cancellationToken);
        }

        #endregion
        
        #region Private methods
        private async Task Delete(Uri listUri, string lockToken, CancellationToken cancellationToken = default)
        {
            // RFC 4918 §9.6.1: DELETE on a collection MUST act as if Depth: infinity
            // were specified, and a client MUST NOT submit any other depth value. We
            // send the header explicitly so strict servers that reject DELETE without
            // it are satisfied; on a non-collection resource the header is harmless.
            IDictionary<string, string> headers = new Dictionary<string, string>(2)
            {
                { "Depth", "infinity" }
            };
            LockTokenHeaderHelper.AddIfHeader(headers, listUri, lockToken, destinationUri: null, destinationLockToken: null);

            var response = await HttpRequest(listUri, HttpMethod.Delete, headers, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK &&
                response.StatusCode != HttpStatusCode.NoContent)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed deleting item.");
            }
        }

        private async Task<Item> Get(Uri listUri, byte[] body, PropFindMode mode, CancellationToken cancellationToken = default)
        {
            // Depth header: http://webdav.org/specs/rfc4918.html#rfc.section.9.1.4
            IDictionary<string, string> headers = new Dictionary<string, string>(1)
            {
                { "Depth", "0" }
            };

            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(listUri, m_propFindMethod, headers, body, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    (int) response.StatusCode != c_httpStatusCode_MultiStatus)
                {
                    throw new WebDAVException((int)response.StatusCode, string.Format("Failed retrieving item/folder (Status Code: {0})", response.StatusCode));
                }

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var result = ResponseParser.ParseItem(stream, mode);

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

        private async Task<bool> Move(Uri srcUri, Uri dstUri, bool overwrite, string sourceLockToken, string destinationLockToken, CancellationToken cancellationToken = default)
        {
            IDictionary<string, string> headers = new Dictionary<string, string>(3)
            {
                { "Destination", dstUri.AbsoluteUri },
                { "Overwrite", overwrite ? "T" : "F" }
            };
            LockTokenHeaderHelper.AddIfHeader(headers, srcUri, sourceLockToken, dstUri, destinationLockToken);

            var response = await HttpRequest(srcUri, m_moveMethod, headers, s_moveRequestContentBytes, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK &&
                response.StatusCode != HttpStatusCode.Created &&
                response.StatusCode != HttpStatusCode.NoContent)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed moving file.");
            }

            return response.IsSuccessStatusCode;
        }

        private async Task<bool> Copy(Uri srcUri, Uri dstUri, bool overwrite, string destinationLockToken, CancellationToken cancellationToken = default)
        {
            IDictionary<string, string> headers = new Dictionary<string, string>(3)
            {
                { "Destination", dstUri.AbsoluteUri },
                { "Overwrite", overwrite ? "T" : "F" }
            };
            // RFC 4918 §7.5.1: source is not modified by COPY, so source lock token is not required.
            LockTokenHeaderHelper.AddIfHeader(headers, srcUri, sourceLockToken: null, destinationUri: dstUri, destinationLockToken: destinationLockToken);

            var response = await HttpRequest(srcUri, m_copyMethod, headers, s_copyRequestContentBytes, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK &&
                response.StatusCode != HttpStatusCode.Created &&
                response.StatusCode != HttpStatusCode.NoContent)
            {
                throw new WebDAVException((int)response.StatusCode, "Failed copying file.");
            }

            return response.IsSuccessStatusCode;
        }

        private async Task<LockInfo> Lock(Uri uri, string depth, int timeoutSeconds, string owner, CancellationToken cancellationToken)
        {
            if (timeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Lock timeout must be greater than zero seconds.");

            var ownerText = string.IsNullOrEmpty(owner) ? c_defaultLockOwner : owner;
            var body = Encoding.UTF8.GetBytes(string.Format(c_lockRequestContentFormat, XmlEscape.Escape(ownerText)));

            IDictionary<string, string> headers = new Dictionary<string, string>(2)
            {
                { "Depth", depth },
                { "Timeout", "Second-" + timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            };

            HttpResponseMessage response = null;
            try
            {
                response = await HttpRequest(uri, s_lockMethod, headers, body, cancellationToken: cancellationToken).ConfigureAwait(false);

                // RFC 4918 §9.10.6: 207 Multi-Status from a depth-infinity LOCK indicates that
                // at least one member could not be locked — the lock as requested was NOT granted
                // and must not be treated as success.
                if (response.StatusCode != HttpStatusCode.OK &&
                    response.StatusCode != HttpStatusCode.Created)
                {
                    throw new WebDAVException((int)response.StatusCode, "Failed acquiring lock.");
                }

                LockInfo info = null;
                if (response.Content != null)
                {
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        info = LockResponseParser.Parse(stream);
                    }
                }

                if (info == null)
                {
                    info = new LockInfo();
                }

                // RFC 4918 §10.5: the canonical lock token of the new lock is the value of the
                // Lock-Token response header. Prefer the header value over any body-derived token
                // (and use the header to populate Token when the body parser came up empty).
                var headerToken = LockTokenHeaderHelper.ExtractLockTokenHeader(response);
                if (!string.IsNullOrEmpty(headerToken))
                {
                    info.Token = headerToken;
                }

                if (string.IsNullOrEmpty(info.Token))
                {
                    throw new WebDAVException((int)response.StatusCode,
                        "Lock response did not contain a lock token in either the Lock-Token header or the response body.");
                }

                return info;
            }
            finally
            {
                response?.Dispose();
            }
        }

        private async Task Unlock(Uri uri, string lockToken, CancellationToken cancellationToken)
        {
            var bareToken = LockTokenHeaderHelper.NormalizeLockToken(lockToken);

            IDictionary<string, string> headers = new Dictionary<string, string>(1)
            {
                { "Lock-Token", "<" + bareToken + ">" }
            };

            using (var response = await HttpRequest(uri, s_unlockMethod, headers, content: null, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    throw new WebDAVException((int)response.StatusCode, "Failed releasing lock.");
                }
            }
        }

        // Build the PROPPATCH request body with XmlWriter so the XML stack handles all
        // escaping / character validity, then check the server's atomic per-property
        // outcome. Used by both SetProperty (isRemove = false) and RemoveProperty
        // (isRemove = true).
        private async Task<bool> PropPatch(string path, string propertyName, string propertyNamespace, string value, bool isRemove, CancellationToken cancellationToken)
        {
            PropPatchRequestBuilder.ValidatePropertyName(propertyName);
            PropPatchRequestBuilder.ValidatePropertyNamespace(propertyNamespace);

            var uri = await GetServerUrl(path, false).ConfigureAwait(false);
            var body = PropPatchRequestBuilder.BuildPropPatchBody(propertyName, propertyNamespace, value, isRemove);

            using (var response = await HttpRequest(uri.Uri, s_propPatchMethod, headers: null, body, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                if ((int)response.StatusCode != c_httpStatusCode_MultiStatus)
                {
                    throw CreateWebDAVException((int)response.StatusCode, "PROPPATCH failed.");
                }

                IList<PropPatchResponseParser.PropStatResult> results;
                if (response.Content == null)
                {
                    throw new WebDAVException((int)response.StatusCode, "PROPPATCH response had no body to confirm property status.");
                }

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    try
                    {
                        results = PropPatchResponseParser.Parse(stream);
                    }
                    catch (System.Xml.XmlException ex)
                    {
                        throw new WebDAVException((int)response.StatusCode, "PROPPATCH response body could not be parsed.", ex);
                    }
                }

                // No propstat at all = ambiguous response. RFC 4918 §9.2 requires the server to
                // report per-property outcomes; treat absence as failure rather than vacuous success.
                if (results.Count == 0)
                {
                    throw new WebDAVException((int)response.StatusCode,
                        "PROPPATCH response did not contain any propstat status — cannot confirm the property change.");
                }

                foreach (var r in results)
                {
                    if (r.StatusCode < 200 || r.StatusCode >= 300)
                    {
                        var msg = "PROPPATCH server reported failure for property '" + propertyName + "': " + (r.StatusLine ?? "(no status line)");
                        if (!string.IsNullOrEmpty(r.ResponseDescription))
                            msg += " — " + r.ResponseDescription;
                        // r.StatusCode may be 0 for a malformed status line; surface 0 verbatim
                        // rather than synthesizing a fake code.
                        throw CreateWebDAVException(r.StatusCode, msg);
                    }
                }

                return true;
            }
        }

        // Centralised so 409 keeps mapping to WebDAVConflictException everywhere.
        private static WebDAVException CreateWebDAVException(int httpCode, string message)
        {
            if (httpCode == (int)HttpStatusCode.Conflict)
                return new WebDAVConflictException(httpCode, message);
            return new WebDAVException(httpCode, message);
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
        // Defends against HTTP header injection (CRLF injection) when callers populate
        // CustomHeaders from untrusted input. Modern .NET's HttpHeaders.Add already
        // throws FormatException for CR/LF in values, but the protection is runtime-
        // dependent and gives a generic error. We validate explicitly so the failure
        // is consistent across runtimes and clearly identifies the offending header.
        private static void EnsureHeaderHasNoCrlf(string key, string value)
        {
            if (key != null && (key.IndexOf('\r') >= 0 || key.IndexOf('\n') >= 0))
            {
                throw new ArgumentException(
                    "Custom header name contains CR/LF characters; refusing to send (possible HTTP header injection).", nameof(key));
            }
            if (value != null && (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0))
            {
                throw new ArgumentException(
                    $"Custom header value for '{key}' contains CR/LF characters; refusing to send (possible HTTP header injection).", nameof(value));
            }
        }

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
                    foreach (var kvp in headers)
                    {
                        request.Headers.Add(kvp.Key, kvp.Value);
                    }
                }

                if (CustomHeaders != null)
                {
                    foreach (var kvp in CustomHeaders)
                    {
                        EnsureHeaderHasNoCrlf(kvp.Key, kvp.Value);
                        request.Headers.Add(kvp.Key, kvp.Value);
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
                    foreach (var kvp in headers)
                    {
                        request.Headers.Add(kvp.Key, kvp.Value);
                    }
                }

                if (CustomHeaders != null)
                {
                    foreach (var kvp in CustomHeaders)
                    {
                        EnsureHeaderHasNoCrlf(kvp.Key, kvp.Value);
                        request.Headers.Add(kvp.Key, kvp.Value);
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
                var root = await Get(baseUri.Uri, PropFindRequestBuilder.BuildAllPropBody(), PropFindMode.AllProp).ConfigureAwait(false);

                m_encodedBasePath = root.Href;
            }

            return BuildServerUrl(path, appendTrailingSlash);
        }

        // Defends against SSRF / open-redirect attacks where a malicious or compromised
        // WebDAV server returns absolute <href> URLs (in PROPFIND multistatus responses)
        // that point at a different host than the configured Server. Any absolute URI
        // that gets fed back into BuildServerUrl must belong to the configured Server.
        private void EnsureSameHost(Uri absoluteUri, string source)
        {
            var serverUri = new Uri(m_server);
            if (!string.Equals(absoluteUri.Host, serverUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Absolute URI host '{absoluteUri.Host}' from {source} does not match the configured Server host '{serverUri.Host}'. " +
                    "Refusing to issue a request to a foreign host (possible SSRF / malicious WebDAV response).");
            }
        }

        // Synchronous core of GetServerUrl. Callers must ensure m_encodedBasePath
        // has already been initialized (i.e. GetServerUrl was awaited at least once).
        private UriBuilder BuildServerUrl(string path, bool appendTrailingSlash)
        {
            // If we've been asked for the "root" folder
            if (string.IsNullOrEmpty(path))
            {
                // If the resolved base path is an absolute URI, use it
                if (TryCreateAbsolute(m_encodedBasePath, out Uri absoluteBaseUri))
                {
                    EnsureSameHost(absoluteBaseUri, "server-resolved base path");
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
                EnsureSameHost(absoluteUri, "absolute path argument");
                return new UriBuilder(absoluteUri);
            }
            else
            {
                // Otherwise, create a URI relative to the server
                UriBuilder baseUri;
                if (TryCreateAbsolute(m_encodedBasePath, out absoluteUri))
                {
                    EnsureSameHost(absoluteUri, "server-resolved base path");
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
