#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using WebDAVClient.Model;
using WebDAVClient.Protocol;

namespace WebDAVClient
{
    /// <summary>
    /// Immutable client defaults. Transport authentication, TLS and redirects belong to HttpClient.
    /// </summary>
    public sealed record DavClientOptions
    {
        /// <summary>
        /// Total metadata-body deadline after headers; independent of HttpClient.Timeout.
        /// </summary>
        public TimeSpan MetadataTimeout
        {
            get;
            init;
        } = TimeSpan.FromSeconds(100);

        /// <summary>
        /// Bounds for buffered and streaming XML responses.
        /// </summary>
        public DavXmlLimits XmlLimits
        {
            get;
            init;
        } = new DavXmlLimits();
    }

    /// <summary>
    /// Request-local options. Collections are copied before the operation first yields.
    /// </summary>
    public record DavRequestOptions
    {
        /// <summary>
        /// Additional non-protocol headers. Typed/protected headers cannot be overridden.
        /// </summary>
        public IReadOnlyDictionary<string, string>? Headers
        {
            get;
            init;
        }

        /// <summary>
        /// HTTP If-Match entity-tag list, including '*'.
        /// </summary>
        public string? IfMatch
        {
            get;
            init;
        }

        /// <summary>
        /// HTTP If-None-Match entity-tag list, including '*'.
        /// </summary>
        public string? IfNoneMatch
        {
            get;
            init;
        }

        /// <summary>
        /// HTTP modification-date validator.
        /// </summary>
        public DateTimeOffset? IfModifiedSince
        {
            get;
            init;
        }

        /// <summary>
        /// HTTP unmodified-date validator.
        /// </summary>
        public DateTimeOffset? IfUnmodifiedSince
        {
            get;
            init;
        }

        /// <summary>
        /// DAV If grammar, independent of HTTP validators. Conditioned requests automatically include
        /// Cache-Control: no-cache and Pragma: no-cache to prevent non-DAV-aware proxy cache hits.
        /// </summary>
        public DavIfConditions? Conditions
        {
            get;
            init;
        }
    }

    /// <summary>
    /// Depth choices supported by PROPFIND; other methods validate their own permitted subset.
    /// </summary>
    public enum DavDepth
    {
        /// <summary>
        /// The addressed resource only.
        /// </summary>
        Zero,

        /// <summary>
        /// The addressed resource and immediate children.
        /// </summary>
        One,

        /// <summary>
        /// All descendants.
        /// </summary>
        Infinity
    }

    /// <summary>
    /// PROPFIND query kind.
    /// </summary>
    public enum DavPropertySelection
    {
        /// <summary>
        /// allprop, optionally augmented with include.
        /// </summary>
        All,

        /// <summary>
        /// Named properties and their values.
        /// </summary>
        Selected,

        /// <summary>
        /// Property names without values.
        /// </summary>
        Names
    }

    /// <summary>
    /// PROPFIND options; allprop does not promise every live property.
    /// </summary>
    public sealed record PropFindOptions : DavRequestOptions
    {
        /// <summary>
        /// Default: resource and immediate children.
        /// </summary>
        public DavDepth Depth
        {
            get;
            init;
        } = DavDepth.One;

        /// <summary>
        /// Default: allprop.
        /// </summary>
        public DavPropertySelection Selection
        {
            get;
            init;
        }

        /// <summary>
        /// Required for Selected; additional include names for All; invalid for Names.
        /// </summary>
        public IReadOnlyList<PropertyName>? Properties
        {
            get;
            init;
        }
    }

    /// <summary>
    /// Property-update options, including narrowly scoped server compatibility.
    /// </summary>
    public sealed record PropPatchOptions : DavRequestOptions
    {
        /// <summary>
        /// Opts into servers known to report a literal '+' as an alias for '%2B' within path segments.
        /// Default false: reserved characters are not universally equivalent under RFC 3986.
        /// Applies only to PROPPATCH response-target confirmation, never query strings, other
        /// reserved escapes, outbound URLs, or additional network requests.
        /// </summary>
        public bool AllowPathPlusEncodingAlias
        {
            get;
            init;
        }
    }

    /// <summary>
    /// Profile-defined REPORT options. The report's profile determines permitted depth values.
    /// </summary>
    public sealed record ReportOptions : DavRequestOptions
    {
        /// <summary>
        /// Default Zero, as specified by the REPORT method. Sync-collection requires Zero.
        /// </summary>
        public DavDepth Depth
        {
            get;
            init;
        }
    }

    /// <summary>
    /// Namespace replacement policy.
    /// </summary>
    public record MoveOptions : DavRequestOptions
    {
        /// <summary>
        /// Explicitly allow replacement. Modern API defaults to false.
        /// </summary>
        public bool Overwrite
        {
            get;
            init;
        }
    }

    /// <summary>
    /// COPY depth and replacement policy.
    /// </summary>
    public sealed record CopyOptions : MoveOptions
    {
        /// <summary>
        /// Zero or Infinity; One is not a valid collection COPY depth.
        /// </summary>
        public DavDepth Depth
        {
            get;
            init;
        } = DavDepth.Infinity;
    }

    /// <summary>
    /// Download options; range endpoints follow HTTP inclusive-end semantics.
    /// </summary>
    public sealed record DownloadOptions : DavRequestOptions
    {
        /// <summary>
        /// One bytes range. A missing start specifies a suffix; a missing end specifies an open-ended range.
        /// </summary>
        public RangeHeaderValue? Range
        {
            get;
            init;
        }

        /// <summary>
        /// Strong ETag or HTTP date. Valid only with Range.
        /// </summary>
        public RangeConditionHeaderValue? IfRange
        {
            get;
            init;
        }
    }

    /// <summary>
    /// PUT content metadata and ownership. No automatic replay or partial PUT is performed.
    /// </summary>
    public sealed record UploadOptions : DavRequestOptions
    {
        /// <summary>
        /// Optional media type; default application/octet-stream.
        /// </summary>
        public string ContentType
        {
            get;
            init;
        } = "application/octet-stream";

        /// <summary>
        /// False retains caller ownership, including failures. True transfers ownership after argument validation;
        /// callers should still use a using scope for pre-request validation failures.
        /// </summary>
        public bool DisposeContent
        {
            get;
            init;
        }
    }

    /// <summary>
    /// LOCK scope.
    /// </summary>
    public enum DavLockScope
    {
        /// <summary>
        /// Exclusive write lock.
        /// </summary>
        Exclusive,

        /// <summary>
        /// Shared write lock, when supported.
        /// </summary>
        Shared
    }

    /// <summary>
    /// Lock acquisition options.
    /// </summary>
    public sealed record LockOptions : DavRequestOptions
    {
        /// <summary>
        /// Default exclusive.
        /// </summary>
        public DavLockScope Scope
        {
            get;
            init;
        }

        /// <summary>
        /// Zero or Infinity; default Zero.
        /// </summary>
        public DavDepth Depth
        {
            get;
            init;
        }

        /// <summary>
        /// Ordered timeout preferences, not a promise of the granted timeout.
        /// </summary>
        public IReadOnlyList<DavLockTimeout> Timeouts
        {
            get;
            init;
        } = new[]
        {
            DavLockTimeout.Finite(TimeSpan.FromMinutes(10))
        };

        /// <summary>
        /// Optional DAV:owner element, preserving structured content.
        /// </summary>
        public DavXmlValue? Owner
        {
            get;
            init;
        }
    }
}
