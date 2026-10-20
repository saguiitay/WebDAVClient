#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using WebDAVClient.Helpers;
using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient
{
    /// <summary>
    /// Resource-oriented WebDAV client with immutable endpoint configuration. Server outcomes are results;
    /// cancellation, transport errors and malformed protocol responses throw. No automatic retries are performed.
    /// </summary>
    public sealed class DavClient : IDisposable
    {
        private readonly System.Net.Http.HttpClient m_transport;
        private readonly bool m_ownsTransport;
        private readonly DavClientOptions m_options;
        private bool m_disposed;
        private static readonly HashSet<string> s_reservedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization", "Proxy-Authorization", "Host", "Cookie", "Content-Length", "Transfer-Encoding",
            "Connection", "Destination", "Depth", "Overwrite", "Lock-Token", "Timeout", "If",
            "If-Match", "If-None-Match", "If-Modified-Since", "If-Unmodified-Since", "If-Range", "Range", "Accept-Encoding"
        };

        /// <summary>Creates an anonymous owned transport with redirects and decompression disabled.</summary>
        /// <param name="endpoint">Absolute HTTP(S) collection URI ending in '/', without query, fragment or user information.</param>
        /// <param name="options">Immutable defaults.</param>
        public DavClient(Uri endpoint, DavClientOptions? options = null)
            : this(endpoint, null, options, true) { }

        /// <summary>Uses a caller-owned transport. Configure auth/TLS and disable automatic redirects on its handler before injection.</summary>
        /// <param name="endpoint">Absolute HTTP(S) collection URI ending in '/', without query, fragment or user information.</param>
        /// <param name="transport">Caller-owned HttpClient; must not use automatic redirects or default protocol/auth headers.</param>
        /// <param name="options">Immutable defaults. HttpClient.Timeout still limits the send independently.</param>
        public DavClient(Uri endpoint, System.Net.Http.HttpClient transport, DavClientOptions? options = null)
            : this(endpoint, transport ?? throw new ArgumentNullException(nameof(transport)), options, false) { }

        private DavClient(Uri endpoint, System.Net.Http.HttpClient? transport, DavClientOptions? options, bool owns)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!endpoint.IsAbsoluteUri || (endpoint.Scheme != "http" && endpoint.Scheme != "https")
                || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
                || !endpoint.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
            {
                throw new ArgumentException("Use an absolute HTTP(S) collection endpoint ending in '/', without user information, query or fragment.", nameof(endpoint));
            }
            m_options = options ?? new DavClientOptions();
            ArgumentNullException.ThrowIfNull(m_options.XmlLimits);
            m_options.XmlLimits.Validate();
            if (m_options.MetadataTimeout != Timeout.InfiniteTimeSpan &&
                (m_options.MetadataTimeout <= TimeSpan.Zero || m_options.MetadataTimeout.TotalMilliseconds > uint.MaxValue - 1))
            {
                throw new ArgumentOutOfRangeException(nameof(options), "MetadataTimeout must be a positive supported deadline or infinite.");
            }
            Endpoint = endpoint;
            m_ownsTransport = owns;
            m_transport = transport ?? new System.Net.Http.HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseDefaultCredentials = false,
                UseCookies = false
            });
        }

        /// <summary>The immutable base collection URI. It is not an access-control boundary.</summary>
        public Uri Endpoint { get; }

        /// <summary>Resolves an escaped reference and enforces scheme/host/port identity without performing discovery or I/O.</summary>
        public Uri Resolve(DavPath path)
        {
            ArgumentNullException.ThrowIfNull(path);
            var uri = new Uri(Endpoint, path.Reference);
            if (!string.Equals(uri.Scheme, Endpoint.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.IdnHost, Endpoint.IdnHost, StringComparison.OrdinalIgnoreCase) || uri.Port != Endpoint.Port
                || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            {
                throw new ArgumentException("Resource must belong to the configured origin and contain no user information or fragment.", nameof(path));
            }
            return uri;
        }

        /// <summary>Retrieves resource-scoped OPTIONS evidence. Missing advertisements remain unknown.</summary>
        public async Task<DavCapabilities> GetCapabilitiesAsync(DavPath path, DavRequestOptions? options = null, CancellationToken cancellationToken = default)
        {
            using var request = CreateRequest("OPTIONS", path, options);
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            var document = await ReadOptionalXmlAsync(response, request, cancellationToken).ConfigureAwait(false);
            return new DavCapabilities(response, request.RequestUri!, Errors(document));
        }

        /// <summary>Retrieves HTTP HEAD metadata without downloading content.</summary>
        public async Task<DavResponse> HeadAsync(DavPath path, DavRequestOptions? options = null, CancellationToken cancellationToken = default)
        {
            using var request = CreateRequest("HEAD", path, options);
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            return new DavResponse(response, request.RequestUri!, "HEAD");
        }

        /// <summary>Returns every PROPFIND resource, including the addressed resource, without legacy filtering.</summary>
        public Task<DavMultiStatusResult> PropFindAsync(DavPath path, PropFindOptions? options = null, CancellationToken cancellationToken = default)
        {
            var request = CreatePropFind(path, options ?? new PropFindOptions());
            return ExecuteMultiStatusAsync(request, requireMultiStatus: true, cancellationToken);
        }

        /// <summary>Opens a response-owning, single-use listing. Enumerate to completion before treating it as a complete observation.</summary>
        public async Task<DavListing> OpenListingAsync(DavPath path, PropFindOptions? options = null, CancellationToken cancellationToken = default)
        {
            using var request = CreatePropFind(path, options ?? new PropFindOptions());
            var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            return new DavListing(response, request.RequestUri!, m_options.XmlLimits, m_options.MetadataTimeout, cancellationToken);
        }

        /// <summary>Submits ordered set/remove instructions in one server-atomic PROPPATCH, including writable DAV properties.</summary>
        public Task<DavMultiStatusResult> UpdatePropertiesAsync(DavPath path, IReadOnlyList<DavPropertyChange> changes,
            DavRequestOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(changes);
            var snapshot = changes.ToArray();
            if (snapshot.Length == 0 || snapshot.Any(c => c == null))
            {
                throw new ArgumentException("Supply at least one non-null property instruction.", nameof(changes));
            }
            var body = new XElement(DavXml.Dav + "propertyupdate", snapshot.Select(c =>
                new XElement(DavXml.Dav + (c.Value == null ? "remove" : "set"),
                    new XElement(DavXml.Dav + "prop", c.Value?.ToElement() ?? new XElement(XName.Get(c.Name.LocalName, c.Name.Namespace))))));
            var request = CreateRequest("PROPPATCH", path, options);
            request.Content = XmlContent(body);
            return ExecuteMultiStatusAsync(request, requireMultiStatus: true, cancellationToken, expectedProperties: snapshot.Select(c => c.Name).Distinct().ToArray());
        }

        /// <summary>Creates a collection with no request body. Conditions can apply to its locked parent.</summary>
        public Task<DavMultiStatusResult> CreateCollectionAsync(DavPath path, DavRequestOptions? options = null, CancellationToken cancellationToken = default) =>
            ExecuteMultiStatusAsync(CreateRequest("MKCOL", path, options), false, cancellationToken);

        /// <summary>Deletes a resource; collection deletion has infinite-depth semantics and may report partial outcomes.</summary>
        public Task<DavMultiStatusResult> DeleteAsync(DavPath path, DavRequestOptions? options = null, CancellationToken cancellationToken = default) =>
            ExecuteMultiStatusAsync(CreateRequest("DELETE", path, options), false, cancellationToken);

        /// <summary>Moves a resource without silently falling back to copy/delete. Replacement defaults to false.</summary>
        public Task<DavMultiStatusResult> MoveAsync(DavPath source, DavPath destination, MoveOptions? options = null, CancellationToken cancellationToken = default) =>
            ExecuteMultiStatusAsync(CreateNamespaceRequest("MOVE", source, destination, options ?? new MoveOptions()), false, cancellationToken);

        /// <summary>Copies a resource at depth Zero or Infinity. Replacement defaults to false.</summary>
        public Task<DavMultiStatusResult> CopyAsync(DavPath source, DavPath destination, CopyOptions? options = null, CancellationToken cancellationToken = default) =>
            ExecuteMultiStatusAsync(CreateNamespaceRequest("COPY", source, destination, options ?? new CopyOptions()), false, cancellationToken);

        /// <summary>Uploads the full representation. Input remains caller-owned unless DisposeContent is explicitly selected.</summary>
        public Task<DavMultiStatusResult> UploadAsync(DavPath path, Stream content, UploadOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(content);
            if (!content.CanRead)
            {
                throw new ArgumentException("Content must be readable.", nameof(content));
            }
            options ??= new UploadOptions();
            var mediaType = MediaTypeHeaderValue.Parse(options.ContentType);
            var request = CreateRequest("PUT", path, options);
            request.Content = options.DisposeContent ? new StreamContent(content) : new BorrowedContent(content);
            request.Content.Headers.ContentType = mediaType;
            return ExecuteMultiStatusAsync(request, false, cancellationToken);
        }

        /// <summary>
        /// Downloads a full representation or validates one requested byte range. Ignored ranges are rejected,
        /// never returned as appendable content. Reads after return use the stream's own cancellation tokens.
        /// </summary>
        public async Task<DavDownloadResult> DownloadAsync(DavPath path, DownloadOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new DownloadOptions();
            var range = options.Range == null ? null : RangeHeaderValue.Parse(options.Range.ToString());
            var ifRange = options.IfRange == null ? null : RangeConditionHeaderValue.Parse(options.IfRange.ToString());
            if (range != null && (range.Unit != "bytes" || range.Ranges.Count != 1 || range.Ranges.Single().To == 0 && !range.Ranges.Single().From.HasValue)
                || ifRange != null && (range == null || ifRange.EntityTag?.IsWeak == true))
            {
                throw new ArgumentException("Use one valid bytes range and a strong If-Range validator.", nameof(options));
            }
            using var request = CreateRequest("GET", path, options);
            request.Headers.Range = range;
            request.Headers.IfRange = ifRange;
            if (range != null)
            {
                request.Headers.AcceptEncoding.ParseAdd("identity");
            }
            var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            try
            {
                if (range != null && (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.PartialContent))
                {
                    ValidateRange(response, request, range.Ranges.Single(), ifRange);
                }
                else if (range == null && response.StatusCode == HttpStatusCode.PartialContent)
                {
                    throw ProtocolError(response, request, "Unrequested partial content.");
                }
                Stream? stream = null;
                if (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.PartialContent)
                {
                    stream = await response.Content.ReadAsStreamAsync(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (response.IsSuccessStatusCode)
                {
                    throw ProtocolError(response, request, "GET returned an unexpected successful status without a representation.");
                }
                var errorDocument = stream == null && response.StatusCode != HttpStatusCode.NotModified
                    ? await ReadOptionalXmlAsync(response, request, cancellationToken).ConfigureAwait(false) : null;
                return new DavDownloadResult(response, request.RequestUri!, stream, Errors(errorDocument));
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        /// <summary>Acquires an exclusive/shared write lock with explicit depth and ordered timeout preferences.</summary>
        public Task<DavLockResult> AcquireLockAsync(DavPath path, LockOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new LockOptions();
            var depth = DepthHeader(options.Depth, allowOne: false);
            if (!Enum.IsDefined(options.Scope))
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
            var timeouts = TimeoutHeader(options.Timeouts);
            if (options.Owner != null && options.Owner.Name != new PropertyName("DAV:", "owner"))
            {
                throw new ArgumentException("Owner must be a DAV:owner element.", nameof(options));
            }
            var body = new XElement(DavXml.Dav + "lockinfo",
                new XElement(DavXml.Dav + "lockscope", new XElement(DavXml.Dav + (options.Scope == DavLockScope.Shared ? "shared" : "exclusive"))),
                new XElement(DavXml.Dav + "locktype", new XElement(DavXml.Dav + "write")), options.Owner?.ToElement());
            var request = CreateRequest("LOCK", path, options);
            request.Headers.Add("Depth", depth);
            request.Headers.Add("Timeout", timeouts);
            request.Content = XmlContent(body);
            return ExecuteLockAsync(request, false, cancellationToken);
        }

        /// <summary>Refreshes a lock without a body. The requested timeout is not substituted for an absent granted timeout.</summary>
        public Task<DavLockResult> RefreshLockAsync(DavPath path, string token, IReadOnlyList<DavLockTimeout>? timeouts = null, CancellationToken cancellationToken = default)
        {
            var conditions = DavIfConditions.Untagged(new DavConditionList(DavCondition.StateToken(token)));
            var timeout = TimeoutHeader(timeouts ?? new[] { DavLockTimeout.Finite(TimeSpan.FromMinutes(10)) });
            var request = CreateRequest("LOCK", path, new DavRequestOptions { Conditions = conditions });
            request.Headers.Add("Timeout", timeout);
            return ExecuteLockAsync(request, true, cancellationToken);
        }

        /// <summary>Releases the identified lock; the token is opaque and must be supplied without brackets.</summary>
        public Task<DavMultiStatusResult> UnlockAsync(DavPath path, string token, CancellationToken cancellationToken = default)
        {
            DavCondition.ValidateToken(token);
            var request = CreateRequest("UNLOCK", path, null);
            request.Headers.Add("Lock-Token", "<" + token + ">");
            return ExecuteMultiStatusAsync(request, false, cancellationToken);
        }

        /// <summary>
        /// Executes a profile-defined XML REPORT through the same origin, condition and transport policies.
        /// This is an extension seam, not a claim of support for every REPORT profile.
        /// </summary>
        public async Task<DavReportResult> ReportAsync(DavPath path, DavXmlValue report, ReportOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(report);
            var depth = DepthHeader(options?.Depth ?? DavDepth.Zero, true);
            using var request = CreateRequest("REPORT", path, options);
            request.Headers.Add("Depth", depth);
            request.Content = XmlContent(report.ToElement());
            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            var document = await ReadOptionalXmlAsync(response, request, cancellationToken, required: response.IsSuccessStatusCode).ConfigureAwait(false);
            var resources = ParseResources(document, response, request);
            return new DavReportResult(response, request.RequestUri!, document == null ? null : new DavXmlValue(document), resources, Errors(document));
        }

        private HttpRequestMessage CreateRequest(string method, DavPath path, DavRequestOptions? options)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            var uri = Resolve(path);
            var headers = options?.Headers?.ToArray();
            var condition = options?.Conditions?.ToHeader(Resolve);
            var request = new HttpRequestMessage(new HttpMethod(method), uri);
            try
            {
                request.Headers.UserAgent.ParseAdd("WebDAVClient/" + typeof(DavClient).Assembly.GetName().Version);
                if (options?.IfMatch != null) { request.Headers.IfMatch.ParseAdd(options.IfMatch); }
                if (options?.IfNoneMatch != null) { request.Headers.IfNoneMatch.ParseAdd(options.IfNoneMatch); }
                request.Headers.IfModifiedSince = options?.IfModifiedSince;
                request.Headers.IfUnmodifiedSince = options?.IfUnmodifiedSince;
                if (condition != null) { request.Headers.Add("If", condition); }
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        if (s_reservedHeaders.Contains(header.Key) || header.Key.Any(c => c == '\r' || c == '\n')
                            || header.Value == null || header.Value.Any(c => c == '\r' || c == '\n'))
                        {
                            throw new ArgumentException("Use typed options or transport configuration for protected headers; CR/LF is not permitted.", nameof(options));
                        }
                        request.Headers.Add(header.Key, header.Value);
                    }
                }
                return request;
            }
            catch
            {
                request.Dispose();
                throw;
            }
        }

        private HttpRequestMessage CreatePropFind(DavPath path, PropFindOptions options)
        {
            var depth = DepthHeader(options.Depth, true);
            var properties = options.Properties?.ToArray() ?? Array.Empty<PropertyName>();
            if (!Enum.IsDefined(options.Selection) || properties.Any(p => p == null)
                || options.Selection == DavPropertySelection.Selected && properties.Length == 0
                || options.Selection == DavPropertySelection.Names && properties.Length != 0)
            {
                throw new ArgumentException("Invalid property selection.", nameof(options));
            }
            var selection = options.Selection == DavPropertySelection.Selected ? "prop" : options.Selection == DavPropertySelection.Names ? "propname" : "allprop";
            var body = new XElement(DavXml.Dav + "propfind", new XElement(DavXml.Dav + selection,
                options.Selection == DavPropertySelection.Selected ? properties.Select(p => new XElement(XName.Get(p.LocalName, p.Namespace))) : null));
            if (options.Selection == DavPropertySelection.All && properties.Length != 0)
            {
                body.Add(new XElement(DavXml.Dav + "include", properties.Select(p => new XElement(XName.Get(p.LocalName, p.Namespace)))));
            }
            var request = CreateRequest("PROPFIND", path, options);
            request.Headers.Add("Depth", depth);
            request.Content = XmlContent(body);
            return request;
        }

        private HttpRequestMessage CreateNamespaceRequest(string method, DavPath source, DavPath destination, MoveOptions options)
        {
            var target = Resolve(destination);
            if (Resolve(source) == target)
            {
                throw new ArgumentException("Source and destination must differ.", nameof(destination));
            }
            var depth = options is CopyOptions copy ? DepthHeader(copy.Depth, false) : null;
            var request = CreateRequest(method, source, options);
            request.Headers.Add("Destination", target.AbsoluteUri);
            request.Headers.Add("Overwrite", options.Overwrite ? "T" : "F");
            if (depth != null) { request.Headers.Add("Depth", depth); }
            return request;
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var response = await m_transport.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.RequestMessage ??= request;
            try
            {
                // An injected handler must prevent redirects before sending; this checks the observable final URI as well.
                Resolve(DavPath.Parse(response.RequestMessage.RequestUri!.AbsoluteUri));
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        private async Task<DavMultiStatusResult> ExecuteMultiStatusAsync(HttpRequestMessage request, bool requireMultiStatus,
            CancellationToken token, IReadOnlyList<PropertyName>? expectedProperties = null)
        {
            using (request)
            using (var response = await SendAsync(request, token).ConfigureAwait(false))
            {
                if (response.IsSuccessStatusCode && requireMultiStatus && (int)response.StatusCode != 207)
                {
                    throw ProtocolError(response, request, "This operation requires a 207 multistatus response.");
                }
                var document = await ReadOptionalXmlAsync(response, request, token, (int)response.StatusCode == 207).ConfigureAwait(false);
                var resources = ParseResources(document, response, request);
                if ((int)response.StatusCode == 207 && request.Method.Method != "PROPFIND" && resources.Count == 0)
                {
                    throw ProtocolError(response, request, "No resource outcomes were reported.");
                }
                if ((int)response.StatusCode == 207 && expectedProperties != null)
                {
                    if (resources.Count != 1 || new Uri(request.RequestUri!, resources[0].Hrefs[0]) != request.RequestUri
                        || expectedProperties.Any(name => !resources[0].GetProperties(name).Any()))
                    {
                        throw ProtocolError(response, request, "PROPPATCH did not report outcomes for every requested property on the target resource.");
                    }
                }
                return new DavMultiStatusResult(response, request.RequestUri!, request.Method.Method, resources, Errors(document),
                    document == null ? null : new DavXmlValue(document));
            }
        }

        private async Task<DavLockResult> ExecuteLockAsync(HttpRequestMessage request, bool refresh, CancellationToken token)
        {
            using (request)
            using (var response = await SendAsync(request, token).ConfigureAwait(false))
            {
                var document = await ReadOptionalXmlAsync(response, request, token, response.IsSuccessStatusCode && !refresh || (int)response.StatusCode == 207).ConfigureAwait(false);
                IReadOnlyList<DavLock> locks = Array.Empty<DavLock>();
                if (response.IsSuccessStatusCode && (int)response.StatusCode != 207 && document != null)
                {
                    try
                    {
                        if (document.Name != DavXml.Dav + "prop")
                        {
                            throw new XmlException("LOCK requires a DAV:prop root.");
                        }
                        locks = DavXml.ParseLocks(document, DateTimeOffset.UtcNow);
                        if (locks.Count == 0)
                        {
                            throw new XmlException("No active locks were reported.");
                        }
                    }
                    catch (XmlException exception)
                    {
                        throw ProtocolError(response, request, "Malformed LOCK response.", exception);
                    }
                }
                return new DavLockResult(response, request.RequestUri!, locks, ParseResources(document, response, request), Errors(document));
            }
        }

        private async Task<XElement?> ReadOptionalXmlAsync(HttpResponseMessage response, HttpRequestMessage request, CancellationToken token, bool required = false)
        {
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var isXml = mediaType == "application/xml" || mediaType == "text/xml" || mediaType?.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) == true;
            if (!required && (response.Content.Headers.ContentLength == 0 || !isXml))
            {
                return null;
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(m_options.MetadataTimeout);
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            using var bound = new CancellationReadStream(stream, deadline.Token);
            try
            {
                var document = await DavXml.ReadDocumentAsync(bound, m_options.XmlLimits, deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
                return document;
            }
            catch (XmlException exception)
            {
                throw ProtocolError(response, request, "Malformed DAV XML response.", exception);
            }
        }

        private static IReadOnlyList<DavResourceResponse> ParseResources(XElement? document, HttpResponseMessage response, HttpRequestMessage request)
        {
            if (document?.Name != DavXml.Dav + "multistatus")
            {
                if ((int)response.StatusCode == 207)
                {
                    throw ProtocolError(response, request, "Expected DAV:multistatus.");
                }
                return Array.Empty<DavResourceResponse>();
            }
            try
            {
                if (document.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)))
                {
                    throw new XmlException("Unexpected text in DAV:multistatus.");
                }
                return Array.AsReadOnly(document.Elements(DavXml.Dav + "response").Select(e => DavXml.ParseResponse(e, false)).ToArray());
            }
            catch (XmlException exception)
            {
                throw ProtocolError(response, request, "Malformed DAV resource response.", exception);
            }
        }

        private static IReadOnlyList<DavXmlValue> Errors(XElement? document) => document?.Name == DavXml.Dav + "error"
            ? Array.AsReadOnly(document.Elements().Select(e => new DavXmlValue(e)).ToArray()) : Array.Empty<DavXmlValue>();

        private static WebDAVException ProtocolError(HttpResponseMessage response, HttpRequestMessage request, string message, Exception? inner = null) =>
            new DavResponse(response, request.RequestUri!, request.Method.Method).CreateException(message, inner);

        private static StringContent XmlContent(XElement element) => new StringContent(element.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "application/xml");

        private static string DepthHeader(DavDepth depth, bool allowOne) => depth switch
        {
            DavDepth.Zero => "0",
            DavDepth.One when allowOne => "1",
            DavDepth.Infinity => "infinity",
            _ => throw new ArgumentOutOfRangeException(nameof(depth))
        };

        private static string TimeoutHeader(IReadOnlyList<DavLockTimeout> timeouts)
        {
            ArgumentNullException.ThrowIfNull(timeouts);
            var values = timeouts.ToArray();
            if (values.Length == 0 || values.Any(t => t == null))
            {
                throw new ArgumentException("At least one timeout preference is required.", nameof(timeouts));
            }
            return string.Join(", ", values.Select(t => t.ToHeader()));
        }

        private static void ValidateRange(HttpResponseMessage response, HttpRequestMessage request, RangeItemHeaderValue requested, RangeConditionHeaderValue? validator)
        {
            var actual = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || actual == null || actual.Unit != "bytes" || !actual.HasRange
                || response.Content.Headers.ContentEncoding.Any(e => e != "identity"))
            {
                throw ProtocolError(response, request, "Server ignored the range or returned an encoded/invalid range.");
            }
            long? start = requested.From ?? (actual.Length.HasValue ? Math.Max(0, actual.Length.Value - requested.To!.Value) : null);
            long? end = requested.To;
            if (!requested.From.HasValue || !end.HasValue)
            {
                end = actual.Length - 1;
            }
            else if (actual.Length.HasValue)
            {
                end = Math.Min(end.Value, actual.Length.Value - 1);
            }
            if (!start.HasValue || !end.HasValue || actual.From != start || actual.To != end
                || actual.Length.HasValue && actual.To >= actual.Length
                || response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength != actual.To - actual.From + 1
                || validator?.EntityTag != null && response.Headers.ETag != null && validator.EntityTag.ToString() != response.Headers.ETag.ToString())
            {
                throw ProtocolError(response, request, "Content-Range or entity tag does not match the requested representation.");
            }
        }

        /// <summary>Disposes only a transport created by this client. Existing response owners remain independently disposable.</summary>
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;
                if (m_ownsTransport) { m_transport.Dispose(); }
            }
        }

        private sealed class BorrowedContent : HttpContent
        {
            private readonly Stream m_content;
            internal BorrowedContent(Stream content) { m_content = content; }
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => m_content.CopyToAsync(stream);
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
                m_content.CopyToAsync(stream, cancellationToken);
            protected override bool TryComputeLength(out long length)
            {
                length = m_content.CanSeek ? m_content.Length - m_content.Position : 0;
                return m_content.CanSeek;
            }
        }
    }
}
