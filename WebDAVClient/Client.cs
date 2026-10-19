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
using WebDAVClient.Authentication;

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
        private bool m_shouldDispose;
        private HttpClientHandler m_handler;
        private string m_server;
        private string m_basePath = "/";
        private string m_encodedBasePath;
        private readonly object m_connectionLock = new object();
        private long m_connectionVersion;
        private int? m_port;
        private bool m_disposedValue;
        private TimeSpan m_responseBodyTimeout = TimeSpan.FromSeconds(100);

        #region WebDAV connection parameters

        /// <summary>
        /// Specify the WebDAV hostname (required).
        /// </summary>
        public string Server
        {
            get { lock (m_connectionLock) return m_server; }
            set
            {
                value = value.TrimEnd('/');
                lock (m_connectionLock)
                {
                    if (m_server == value) return;
                    m_server = value;
                    m_encodedBasePath = null;
                    m_connectionVersion++;
                }
            }
        }

        /// <summary>
        /// Specify the path of a WebDAV directory to use as 'root' (default: /)
        /// </summary>
        public string BasePath
        {
            get { lock (m_connectionLock) return m_basePath; }
            set
            {
                value = value.Trim('/');
                var basePath = string.IsNullOrEmpty(value) ? "/" : "/" + value + "/";
                lock (m_connectionLock)
                {
                    if (m_basePath == basePath) return;
                    m_basePath = basePath;
                    m_encodedBasePath = null;
                    m_connectionVersion++;
                }
            }
        }

        /// <summary>
        /// Specify a port to use
        /// </summary>
        public int? Port
        {
            get { lock (m_connectionLock) return m_port; }
            set
            {
                lock (m_connectionLock)
                {
                    if (m_port == value) return;
                    m_port = value;
                    m_encodedBasePath = null;
                    m_connectionVersion++;
                }
            }
        }

        /// <summary>
        /// Specify the UserAgent (and UserAgent version) string to use in requests
        /// </summary>
        public string UserAgent { get; set; }

        /// <summary>
        /// Specify the UserAgent (and UserAgent version) string to use in requests
        /// </summary>
        public string UserAgentVersion { get; set; }

        /// <summary>
        /// Total time allowed to acquire and parse each metadata response body after
        /// headers arrive. Defaults to 100 seconds, independently of HttpClient.Timeout.
        /// Use Timeout.InfiniteTimeSpan to disable the deadline; caller cancellation still applies.
        /// Does not limit reads from streams returned by Download or DownloadPartial.
        /// </summary>
        public TimeSpan ResponseBodyTimeout
        {
            get => m_responseBodyTimeout;
            set
            {
                if (value != Timeout.InfiniteTimeSpan
                    && (value <= TimeSpan.Zero || value.TotalMilliseconds > uint.MaxValue - 1))
                    throw new ArgumentOutOfRangeException(nameof(value));
                m_responseBodyTimeout = value;
            }
        }

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
            InitializeOwnedHttpClients(credential, authHandler: null, uploadTimeout, proxy);
        }

        /// <summary>
        /// Creates a Client that authenticates every request with a static
        /// <c>Authorization: Bearer &lt;token&gt;</c> header — the standard
        /// transport for OAuth 2.0 / JWT / Azure AD / Nextcloud-app-password
        /// scenarios where no <see cref="ICredentials"/> implementation fits.
        /// </summary>
        /// <param name="bearerToken">The bearer token to send on every request. Required, non-empty.</param>
        /// <param name="uploadTimeout">Optional override for the upload <see cref="System.Net.Http.HttpClient"/>'s timeout.</param>
        /// <param name="proxy">Optional <see cref="IWebProxy"/> to apply to the underlying handler.</param>
        public Client(string bearerToken, TimeSpan? uploadTimeout = null, IWebProxy proxy = null)
        {
            if (string.IsNullOrWhiteSpace(bearerToken))
                throw new ArgumentException("Bearer token must be a non-empty string.", nameof(bearerToken));
            InitializeOwnedHttpClients(credential: null, new BearerTokenAuthenticationHandler(bearerToken), uploadTimeout, proxy);
        }

        /// <summary>
        /// Creates a Client whose every request asks the supplied async
        /// <paramref name="bearerTokenProvider"/> for the current bearer token —
        /// supports refresh / rotation flows (Azure Identity's
        /// <c>TokenCredential</c>, MSAL, IdentityModel, custom OAuth 2.0 token
        /// stores). The provider may return <c>null</c> to send the request
        /// without an <c>Authorization</c> header.
        /// </summary>
        /// <param name="bearerTokenProvider">Async token provider invoked once per outbound request. Required.</param>
        /// <param name="uploadTimeout">Optional override for the upload <see cref="System.Net.Http.HttpClient"/>'s timeout.</param>
        /// <param name="proxy">Optional <see cref="IWebProxy"/> to apply to the underlying handler.</param>
        public Client(Func<CancellationToken, Task<string>> bearerTokenProvider, TimeSpan? uploadTimeout = null, IWebProxy proxy = null)
        {
            if (bearerTokenProvider == null) throw new ArgumentNullException(nameof(bearerTokenProvider));
            InitializeOwnedHttpClients(credential: null, new BearerTokenAuthenticationHandler(bearerTokenProvider), uploadTimeout, proxy);
        }

        private void InitializeOwnedHttpClients(ICredentials credential, DelegatingHandler authHandler, TimeSpan? uploadTimeout, IWebProxy proxy)
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
            else if (authHandler == null)
            {
                // Only fall back to default credentials when no other auth is wired —
                // a Bearer-token Client is intentionally not a Windows-auth Client.
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

            // If an authentication delegating handler was supplied, chain it
            // ahead of the HttpClientHandler so it sees every outbound request
            // (including the upload pipeline). The DelegatingHandler owns the
            // inner handler's lifetime once chained, so disposeHandler on the
            // resulting HttpClient still cleans the whole chain up.
            HttpMessageHandler pipeline = handler;
            if (authHandler != null)
            {
                authHandler.InnerHandler = handler;
                pipeline = authHandler;
            }

            var client = new System.Net.Http.HttpClient(pipeline, disposeHandler: true);
            client.DefaultRequestHeaders.ExpectContinue = false;

            System.Net.Http.HttpClient uploadClient = null;
            if (uploadTimeout != null)
            {
                uploadClient = new System.Net.Http.HttpClient(pipeline, disposeHandler: false);
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
            var uri = BuildOptionsUrl(path);

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
                    throw CreateWebDAVException((int)response.StatusCode, "Failed retrieving server OPTIONS.");
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
            var listUri = await GetServerUrl(path, true, cancellationToken).ConfigureAwait(false);

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
                    throw CreateWebDAVException((int)response.StatusCode, "Failed retrieving items in folder.");
                }

                var items = await ReadMetadataResponse(response,
                    (stream, token) => ResponseParser.ParseItemsAsync(stream, mode, token), cancellationToken).ConfigureAwait(false);
                if (items == null)
                {
                    throw new WebDAVException("Failed deserializing data returned from server.");
                }

                var listUrl = listUri.Uri.AbsoluteUri.TrimEnd('/');

                var result = new List<Item>(items.Count);
                foreach (var item in items)
                {
                    var fullHref = BuildServerUrl(item.Href, false).Uri.AbsoluteUri.TrimEnd('/');
                    if (!string.Equals(fullHref, listUrl, StringComparison.OrdinalIgnoreCase))
                        result.Add(item);
                }
                return result;
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
            var listUri = await GetServerUrl(path, true, cancellationToken).ConfigureAwait(false);
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
            var listUri = await GetServerUrl(path, true, cancellationToken).ConfigureAwait(false);
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
            var listUri = await GetServerUrl(path, true, cancellationToken).ConfigureAwait(false);
            return await Get(listUri.Uri, PropFindRequestBuilder.BuildPropNameBody(), PropFindMode.PropName, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// List all files present on the server.
        /// </summary>
        /// <returns>An item representing the retrieved file</returns>
        public async Task<Item> GetFile(string path = "/", CancellationToken cancellationToken = default)
        {
            var listUri = await GetServerUrl(path, false, cancellationToken).ConfigureAwait(false);
            return await Get(listUri.Uri, PropFindRequestBuilder.BuildAllPropBody(), PropFindMode.AllProp, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// PROPFIND a file for a caller-supplied list of properties (RFC 4918
        /// §9.1 <c>&lt;prop&gt;</c>). See <see cref="GetFolder(string, IEnumerable{PropertyName}, CancellationToken)"/>.
        /// </summary>
        public async Task<Item> GetFile(string path, IEnumerable<PropertyName> properties, CancellationToken cancellationToken = default)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            var listUri = await GetServerUrl(path, false, cancellationToken).ConfigureAwait(false);
            var body = PropFindRequestBuilder.BuildPropBody(properties);
            return await Get(listUri.Uri, body, PropFindMode.NamedProperties, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// PROPFIND <c>&lt;propname/&gt;</c> on a file — discover the property
        /// names the server reports for it.
        /// </summary>
        public async Task<Item> GetFilePropertyNames(string path = "/", CancellationToken cancellationToken = default)
        {
            var listUri = await GetServerUrl(path, false, cancellationToken).ConfigureAwait(false);
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
            return DownloadFile(remoteFilePath, headers, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Download a part of file from the server
        /// </summary>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="startBytes">Inclusive starting byte offset.</param>
        /// <param name="endBytes">Inclusive ending byte offset, clipped at EOF when the server reports a shorter resource.</param>
        /// <returns>A stream with the partial content of the downloaded file</returns>
        public Task<Stream> DownloadPartial(string remoteFilePath, long startBytes, long endBytes, CancellationToken cancellationToken = default)
        {
            if (startBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(startBytes));
            if (endBytes < startBytes)
                throw new ArgumentOutOfRangeException(nameof(endBytes));
            var headers = new Dictionary<string, string>(2)
            { 
                { "translate", "f" }, 
                { "Range", "bytes=" + startBytes + "-" + endBytes } 
            };
            return DownloadFile(remoteFilePath, headers, startBytes, endBytes, cancellationToken);
        }

        /// <summary>
        /// Upload a file to the server
        /// </summary>
        /// <remarks>Once the request is created, it owns and disposes <paramref name="content"/> on success, failure, or cancellation. Callers should still use a using scope because validation/discovery can fail before request creation. Reopen content for any retry.</remarks>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="content"></param>
        /// <param name="name"></param>
        /// <returns>True if the file was uploaded successfully. False otherwise</returns>
        /// <param name="lockToken">Optional WebDAV lock token (RFC 4918 §10.4) for the resource being written. Pass the token returned by <see cref="LockFile"/> / <see cref="LockFolder"/> when the destination resource (or a parent collection) is locked, otherwise the server will reject the request with <c>423 Locked</c>. Bare (<c>opaquelocktoken:...</c>) and angle-bracket-wrapped forms are both accepted.</param>
        public async Task<bool> Upload(string remoteFilePath, Stream content, string name, string lockToken = null, CancellationToken cancellationToken = default)
        {
            // Should not have a trailing slash.
            var uploadUri = await GetServerUrl(remoteFilePath.TrimEnd('/') + "/" + name.TrimStart('/'), false, cancellationToken).ConfigureAwait(false);

            var headers = LockTokenHeaderHelper.BuildLockTokenHeaders(uploadUri.Uri, lockToken);

            HttpResponseMessage response = null;

            try
            {
                response = await HttpUploadRequest(uploadUri.Uri, HttpMethod.Put, content, headers, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    response.StatusCode != HttpStatusCode.NoContent &&
                    response.StatusCode != HttpStatusCode.Created)
                {
                    throw CreateWebDAVException((int)response.StatusCode, "Failed uploading file.");
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
        /// <remarks>Once the request is created, it owns and disposes <paramref name="content"/> on success, failure, or cancellation. Callers should still use a using scope because validation/discovery can fail before request creation. Reopen and reposition content for any retry.</remarks>
        /// <param name="remoteFilePath">Source path and filename of the file on the server</param>
        /// <param name="content">Readable, seekable content to send from its current position to its end.</param>
        /// <param name="name">Name of the file to update</param>
        /// <param name="startBytes">Start byte position of the target content</param>
        /// <param name="endBytes">Exclusive end offset. Must equal <paramref name="startBytes"/> plus the remaining content length.</param>
        /// <returns>True if the file part was uploaded successfully. False otherwise</returns>
        /// <param name="lockToken">Optional WebDAV lock token (RFC 4918 §10.4) for the resource being written. See <see cref="Upload"/> for details.</param>
        public async Task<bool> UploadPartial(string remoteFilePath, Stream content, string name, long startBytes, long endBytes, string lockToken = null, CancellationToken cancellationToken = default)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));
            if (!content.CanRead || !content.CanSeek)
                throw new ArgumentException("Partial upload content must be readable and seekable.", nameof(content));
            if (startBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(startBytes));
            if (endBytes <= startBytes)
                throw new ArgumentOutOfRangeException(nameof(endBytes), "The exclusive end must be greater than the start.");
            if (content.Length - content.Position != endBytes - startBytes)
            {
                throw new InvalidOperationException("The remaining content length must match endBytes minus startBytes.");
            }

            // Should not have a trailing slash.
            var uploadUri = await GetServerUrl(remoteFilePath.TrimEnd('/') + "/" + name.TrimStart('/'), false, cancellationToken).ConfigureAwait(false);

            var headers = LockTokenHeaderHelper.BuildLockTokenHeaders(uploadUri.Uri, lockToken);

            HttpResponseMessage response = null;

            try
            {
                response = await HttpUploadRequest(uploadUri.Uri, HttpMethod.Put, content, headers, startBytes, endBytes, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    response.StatusCode != HttpStatusCode.NoContent &&
                    response.StatusCode != HttpStatusCode.Created)
                {
                    throw CreateWebDAVException((int)response.StatusCode, "Failed uploading file.");
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
            var dirUri = await GetServerUrl(remotePath.TrimEnd('/') + "/" + name.TrimStart('/'), false, cancellationToken).ConfigureAwait(false);

            HttpResponseMessage response = null;

            try
            {
                response = await HttpRequest(dirUri.Uri, m_mkColMethod, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    response.StatusCode != HttpStatusCode.NoContent &&
                    response.StatusCode != HttpStatusCode.Created)
                {
                    throw CreateWebDAVException((int)response.StatusCode, "Failed creating folder.");
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
            var listUri = await GetServerUrl(href, true, cancellationToken).ConfigureAwait(false);
            await Delete(listUri.Uri, lockToken, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes a file from the server.
        /// </summary>
        /// <param name="lockToken">Optional WebDAV lock token (RFC 4918 §10.4) for the file being deleted. Required when the file (or its parent collection) is locked; otherwise the server returns <c>423 Locked</c>.</param>
        public async Task DeleteFile(string href, string lockToken = null, CancellationToken cancellationToken = default)
        {
            var listUri = await GetServerUrl(href, false, cancellationToken).ConfigureAwait(false);
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
            var srcUri = await GetServerUrl(srcFolderPath, true, cancellationToken).ConfigureAwait(false);
            var dstUri = await GetServerUrl(dstFolderPath, true, cancellationToken).ConfigureAwait(false);

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
            var srcUri = await GetServerUrl(srcFilePath, false, cancellationToken).ConfigureAwait(false);
            var dstUri = await GetServerUrl(dstFilePath, false, cancellationToken).ConfigureAwait(false);

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
            var srcUri = await GetServerUrl(srcFolderPath, true, cancellationToken).ConfigureAwait(false);
            var dstUri = await GetServerUrl(dstFolderPath, true, cancellationToken).ConfigureAwait(false);

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
            var srcUri = await GetServerUrl(srcFilePath, false, cancellationToken).ConfigureAwait(false);
            var dstUri = await GetServerUrl(dstFilePath, false, cancellationToken).ConfigureAwait(false);

            return await Copy(srcUri.Uri, dstUri.Uri, overwrite, destinationLockToken, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Acquire an exclusive write lock on a file (RFC 4918 §9.10, <c>Depth: 0</c>).
        /// </summary>
        public async Task<LockInfo> LockFile(string filePath, int timeoutSeconds = 600, string owner = null, CancellationToken cancellationToken = default)
        {
            var uri = await GetServerUrl(filePath, false, cancellationToken).ConfigureAwait(false);
            return await Lock(uri.Uri, depth: "0", timeoutSeconds, owner, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Acquire an exclusive write lock on a folder (RFC 4918 §9.10, <c>Depth: infinity</c>).
        /// </summary>
        public async Task<LockInfo> LockFolder(string folderPath, int timeoutSeconds = 600, string owner = null, CancellationToken cancellationToken = default)
        {
            var uri = await GetServerUrl(folderPath, true, cancellationToken).ConfigureAwait(false);
            return await Lock(uri.Uri, depth: "infinity", timeoutSeconds, owner, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Release a lock previously acquired with <see cref="LockFile"/> (RFC 4918 §9.11).
        /// </summary>
        public async Task UnlockFile(string filePath, string lockToken, CancellationToken cancellationToken = default)
        {
            var uri = await GetServerUrl(filePath, false, cancellationToken).ConfigureAwait(false);
            await Unlock(uri.Uri, lockToken, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Release a lock previously acquired with <see cref="LockFolder"/> (RFC 4918 §9.11).
        /// </summary>
        public async Task UnlockFolder(string folderPath, string lockToken, CancellationToken cancellationToken = default)
        {
            var uri = await GetServerUrl(folderPath, true, cancellationToken).ConfigureAwait(false);
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
            var uri = await GetServerUrl(path, false, cancellationToken).ConfigureAwait(false);

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
                    throw CreateWebDAVException((int)response.StatusCode, "Failed refreshing lock.");
                }

                LockInfo info = null;
                if (response.Content != null)
                {
                    info = await ReadMetadataResponse(response, LockResponseParser.ParseAsync, cancellationToken).ConfigureAwait(false);
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

            HttpResponseMessage response = null;
            try
            {
                response = await HttpRequest(listUri, HttpMethod.Delete, headers, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK &&
                    response.StatusCode != HttpStatusCode.NoContent)
                {
                    throw await CreateMutationException(response, "Failed deleting item.", cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                response?.Dispose();
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
                    throw CreateWebDAVException((int)response.StatusCode, string.Format("Failed retrieving item/folder (Status Code: {0})", response.StatusCode));
                }

                var result = await ReadMetadataResponse(response,
                    (stream, token) => ResponseParser.ParseItemAsync(stream, mode, token), cancellationToken).ConfigureAwait(false);
                if (result == null)
                {
                    throw new WebDAVException("Failed deserializing data returned from server.");
                }
                return result;
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

            using var response = await HttpRequest(srcUri, m_moveMethod, headers, s_moveRequestContentBytes, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK &&
                response.StatusCode != HttpStatusCode.Created &&
                response.StatusCode != HttpStatusCode.NoContent)
            {
                throw await CreateMutationException(response, "Failed moving file.", cancellationToken).ConfigureAwait(false);
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

            using var response = await HttpRequest(srcUri, m_copyMethod, headers, s_copyRequestContentBytes, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK &&
                response.StatusCode != HttpStatusCode.Created &&
                response.StatusCode != HttpStatusCode.NoContent)
            {
                throw await CreateMutationException(response, "Failed copying file.", cancellationToken).ConfigureAwait(false);
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
                    throw CreateWebDAVException((int)response.StatusCode, "Failed acquiring lock.");
                }

                LockInfo info = null;
                if (response.Content != null)
                {
                    info = await ReadMetadataResponse(response, LockResponseParser.ParseAsync, cancellationToken).ConfigureAwait(false);
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
                    throw CreateWebDAVException((int)response.StatusCode, "Failed releasing lock.");
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

            var uri = await GetServerUrl(path, false, cancellationToken).ConfigureAwait(false);
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

                try
                {
                    results = await ReadMetadataResponse(response, PropPatchResponseParser.ParseAsync, cancellationToken).ConfigureAwait(false);
                }
                catch (System.Xml.XmlException ex)
                {
                    throw new WebDAVException((int)response.StatusCode, "PROPPATCH response body could not be parsed.", ex);
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

        private async Task<WebDAVException> CreateMutationException(HttpResponseMessage response, string message, CancellationToken cancellationToken)
        {
            if ((int)response.StatusCode != c_httpStatusCode_MultiStatus)
                return CreateWebDAVException((int)response.StatusCode, message);
            try
            {
                var statuses = await ReadMetadataResponse(response, MultiStatusResponseParser.ParseAsync, cancellationToken).ConfigureAwait(false);
                return new WebDAVException(c_httpStatusCode_MultiStatus, message) { ResourceStatuses = statuses };
            }
            catch (System.Xml.XmlException ex)
            {
                return new WebDAVException(c_httpStatusCode_MultiStatus, message + " Multi-status recovery details could not be parsed.", ex);
            }
        }

        private async Task<T> ReadMetadataResponse<T>(HttpResponseMessage response,
            Func<Stream, CancellationToken, Task<T>> parse, CancellationToken cancellationToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(ResponseBodyTimeout);
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            using var cancellableStream = new CancellationReadStream(stream, deadline.Token);
            var result = await parse(cancellableStream, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }

        // Centralised so 409 keeps mapping to WebDAVConflictException everywhere.
        private static WebDAVException CreateWebDAVException(int httpCode, string message)
        {
            if (httpCode == (int)HttpStatusCode.Conflict)
                return new WebDAVConflictException(httpCode, message);
            return new WebDAVException(httpCode, message);
        }

        private async Task<Stream> DownloadFile(string remoteFilePath, Dictionary<string, string> header, long? rangeStart = null, long? rangeEnd = null, CancellationToken cancellationToken = default)
        {
            // Should not have a trailing slash.
            var downloadUri = await GetServerUrl(remoteFilePath, false, cancellationToken).ConfigureAwait(false);
            var response = await HttpRequest(downloadUri.Uri, HttpMethod.Get, header, cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
                    throw CreateWebDAVException((int)response.StatusCode, "Failed retrieving file.");
                if (rangeStart.HasValue)
                {
                    var range = response.Content.Headers.ContentRange;
                    var expectedEnd = range != null && range.HasLength
                        ? Math.Min(rangeEnd.Value, range.Length.Value - 1) : rangeEnd.Value;
                    if (response.StatusCode != HttpStatusCode.PartialContent || range == null || !range.HasRange
                        || !string.Equals(range.Unit, "bytes", StringComparison.OrdinalIgnoreCase)
                        || range.From != rangeStart || range.To != expectedEnd
                        || (response.Content.Headers.ContentLength.HasValue
                            && response.Content.Headers.ContentLength.Value != range.To.Value - range.From.Value + 1))
                        throw new WebDAVException((int)response.StatusCode, "The server did not return the requested byte range.");
                }
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
                return new ResponseOwnedStream(stream, response);
            }
            catch
            {
                response.Dispose();
                throw;
            }
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
                        request.Content.Headers.ContentRange = new ContentRangeHeaderValue(startBytes.Value, endBytes.Value - 1);
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

        private async Task<UriBuilder> GetServerUrl(string path, bool appendTrailingSlash, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Uri discoveryUri;
                long version;
                lock (m_connectionLock)
                {
                    if (m_encodedBasePath != null)
                        return BuildServerUrl(path, appendTrailingSlash);
                    var baseUri = new UriBuilder(m_server) { Path = m_basePath };
                    if (m_port.HasValue)
                        baseUri.Port = m_port.Value;
                    discoveryUri = baseUri.Uri;
                    version = m_connectionVersion;
                }
                var root = await Get(discoveryUri, PropFindRequestBuilder.BuildAllPropBody(), PropFindMode.AllProp, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                lock (m_connectionLock)
                {
                    // A discovery started before reconfiguration must not repopulate
                    // the new connection's cache with the previous root.
                    if (version != m_connectionVersion)
                        continue;
                    if (TryCreateAbsolute(root.Href, out var absoluteRootUri))
                        EnsureSameOrigin(absoluteRootUri, "server-resolved base path");
                    m_encodedBasePath = root.Href;
                    return BuildServerUrl(path, appendTrailingSlash);
                }
            }
        }

        // A matching hostname alone does not prevent HTTPS downgrades or requests to
        // another service on the same host. Validate the complete configured origin.
        private void EnsureSameOrigin(Uri absoluteUri, string source)
        {
            var serverUriBuilder = new UriBuilder(m_server);
            if (Port.HasValue)
                serverUriBuilder.Port = Port.Value;
            var serverUri = serverUriBuilder.Uri;
            if (!string.Equals(absoluteUri.Scheme, serverUri.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(absoluteUri.Host, serverUri.Host, StringComparison.OrdinalIgnoreCase)
                || absoluteUri.Port != serverUri.Port)
            {
                throw new InvalidOperationException(
                    $"Absolute URI origin '{absoluteUri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped)}' from {source} does not match " +
                    $"the configured Server origin '{serverUri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped)}'. " +
                    "Refusing to issue a cross-origin request (possible SSRF / malicious WebDAV response).");
            }
        }

        // Synchronous core of GetServerUrl. Callers must ensure m_encodedBasePath
        // has already been initialized (i.e. GetServerUrl was awaited at least once).
        private UriBuilder BuildServerUrl(string path, bool appendTrailingSlash)
        {
            lock (m_connectionLock)
            {
                if (m_encodedBasePath == null)
                    throw new InvalidOperationException("Connection configuration changed during the operation; retry it with the current configuration.");
                return BuildResolvedServerUrl(path, appendTrailingSlash);
            }
        }

        private UriBuilder BuildResolvedServerUrl(string path, bool appendTrailingSlash)
        {
            if (TryCreateAbsolute(path, out Uri absoluteUri))
            {
                EnsureSameOrigin(absoluteUri, "absolute path argument");
                return new UriBuilder(absoluteUri);
            }

            UriBuilder baseUri;
            if (TryCreateAbsolute(m_encodedBasePath, out var absoluteBaseUri))
            {
                EnsureSameOrigin(absoluteBaseUri, "server-resolved base path");
                baseUri = new UriBuilder(absoluteBaseUri);
            }
            else
            {
                baseUri = new UriBuilder(m_server) { Path = m_encodedBasePath };
                if (m_port.HasValue)
                    baseUri.Port = m_port.Value;
            }
            return JoinServerPath(baseUri, path, appendTrailingSlash);
        }

        private UriBuilder BuildOptionsUrl(string path)
        {
            lock (m_connectionLock)
            {
                if (TryCreateAbsolute(path, out var absoluteUri))
                {
                    EnsureSameOrigin(absoluteUri, "OPTIONS path argument");
                    return new UriBuilder(absoluteUri);
                }
                var baseUri = new UriBuilder(m_server) { Path = m_basePath };
                if (m_port.HasValue)
                    baseUri.Port = m_port.Value;
                return JoinServerPath(baseUri, path, false);
            }
        }

        private static UriBuilder JoinServerPath(UriBuilder baseUri, string path, bool appendTrailingSlash)
        {
            if (string.IsNullOrEmpty(path))
                return baseUri;

            var rootPath = baseUri.Path.TrimEnd('/');
            var normalizedPath = "/" + path.TrimStart('/');
            var alreadyRooted = path.StartsWith("/", StringComparison.Ordinal)
                && (string.Equals(normalizedPath, rootPath, StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase));
            var finalPath = alreadyRooted ? normalizedPath : rootPath + "/" + path.TrimStart('/');
            if (appendTrailingSlash)
                finalPath = finalPath.TrimEnd('/') + "/";
            baseUri.Path = finalPath;
            return baseUri;
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
