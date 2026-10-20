#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using WebDAVClient.Helpers;
using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient
{
    /// <summary>
    /// Detached HTTP response metadata and DAV error conditions. Server rejection is inspectable data.
    /// </summary>
    public class DavResponse
    {
        internal DavResponse(HttpResponseMessage response, Uri uri, string method, IReadOnlyList<DavXmlValue>? errors = null)
        {
            StatusCode = (int)response.StatusCode;
            EffectiveUri = response.RequestMessage?.RequestUri ?? uri;
            Method = method;
            Headers = new ReadOnlyDictionary<string, IReadOnlyList<string>>(response.Headers
                .Concat(response.Content.Headers).GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)Array.AsReadOnly(g.SelectMany(h => h.Value).ToArray()), StringComparer.OrdinalIgnoreCase));
            Errors = errors ?? Array.Empty<DavXmlValue>();
        }

        /// <summary>
        /// Actual outer HTTP status, never substituted with a member's status.
        /// </summary>
        public int StatusCode
        {
            get;
        }

        /// <summary>
        /// Effective request URI. May contain sensitive application paths; do not log indiscriminately.
        /// </summary>
        public Uri EffectiveUri
        {
            get;
        }

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string Method
        {
            get;
        }

        /// <summary>
        /// Immutable response and content header snapshots.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers
        {
            get;
        }

        /// <summary>
        /// Structured DAV:error conditions, if the response used DAV XML.
        /// </summary>
        public IReadOnlyList<DavXmlValue> Errors
        {
            get;
        }

        /// <summary>
        /// Returns a header's first value, or null if absent.
        /// </summary>
        public string? GetHeader(string name) => Headers.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;

        /// <summary>
        /// Throws for an outer non-2xx status. Does not interpret multistatus members.
        /// </summary>
        public void EnsureHttpSuccess()
        {
            if (StatusCode < 200 || StatusCode >= 300)
            {
                throw CreateException("The server rejected the " + Method + " request.");
            }
        }

        internal WebDAVException CreateException(string message, Exception? inner = null, int? failedStatus = null)
        {
            var exception = StatusCode == 409 || failedStatus == 409
                ? new WebDAVConflictException(StatusCode, message, inner!)
                : new WebDAVException(StatusCode, message, inner!);
            exception.ResponseStatusCode = StatusCode;
            exception.FailedStatusCode = failedStatus;
            using var request = new HttpRequestMessage(new HttpMethod(Method), EffectiveUri);
            return exception.WithRequestContext(request);
        }
    }

    /// <summary>
    /// Buffered core operation result; preserves all resource and property outcomes.
    /// </summary>
    public sealed class DavMultiStatusResult : DavResponse
    {
        internal DavMultiStatusResult(HttpResponseMessage response, Uri uri, string method,
            IReadOnlyList<DavResourceResponse> resources, IReadOnlyList<DavXmlValue>? errors = null, DavXmlValue? document = null)
            : base(response, uri, method, errors)
        {
            Resources = resources;
            Document = document;
        }

        /// <summary>
        /// Complete buffered XML, including root-level extension elements and descriptions.
        /// </summary>
        public DavXmlValue? Document
        {
            get;
        }

        /// <summary>
        /// Responses in wire order; empty for a bodyless single-status result.
        /// </summary>
        public IReadOnlyList<DavResourceResponse> Resources
        {
            get;
        }

        /// <summary>
        /// Requires outer and reported member/property success for core operations.
        /// REPORT profiles must interpret their own outcomes (for example a sync deletion is a 404).
        /// </summary>
        public void EnsureSucceeded()
        {
            EnsureHttpSuccess();
            var confirmed = Method switch
            {
                "PROPFIND" or "PROPPATCH" => StatusCode == 207,
                "PUT" => StatusCode == 200 || StatusCode == 201 || StatusCode == 204,
                "MKCOL" => StatusCode == 201,
                "MOVE" or "COPY" => StatusCode == 201 || StatusCode == 204 || StatusCode == 207,
                "DELETE" => StatusCode == 200 || StatusCode == 204 || StatusCode == 207,
                "UNLOCK" => StatusCode == 204,
                _ => false
            };
            if (!confirmed)
            {
                throw CreateException("The response does not confirm completion of this operation.");
            }
            int? failed = null;
            for (int i = 0; i < Resources.Count && !failed.HasValue; i++)
            {
                var resource = Resources[i];
                if (resource.StatusCode < 200 || resource.StatusCode >= 300)
                {
                    failed = resource.StatusCode;
                    break;
                }
                for (int j = 0; j < resource.PropertyStatuses.Count; j++)
                {
                    var status = resource.PropertyStatuses[j].StatusCode;
                    if (status < 200 || status >= 300)
                    {
                        failed = status;
                        break;
                    }
                }
            }
            if (failed.HasValue)
            {
                var exception = CreateException("A DAV resource or property failed.", failedStatus: failed);
                var statuses = new List<ResourceStatus>();
                for (int i = 0; i < Resources.Count; i++)
                {
                    var resource = Resources[i];
                    if (resource.StatusCode.HasValue)
                    {
                        for (int j = 0; j < resource.Hrefs.Count; j++)
                        {
                            statuses.Add(new ResourceStatus(resource.Hrefs[j], resource.StatusCode.Value, resource.Description!));
                        }
                    }
                }
                exception.ResourceStatuses = statuses.AsReadOnly();
                throw exception;
            }
        }
    }

    /// <summary>
    /// Resource-scoped discovery evidence, not a conformance or authorization guarantee.
    /// </summary>
    public sealed class DavCapabilities : DavResponse
    {
        internal DavCapabilities(HttpResponseMessage response, Uri uri, IReadOnlyList<DavXmlValue> errors)
            : base(response, uri, "OPTIONS", errors)
        {
            ObservedAt = DateTimeOffset.UtcNow;
            DavTokens = Split("DAV");
            AllowedMethods = Split("Allow");
            SearchGrammars = Split("DASL");
        }

        private IReadOnlyList<string> Split(string name) => Array.AsReadOnly(Headers.TryGetValue(name, out var values)
            ? values.SelectMany(SplitCodedValues).Select(v => v.Trim()).Where(v => v.Length != 0).ToArray()
            : Array.Empty<string>());

        private static IEnumerable<string> SplitCodedValues(string value)
        {
            int start = 0;
            bool codedUrl = false;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '<')
                {
                    codedUrl = true;
                }
                else if (value[i] == '>')
                {
                    codedUrl = false;
                }
                else if (value[i] == ',' && !codedUrl)
                {
                    yield return value.Substring(start, i - start);
                    start = i + 1;
                }
            }
            yield return value.Substring(start);
        }

        /// <summary>
        /// Time this observation was obtained.
        /// </summary>
        public DateTimeOffset ObservedAt
        {
            get;
        }

        /// <summary>
        /// Advertised DAV tokens, including unknown tokens.
        /// </summary>
        public IReadOnlyList<string> DavTokens
        {
            get;
        }

        /// <summary>
        /// Advertised methods. An absent method is not proof of permanent lack of support.
        /// </summary>
        public IReadOnlyList<string> AllowedMethods
        {
            get;
        }

        /// <summary>
        /// Advertised DASL grammar identifiers.
        /// </summary>
        public IReadOnlyList<string> SearchGrammars
        {
            get;
        }
    }

    /// <summary>
    /// Profile-defined REPORT result. The profile, not generic HTTP success, determines member semantics.
    /// </summary>
    public sealed class DavReportResult : DavResponse
    {
        internal DavReportResult(HttpResponseMessage response, Uri uri, DavXmlValue? document,
            IReadOnlyList<DavResourceResponse> resources, IReadOnlyList<DavXmlValue> errors)
            : base(response, uri, "REPORT", errors)
        {
            Document = document;
            Resources = resources;
        }

        /// <summary>
        /// Complete XML response including report-specific tokens and extension elements.
        /// </summary>
        public DavXmlValue? Document
        {
            get;
        }

        /// <summary>
        /// Multistatus members, when this report uses that shape.
        /// </summary>
        public IReadOnlyList<DavResourceResponse> Resources
        {
            get;
        }
    }

    /// <summary>
    /// LOCK response retaining every active lock and the separate Lock-Token header.
    /// </summary>
    public sealed class DavLockResult : DavResponse
    {
        internal DavLockResult(HttpResponseMessage response, Uri uri, IReadOnlyList<DavLock> locks,
            IReadOnlyList<DavResourceResponse> resources, IReadOnlyList<DavXmlValue> errors)
            : base(response, uri, "LOCK", errors)
        {
            Locks = locks;
            Resources = resources;
        }

        /// <summary>
        /// Observed active locks. An empty refresh body does not invent a granted timeout.
        /// </summary>
        public IReadOnlyList<DavLock> Locks
        {
            get;
        }

        /// <summary>
        /// Recursive lock failure details. A 207 is not a granted tree-wide lock.
        /// </summary>
        public IReadOnlyList<DavResourceResponse> Resources
        {
            get;
        }

        /// <summary>
        /// Canonical token header, including wire brackets, when present.
        /// </summary>
        public string? LockToken => GetHeader("Lock-Token");

        /// <summary>
        /// Requires a successful non-multistatus LOCK response.
        /// </summary>
        public void EnsureSucceeded()
        {
            EnsureHttpSuccess();
            if (StatusCode != 200 && StatusCode != 201)
            {
                throw CreateException("The requested lock was not confirmed; a multistatus response is not a granted tree lock.");
            }
        }
    }

    /// <summary>
    /// Response-owning GET result. Dispose it even for 304, 412 or 416 outcomes.
    /// </summary>
    public sealed class DavDownloadResult : DavResponse, IDisposable, IAsyncDisposable
    {
        private readonly HttpResponseMessage m_response;

        private int m_disposed;

        internal DavDownloadResult(HttpResponseMessage response, Uri uri, Stream? content, IReadOnlyList<DavXmlValue>? errors = null) : base(response, uri, "GET", errors)
        {
            m_response = response;
            Content = content;
            ETag = response.Headers.ETag?.ToString();
            ContentLength = response.Content.Headers.ContentLength;
            ContentRange = response.Content.Headers.ContentRange?.ToString();
            ContentType = response.Content.Headers.ContentType?.ToString();
            LastModified = response.Content.Headers.LastModified;
        }

        /// <summary>
        /// Content for accepted 200/206 responses, or an empty stream for a full-resource 204.
        /// Caller must dispose this result, including for 204.
        /// </summary>
        public Stream? Content
        {
            get;
        }

        /// <summary>
        /// True for 304; no content stream is supplied.
        /// </summary>
        public bool NotModified => StatusCode == 304;

        /// <summary>
        /// Opaque response entity tag.
        /// </summary>
        public string? ETag
        {
            get;
        }

        /// <summary>
        /// Content length, if known.
        /// </summary>
        public long? ContentLength
        {
            get;
        }

        /// <summary>
        /// Actual Content-Range, if supplied.
        /// </summary>
        public string? ContentRange
        {
            get;
        }

        /// <summary>
        /// Content media type.
        /// </summary>
        public string? ContentType
        {
            get;
        }

        /// <summary>
        /// Reported last-modified timestamp.
        /// </summary>
        public DateTimeOffset? LastModified
        {
            get;
        }

        /// <summary>
        /// Closes content and the HTTP response; does not dispose the client's transport.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_response.Dispose();
            }
        }

        /// <summary>
        /// Releases the response and its content once. HttpResponseMessage owns synchronous disposal.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Single-use streamed PROPFIND response. Completion is true only after the entire XML document is validated.
    /// </summary>
    public sealed class DavListing : DavResponse, IDisposable, IAsyncDisposable
    {
        private readonly HttpResponseMessage m_response;

        private readonly DavXmlLimits m_limits;

        private readonly CancellationTokenSource m_deadline;

        private readonly List<DavXmlValue> m_extensions = new List<DavXmlValue>();

        private int m_started;

        private int m_disposed;

        internal DavListing(HttpResponseMessage response, Uri uri, DavXmlLimits limits, TimeSpan timeout, CancellationToken token,
            IReadOnlyList<DavXmlValue>? errors = null)
            : base(response, uri, "PROPFIND", errors)
        {
            m_response = response;
            m_limits = limits;
            m_deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            m_deadline.CancelAfter(timeout);
            Extensions = m_extensions.AsReadOnly();
        }

        /// <summary>
        /// Root-level descriptions/errors/extensions observed so far; final only when IsComplete is true.
        /// </summary>
        public IReadOnlyList<DavXmlValue> Extensions
        {
            get;
        }

        /// <summary>
        /// False after early disposal, cancellation, rejection, or malformed/truncated XML.
        /// </summary>
        public bool IsComplete
        {
            get;
            private set;
        }

        /// <summary>
        /// Reads each resource once. Per-resource failures are returned as data, not filtered out.
        /// </summary>
        public async IAsyncEnumerable<DavResourceResponse> ReadResourcesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(m_disposed != 0, this);
            if (Interlocked.Exchange(ref m_started, 1) != 0)
            {
                throw new InvalidOperationException("A DAV listing can only be enumerated once.");
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(m_deadline.Token, cancellationToken);
            try
            {
                EnsureHttpSuccess();
                if (StatusCode != 207)
                {
                    throw CreateException("PROPFIND requires a 207 multistatus response.");
                }
                using var stream = await m_response.Content.ReadAsStreamAsync(linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
                using var cancellable = new CancellationReadStream(stream, linked.Token);
                await using var iterator = DavXml.ReadMultiStatusAsync(cancellable, m_limits, cancellationToken: linked.Token,
                    onExtension: value => m_extensions.Add(value)).GetAsyncEnumerator(linked.Token);
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await iterator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (XmlException exception)
                    {
                        throw CreateException("Malformed PROPFIND response.", exception);
                    }
                    if (!hasNext)
                    {
                        break;
                    }
                    yield return iterator.Current;
                }
                linked.Token.ThrowIfCancellationRequested();
                IsComplete = true;
            }
            finally
            {
                Dispose();
            }
        }

        /// <summary>
        /// Cancels reads and releases the response without certifying a complete listing.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_deadline.Cancel();
                m_response.Dispose();
                m_deadline.Dispose();
            }
        }

        /// <summary>
        /// Releases the response and cancels outstanding reads.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
