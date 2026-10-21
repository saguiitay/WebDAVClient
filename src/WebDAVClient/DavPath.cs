#nullable enable
using System;
using System.Linq;

namespace WebDAVClient
{
    /// <summary>
    /// An explicitly escaped URI reference, never a local filesystem path.
    /// </summary>
    public sealed class DavPath
    {
        private DavPath(string reference)
        {
            Reference = reference;
        }

        /// <summary>
        /// The escaped URI reference as supplied.
        /// </summary>
        public string Reference
        {
            get;
        }

        /// <summary>
        /// The configured endpoint itself.
        /// </summary>
        public static DavPath Root
        {
            get;
        } = new DavPath("");

        /// <summary>
        /// Creates an endpoint-relative path by escaping each unescaped segment exactly once.
        /// </summary>
        public static DavPath FromSegments(params string[] segments)
        {
            ArgumentNullException.ThrowIfNull(segments);
            if (segments.Any(s => string.IsNullOrEmpty(s) || s == "." || s == ".." || s.IndexOfAny(new[]
                {
                    '/', '\\'
                }) >= 0))
            {
                throw new ArgumentException("Use nonempty path segments, not separators or dot segments.", nameof(segments));
            }
            return new DavPath(string.Join("/", segments.Select(Uri.EscapeDataString)));
        }

        /// <summary>
        /// Parses an already-escaped URI reference. A leading slash is origin-relative, not endpoint-relative.
        /// Absolute references must pass the client's same-origin policy before use.
        /// </summary>
        public static DavPath Parse(string reference)
        {
            ArgumentNullException.ThrowIfNull(reference);
            if (reference.StartsWith("//", StringComparison.Ordinal) || reference.IndexOfAny(new[]
                {
                    '\\', '#', '\r', '\n'
                }) >= 0
                || reference.Any(char.IsWhiteSpace) || !Uri.TryCreate(reference, UriKind.RelativeOrAbsolute, out _))
            {
                throw new ArgumentException("Use an escaped HTTP URI reference without a fragment or backslash.", nameof(reference));
            }
            for (int i = 0; i < reference.Length; i++)
            {
                if (reference[i] == '%' && (i + 2 >= reference.Length || !Uri.IsHexDigit(reference[i + 1]) || !Uri.IsHexDigit(reference[i + 2])))
                {
                    throw new ArgumentException("Invalid percent escape.", nameof(reference));
                }
            }
            return new DavPath(reference);
        }

        /// <inheritdoc />
        public override string ToString() => Reference;
    }
}
